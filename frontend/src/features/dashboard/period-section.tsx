import { ArrowDown, ArrowUp, Minus } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'

import type { TechnicianPerformance } from '@/features/tickets/types'
import { useDashboardPeriod } from '@/features/tickets/queries'
import { errorMessage } from '@/lib/api'
import { addDays, startOfDayInSaoPaulo, todayInSaoPaulo } from '@/lib/format'
import { cn } from '@/lib/utils'

const presets = [7, 30, 90] as const

const percent = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 })

/**
 * "Como fomos no período?" (README, seção 13.7): volume, cumprimento de SLA e reabertura,
 * comparados com o período anterior de mesmo tamanho.
 *
 * Números, e não gráfico: cada indicador é um valor só, e a pergunta é "subiu ou desceu".
 * Um tile com a variação responde isso; um gráfico de uma barra só daria mais tinta para
 * a mesma informação.
 */
export function PeriodSection() {
  const [params, setParams] = useSearchParams()
  const requested = Number(params.get('periodo'))
  const days = (presets as readonly number[]).includes(requested) ? requested : 30

  // Dias de calendário de São Paulo: hoje entra inteiro, e o período anterior é o bloco
  // de mesmo tamanho logo antes.
  const today = todayInSaoPaulo()
  const from = startOfDayInSaoPaulo(addDays(today, -(days - 1)))
  const before = startOfDayInSaoPaulo(addDays(today, 1))

  const period = useDashboardPeriod(from, before)
  const comparison = `vs. ${days} dias anteriores`

  return (
    <section aria-labelledby="period-title" className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 id="period-title" className="text-lg font-semibold tracking-tight">
            Desempenho no período
          </h2>
          <p className="text-muted-foreground text-sm">Chamados cancelados ficam fora destes números.</p>
        </div>

        <div role="group" aria-label="Período" className="flex gap-1 rounded-lg border p-1">
          {presets.map((preset) => (
            <button
              key={preset}
              type="button"
              aria-pressed={days === preset}
              onClick={() => {
                const next = new URLSearchParams(params)
                next.set('periodo', String(preset))
                setParams(next, { replace: true })
              }}
              className={cn(
                'rounded-md px-3 py-1 text-sm font-medium transition-colors',
                days === preset ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
              )}
            >
              {preset} dias
            </button>
          ))}
        </div>
      </div>

      {period.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(period.error, 'Não foi possível carregar os indicadores do período.')}
        </p>
      )}

      {period.isPending && <p className="text-muted-foreground text-sm">Carregando indicadores…</p>}

      {period.data && (
        <div className={cn('space-y-4', period.isPlaceholderData && 'opacity-60')}>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            <DeltaTile label="Abertos" kind="count" direction="neutral" comparison={comparison}
              current={period.data.current.opened} previous={period.data.previous.opened} />
            <DeltaTile label="Resolvidos" kind="count" direction="up-is-good" comparison={comparison}
              current={period.data.current.resolved} previous={period.data.previous.resolved} />
            <DeltaTile label="Resposta no prazo" kind="percent" direction="up-is-good" comparison={comparison}
              current={period.data.current.responseCompliance} previous={period.data.previous.responseCompliance}
              detail={detail(period.data.current.responseMet, period.data.current.responseMissed)} />
            <DeltaTile label="Resolução no prazo" kind="percent" direction="up-is-good" comparison={comparison}
              current={period.data.current.resolutionCompliance} previous={period.data.previous.resolutionCompliance}
              detail={detail(period.data.current.resolutionMet, period.data.current.resolutionMissed)} />
            <DeltaTile label="Reabertura" kind="percent" direction="down-is-good" comparison={comparison}
              current={period.data.current.reopenRate} previous={period.data.previous.reopenRate}
              detail={`${period.data.current.reopened} reaberto(s)`} />
          </div>

          <TechnicianTable rows={period.data.byTechnician} />
        </div>
      )}
    </section>
  )
}

function detail(met: number, missed: number): string | undefined {
  return met + missed === 0 ? undefined : `${met} de ${met + missed} no prazo`
}

type Direction = 'up-is-good' | 'down-is-good' | 'neutral'

/**
 * Indicador com a variação contra o período anterior. A variação leva seta e texto para
 * leitor de tela, e a cor diz se a mudança é boa ou ruim — nunca a cor sozinha.
 */
function DeltaTile({
  label,
  kind,
  current,
  previous,
  direction,
  comparison,
  detail,
}: {
  label: string
  kind: 'count' | 'percent'
  current: number | null
  previous: number | null
  direction: Direction
  comparison: string
  detail?: string
}) {
  const value = current === null ? '—' : kind === 'percent' ? `${percent.format(current)}%` : String(current)

  return (
    <div className="rounded-lg border p-4">
      <p className="text-muted-foreground text-xs">{label}</p>
      <p className="text-2xl font-semibold">{value}</p>
      {current === null && <p className="text-muted-foreground text-xs">Nada decidido no período</p>}
      {detail && <p className="text-muted-foreground text-xs">{detail}</p>}
      <Delta kind={kind} current={current} previous={previous} direction={direction} comparison={comparison} />
    </div>
  )
}

function Delta({
  kind,
  current,
  previous,
  direction,
  comparison,
}: {
  kind: 'count' | 'percent'
  current: number | null
  previous: number | null
  direction: Direction
  comparison: string
}) {
  if (current === null || previous === null) {
    return <p className="text-muted-foreground mt-1 text-xs">Sem base de comparação</p>
  }

  const difference = Math.round((current - previous) * 10) / 10

  if (difference === 0) {
    return (
      <p className="text-muted-foreground mt-1 flex items-center gap-1 text-xs">
        <Minus aria-hidden className="size-3" /> Igual {comparison}
      </p>
    )
  }

  const up = difference > 0
  const good = direction === 'neutral' ? null : (direction === 'up-is-good') === up
  const amount = kind === 'percent' ? `${percent.format(Math.abs(difference))} p.p.` : String(Math.abs(difference))
  const Arrow = up ? ArrowUp : ArrowDown

  return (
    <p
      className={cn(
        'mt-1 flex items-center gap-1 text-xs font-medium',
        good === null && 'text-muted-foreground',
        good === true && 'text-emerald-700 dark:text-emerald-400',
        good === false && 'text-sla-overdue',
      )}
    >
      <Arrow aria-hidden className="size-3" />
      {up ? '+' : '−'}
      {amount} {comparison}
      {good !== null && <span className="sr-only">{good ? ', melhorou' : ', piorou'}</span>}
    </p>
  )
}

function TechnicianTable({ rows }: { rows: TechnicianPerformance[] }) {
  if (rows.length === 0) {
    return (
      <p className="text-muted-foreground rounded-lg border border-dashed p-6 text-center text-sm">
        Nenhum chamado resolvido no período.
      </p>
    )
  }

  return (
    <div className="overflow-x-auto rounded-lg border">
      <table className="w-full text-sm">
        <caption className="sr-only">Desempenho por técnico no período</caption>
        <thead className="bg-muted/50 text-muted-foreground text-left text-xs">
          <tr>
            <th scope="col" className="px-4 py-2 font-medium">Técnico</th>
            <th scope="col" className="px-4 py-2 text-right font-medium">Resolvidos</th>
            <th scope="col" className="px-4 py-2 text-right font-medium">Resolução no prazo</th>
            <th scope="col" className="px-4 py-2 text-right font-medium">Reabertos</th>
          </tr>
        </thead>
        <tbody className="divide-y tabular-nums">
          {rows.map((row) => (
            <tr key={row.id}>
              <th scope="row" className="px-4 py-2 text-left font-medium">{row.name}</th>
              <td className="px-4 py-2 text-right">{row.resolved}</td>
              <td className="px-4 py-2 text-right">
                {row.resolutionCompliance === null ? '—' : `${percent.format(row.resolutionCompliance)}%`}
              </td>
              <td className="px-4 py-2 text-right">{row.reopened}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
