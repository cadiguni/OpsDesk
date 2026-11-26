import { useState } from 'react'
import { ChevronLeft, ChevronRight, Plus, Trash2 } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session-context'
import type { Holiday } from '@/features/holidays/api'
import {
  useAddHoliday,
  useChangeHolidayActivation,
  useDeleteHoliday,
  useHolidays,
} from '@/features/holidays/queries'
import { errorMessage } from '@/lib/api'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'

const weekday = new Intl.DateTimeFormat('pt-BR', { weekday: 'long', timeZone: 'UTC' })

/**
 * Feriados do expediente (README, seção 13.11). Só gestor.
 *
 * Os nacionais vêm do sistema e se renovam sozinhos; aqui o gestor desliga um deles, se a
 * empresa trabalha no dia, e cadastra os que a tabela nacional não tem.
 */
export function HolidayAdmin() {
  const { role } = useSession()
  const [params, setParams] = useSearchParams()
  const year = Number(params.get('ano') ?? new Date().getFullYear())

  const holidays = useHolidays(year, role === 'Manager')

  if (role !== 'Manager') {
    return <p className="text-muted-foreground text-sm">Apenas administradores gerenciam feriados.</p>
  }

  function goTo(next: number) {
    setParams({ ano: String(next) }, { replace: true })
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Feriados</h1>
          <p className="text-muted-foreground text-sm">
            Dias que não contam como expediente no cálculo de SLA.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" aria-label="Ano anterior" onClick={() => goTo(year - 1)}>
            <ChevronLeft />
          </Button>
          <span className="min-w-14 text-center font-semibold tabular-nums">{year}</span>
          <Button variant="outline" size="sm" aria-label="Próximo ano" onClick={() => goTo(year + 1)}>
            <ChevronRight />
          </Button>
        </div>
      </div>

      <AddHolidayForm year={year} />

      <p className="text-muted-foreground text-xs">
        Mudar um feriado vale para os chamados abertos daqui em diante. Prazo de chamado já aberto não é
        recalculado: ele é o compromisso assumido na abertura.
      </p>

      {holidays.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(holidays.error, 'Não foi possível carregar os feriados.')}
        </p>
      )}

      {holidays.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}

      {holidays.data?.length === 0 && (
        <div className="text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
          Nenhum feriado cadastrado em {year}. Os nacionais entram automaticamente para o ano corrente e os dois seguintes.
        </div>
      )}

      {holidays.data && holidays.data.length > 0 && (
        <ul className="divide-y rounded-xl border bg-card shadow-sm">
          {holidays.data.map((holiday) => (
            <HolidayRow key={holiday.id} holiday={holiday} />
          ))}
        </ul>
      )}
    </div>
  )
}

function AddHolidayForm({ year }: { year: number }) {
  const add = useAddHoliday()
  const [date, setDate] = useState('')
  const [name, setName] = useState('')

  return (
    <form
      className="flex flex-wrap items-end gap-3 rounded-xl border bg-card p-4 shadow-sm"
      onSubmit={(event) => {
        event.preventDefault()
        add.mutate(
          { date, name },
          {
            onSuccess: () => {
              setDate('')
              setName('')
            },
          },
        )
      }}
    >
      <Field label="Data">
        {(field) => (
          <Input
            {...field}
            type="date"
            required
            value={date}
            min={`${year - 1}-01-01`}
            onChange={(event) => setDate(event.target.value)}
          />
        )}
      </Field>
      <Field label="Nome" className="min-w-60 flex-1">
        {(field) => (
          <Input
            {...field}
            required
            maxLength={200}
            placeholder="Aniversário da cidade, recesso de fim de ano…"
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        )}
      </Field>
      <Button type="submit" disabled={add.isPending || !date || !name.trim()}>
        <Plus /> Cadastrar feriado
      </Button>

      {add.isError && (
        <p role="alert" className="text-destructive w-full text-sm">
          {errorMessage(add.error, 'Não foi possível cadastrar o feriado.')}
        </p>
      )}
    </form>
  )
}

function HolidayRow({ holiday }: { holiday: Holiday }) {
  const activation = useChangeHolidayActivation()
  const remove = useDeleteHoliday()
  const error = activation.error ?? remove.error

  // Meio-dia UTC: a data é de calendário, e assim nenhum fuso a empurra para o dia anterior.
  const instant = `${holiday.date}T12:00:00Z`

  return (
    <li className={cn('flex flex-wrap items-center gap-4 p-4', !holiday.isActive && 'opacity-60')}>
      <div className="min-w-56 flex-1">
        <p className="flex flex-wrap items-center gap-2 font-medium">
          {holiday.name}
          {holiday.isCustom ? <Badge variant="secondary">Cadastrado</Badge> : <Badge variant="outline">Nacional</Badge>}
          {!holiday.isActive && <Badge variant="outline">Desligado</Badge>}
        </p>
        <p className="text-muted-foreground text-sm">
          {formatDate(instant)} · {weekday.format(new Date(instant))}
        </p>
      </div>

      <div className="flex gap-2">
        <Button
          variant="outline"
          size="sm"
          disabled={activation.isPending}
          onClick={() => activation.mutate({ id: holiday.id, isActive: !holiday.isActive })}
        >
          {holiday.isActive ? 'Desligar' : 'Religar'}
        </Button>

        {holiday.isCustom && (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Apagar ${holiday.name}`}
            disabled={remove.isPending}
            onClick={() => {
              if (window.confirm(`Apagar o feriado "${holiday.name}"?`)) {
                remove.mutate(holiday.id)
              }
            }}
          >
            <Trash2 />
          </Button>
        )}
      </div>

      {error && (
        <p role="alert" className="text-destructive w-full text-sm">
          {errorMessage(error)}
        </p>
      )}
    </li>
  )
}
