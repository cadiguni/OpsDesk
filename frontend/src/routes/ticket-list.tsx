import type { ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AlarmClock, Search, UserX, X } from 'lucide-react'

import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import {
  ticketPriorities,
  ticketPriorityLabels,
  ticketStatuses,
  ticketStatusLabels,
  type TicketPriority,
  type TicketStatus,
} from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { useCategories, useTickets } from '@/features/tickets/queries'
import { TicketCard, TicketCardSkeleton } from '@/features/tickets/ticket-card'
import type { TicketSort } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'
import { cn } from '@/lib/utils'

const sortLabels: Record<TicketSort, string> = {
  CreatedAtDescending: 'Mais recentes',
  CreatedAtAscending: 'Mais antigos',
  ResolutionDueAtAscending: 'Prazo mais próximo',
  PriorityDescending: 'Prioridade',
}

/**
 * Listagem de chamados.
 *
 * Serve tanto a home do usuário (README, 13.3) quanto o painel do técnico (13.6): o que
 * muda entre os dois é o conjunto de chamados, e isso quem decide é o filtro de
 * visibilidade no backend, não esta tela.
 *
 * Os filtros vivem na URL, não em estado local. Assim a página é compartilhável, o botão
 * de voltar do navegador funciona, e recarregar não perde o que a pessoa filtrou.
 */
export function TicketList() {
  const { isStaff } = useSession()
  const [params, setParams] = useSearchParams()
  const categories = useCategories()

  const status = params.getAll('status') as TicketStatus[]
  const priority = params.getAll('priority') as TicketPriority[]
  const categoryId = params.get('categoryId') ?? undefined
  const search = params.get('search') ?? ''
  const unassigned = params.get('unassigned') === 'true'
  const overdue = params.get('overdue') === 'true'
  const sort = (params.get('sort') as TicketSort | null) ?? 'CreatedAtDescending'
  const page = Number(params.get('page') ?? '1')

  const tickets = useTickets({
    status: status.length > 0 ? status : undefined,
    priority: priority.length > 0 ? priority : undefined,
    categoryId,
    unassigned: unassigned || undefined,
    overdue: overdue || undefined,
    search: search || undefined,
    sort,
    page,
  })

  function update(changes: Record<string, string | string[] | null>) {
    const next = new URLSearchParams(params)

    for (const [key, value] of Object.entries(changes)) {
      next.delete(key)

      if (Array.isArray(value)) {
        value.forEach((item) => next.append(key, item))
      } else if (value !== null && value !== '') {
        next.set(key, value)
      }
    }

    // Qualquer mudança de filtro volta para a primeira página: manter a página 5 depois
    // de filtrar mostraria uma lista vazia sem explicação.
    if (!('page' in changes)) {
      next.delete('page')
    }

    setParams(next, { replace: true })
  }

  /** Status é multisseleção: a fila de trabalho raramente é um status só. */
  function toggleStatus(value: TicketStatus) {
    update({
      status: status.includes(value) ? status.filter((item) => item !== value) : [...status, value],
    })
  }

  const activeFilters = [
    ...status.map((value) => ({
      key: `status-${value}`,
      label: ticketStatusLabels[value],
      clear: () => toggleStatus(value),
    })),
    ...priority.map((value) => ({
      key: `priority-${value}`,
      label: ticketPriorityLabels[value],
      clear: () => update({ priority: [] }),
    })),
    ...(categoryId
      ? [
          {
            key: 'category',
            label: categories.data?.find((item) => item.id === categoryId)?.name ?? 'Categoria',
            clear: () => update({ categoryId: null }),
          },
        ]
      : []),
    ...(search ? [{ key: 'search', label: `"${search}"`, clear: () => update({ search: null }) }] : []),
    ...(unassigned
      ? [{ key: 'unassigned', label: 'Sem responsável', clear: () => update({ unassigned: null }) }]
      : []),
    ...(overdue ? [{ key: 'overdue', label: 'Vencidos', clear: () => update({ overdue: null }) }] : []),
  ]

  function clearAll() {
    setParams(new URLSearchParams(), { replace: true })
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            {isStaff ? 'Chamados' : 'Meus chamados'}
          </h1>
          <p className="text-muted-foreground text-sm">
            {tickets.data ? `${tickets.data.totalCount} chamado(s)` : 'Carregando…'}
          </p>
        </div>

        <Link to="/chamados/novo" className={buttonVariants()}>
          Abrir chamado
        </Link>
      </div>

      <div className="space-y-4 rounded-xl border bg-card p-4 shadow-sm">
        {/* Busca e ordenação ficam sempre visíveis: são o que se usa a cada visita. */}
        <div className="flex flex-wrap items-end gap-3">
          <form
            className="relative min-w-60 flex-1"
            onSubmit={(event) => {
              event.preventDefault()
              const field = event.currentTarget.elements.namedItem('search')
              update({ search: field instanceof HTMLInputElement ? field.value : null })
            }}
          >
            <Search
              aria-hidden
              className="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2"
            />
            <Input
              name="search"
              defaultValue={search}
              key={search}
              placeholder="Buscar por código ou título"
              aria-label="Buscar chamados"
              className="pl-9"
              onBlur={(event) => update({ search: event.target.value })}
            />
          </form>

          <label className="grid gap-1.5 text-xs font-medium">
            Ordenar por
            <Select value={sort} onChange={(event) => update({ sort: event.target.value })}>
              {Object.entries(sortLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </Select>
          </label>

          <label className="grid gap-1.5 text-xs font-medium">
            Prioridade
            <Select
              value={priority[0] ?? ''}
              onChange={(event) =>
                update({ priority: event.target.value ? [event.target.value] : [] })
              }
            >
              <option value="">Todas</option>
              {ticketPriorities.map((value) => (
                <option key={value} value={value}>
                  {ticketPriorityLabels[value]}
                </option>
              ))}
            </Select>
          </label>

          <label className="grid gap-1.5 text-xs font-medium">
            Categoria
            <Select
              value={categoryId ?? ''}
              onChange={(event) => update({ categoryId: event.target.value })}
            >
              <option value="">Todas</option>
              {categories.data?.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </Select>
          </label>
        </div>

        {/* Status vira faixa de alternadores em vez de select: são sete valores, a
            combinação é comum, e assim o estado do filtro fica visível sem abrir nada. */}
        <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filtrar por status">
          <FilterChip active={status.length === 0} onClick={() => update({ status: [] })}>
            Todos
          </FilterChip>

          {ticketStatuses.map((value) => (
            <FilterChip
              key={value}
              active={status.includes(value)}
              onClick={() => toggleStatus(value)}
            >
              {ticketStatusLabels[value]}
            </FilterChip>
          ))}

          {/* Sem responsável e vencidos são as duas filas que o painel do técnico usa. */}
          {isStaff && (
            <>
              <span aria-hidden className="bg-border mx-1 h-5 w-px" />

              <FilterChip
                active={unassigned}
                onClick={() => update({ unassigned: unassigned ? null : 'true' })}
              >
                <UserX className="size-3.5" /> Sem responsável
              </FilterChip>

              <FilterChip
                active={overdue}
                onClick={() => update({ overdue: overdue ? null : 'true' })}
              >
                <AlarmClock className="size-3.5" /> Vencidos
              </FilterChip>
            </>
          )}
        </div>

        {activeFilters.length > 0 && (
          <div className="flex flex-wrap items-center gap-2 border-t pt-3 text-xs">
            <span className="text-muted-foreground">Filtros ativos:</span>

            {activeFilters.map((filter) => (
              <button
                key={filter.key}
                type="button"
                onClick={filter.clear}
                className="bg-muted hover:bg-accent flex items-center gap-1 rounded-md px-2 py-1 font-medium"
              >
                {filter.label}
                <X className="size-3" />
                <span className="sr-only">Remover filtro</span>
              </button>
            ))}

            <Button variant="ghost" size="sm" className="ml-auto" onClick={clearAll}>
              Limpar tudo
            </Button>
          </div>
        )}
      </div>

      {tickets.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(tickets.error, 'Não foi possível carregar os chamados.')}
        </p>
      )}

      {tickets.isPending && (
        <div className="grid gap-3 lg:grid-cols-2">
          {Array.from({ length: 6 }, (_, index) => (
            <TicketCardSkeleton key={index} />
          ))}
        </div>
      )}

      {tickets.data?.items.length === 0 && (
        <div className="text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
          <p>Nenhum chamado encontrado com esses filtros.</p>
          {activeFilters.length > 0 && (
            <Button variant="outline" size="sm" className="mt-3" onClick={clearAll}>
              Limpar filtros
            </Button>
          )}
        </div>
      )}

      {tickets.data && tickets.data.items.length > 0 && (
        <div
          className={cn(
            'grid gap-3 lg:grid-cols-2',
            // Enquanto a próxima página carrega, a atual continua na tela; o esmaecido
            // avisa que o conteúdo está desatualizado sem tirar nada do lugar.
            tickets.isPlaceholderData && 'opacity-60',
          )}
        >
          {tickets.data.items.map((ticket) => (
            <TicketCard key={ticket.id} ticket={ticket} showRequester={isStaff} />
          ))}
        </div>
      )}

      {tickets.data && tickets.data.totalPages > 1 && (
        <div className="flex items-center justify-between text-sm">
          <p className="text-muted-foreground">
            Página {tickets.data.page} de {tickets.data.totalPages}
          </p>

          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={tickets.data.page <= 1}
              onClick={() => update({ page: String(tickets.data.page - 1) })}
            >
              Anterior
            </Button>

            <Button
              variant="outline"
              size="sm"
              disabled={!tickets.data.hasNextPage}
              onClick={() => update({ page: String(tickets.data.page + 1) })}
            >
              Próxima
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

function FilterChip({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={cn(
        'flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium transition-colors',
        'focus-visible:ring-ring/50 focus-visible:ring-[3px] focus-visible:outline-none',
        active
          ? 'border-primary bg-primary/10 text-primary'
          : 'text-muted-foreground hover:bg-accent hover:text-foreground',
      )}
    >
      {children}
    </button>
  )
}
