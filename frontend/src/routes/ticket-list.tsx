import { useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AlarmClock, Search, UserRound, UserX, X } from 'lucide-react'

import { Pagination } from '@/components/pagination'
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
import { BulkStatusBar } from '@/features/tickets/bulk-status-bar'
import { useFilterCategories, useStaff, useTickets } from '@/features/tickets/queries'
import { TicketCard, TicketCardSkeleton } from '@/features/tickets/ticket-card'
import type { TicketSort } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'
import { formatDate, nextDay, startOfDayInSaoPaulo } from '@/lib/format'
import { pageSizeOptions } from '@/lib/pagination'
import { cn } from '@/lib/utils'

const sortLabels: Record<TicketSort, string> = {
  CreatedAtDescending: 'Mais recentes',
  CreatedAtAscending: 'Mais antigos',
  ResolutionDueAtAscending: 'Prazo mais próximo',
  PriorityDescending: 'Prioridade',
  UpdatedAtDescending: 'Atualizados recentemente',
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
  const { isStaff, user } = useSession()
  const [params, setParams] = useSearchParams()
  const categories = useFilterCategories(isStaff)
  const staff = useStaff(isStaff)

  const status = params.getAll('status') as TicketStatus[]
  const priority = params.getAll('priority') as TicketPriority[]
  const categoryId = params.get('categoryId') ?? undefined
  const search = params.get('search') ?? ''
  const unassigned = params.get('unassigned') === 'true'

  // Guardado como `mine=true`, e não com o identificador na URL: a equipe enxerga a fila
  // inteira desde que técnico deixou de ver só o que é dele, e o filtro pessoal virou
  // escolha de quem olha. Assim o mesmo endereço serve a qualquer pessoa da equipe.
  const mine = params.get('mine') === 'true'
  const overdue = params.get('overdue') === 'true'

  // Responsável escolhido na lista da equipe. "Meus chamados" continua como atalho, e os
  // dois se excluem: escolher um limpa o outro.
  const assignedTechnicianId = params.get('assignedTechnicianId') ?? undefined

  // Período em datas de calendário (`yyyy-mm-dd`), inclusivas nas duas pontas, que é como
  // a pessoa pensa "de 1 a 15". A conversão para instante — meia-noite de São Paulo, e o
  // fim como início do dia seguinte — acontece só na hora de chamar a API.
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const sort = (params.get('sort') as TicketSort | null) ?? 'CreatedAtDescending'
  const page = Number(params.get('page') ?? '1')

  // Fora das opções oferecidas, vale o padrão; o servidor limitaria de qualquer forma.
  const requestedSize = Number(params.get('pageSize'))
  const pageSize = (pageSizeOptions as readonly number[]).includes(requestedSize) ? requestedSize : pageSizeOptions[0]

  const tickets = useTickets({
    status: status.length > 0 ? status : undefined,
    priority: priority.length > 0 ? priority : undefined,
    categoryId,
    assignedTechnicianId: mine ? user?.id : assignedTechnicianId,
    unassigned: unassigned || undefined,
    overdue: overdue || undefined,
    search: search || undefined,
    createdFrom: from ? startOfDayInSaoPaulo(from) : undefined,
    createdBefore: to ? startOfDayInSaoPaulo(nextDay(to)) : undefined,
    sort,
    page,
    pageSize,
  })

  // Seleção para a ação em lote, presa aos filtros e à página em que foi feita: trocar
  // qualquer um deles a descarta, sem efeito, porque a chave deixa de bater. Selecionar
  // numa página e aplicar depois de ir para outra agiria sobre chamados fora da vista.
  const view = params.toString()
  const [selection, setSelection] = useState<{ view: string; ids: string[] }>({ view, ids: [] })
  const selectedIds = selection.view === view ? selection.ids : []
  const pageIds = tickets.data?.items.map((ticket) => ticket.id) ?? []
  const allSelected = pageIds.length > 0 && pageIds.every((id) => selectedIds.includes(id))

  function select(ids: string[]) {
    setSelection({ view, ids })
  }

  function toggleSelected(id: string) {
    select(selectedIds.includes(id) ? selectedIds.filter((item) => item !== id) : [...selectedIds, id])
  }

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
    ...(mine ? [{ key: 'mine', label: 'Meus chamados', clear: () => update({ mine: null }) }] : []),
    ...(assignedTechnicianId
      ? [
          {
            key: 'assigned',
            label: staff.data?.find((item) => item.id === assignedTechnicianId)?.name ?? 'Responsável',
            clear: () => update({ assignedTechnicianId: null }),
          },
        ]
      : []),
    ...(from || to
      ? [
          {
            key: 'period',
            label: periodLabel(from, to),
            clear: () => update({ from: null, to: null }),
          },
        ]
      : []),
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
              placeholder="Buscar por código, título ou descrição"
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
                  {category.isActive ? category.name : `${category.name} (desativada)`}
                </option>
              ))}
            </Select>
          </label>

          {isStaff && (
            <label className="grid gap-1.5 text-xs font-medium">
              Responsável
              <Select
                value={mine ? (user?.id ?? '') : (assignedTechnicianId ?? '')}
                onChange={(event) =>
                  update({ assignedTechnicianId: event.target.value, mine: null, unassigned: null })
                }
              >
                <option value="">Todos</option>
                {staff.data?.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.name}
                  </option>
                ))}
              </Select>
            </label>
          )}

          <label className="grid gap-1.5 text-xs font-medium">
            Aberto de
            <Input
              type="date"
              value={from}
              max={to || undefined}
              onChange={(event) => update({ from: event.target.value })}
            />
          </label>

          <label className="grid gap-1.5 text-xs font-medium">
            até
            <Input
              type="date"
              value={to}
              min={from || undefined}
              onChange={(event) => update({ to: event.target.value })}
            />
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
                active={mine}
                onClick={() =>
                  update({ mine: mine ? null : 'true', assignedTechnicianId: null, unassigned: null })
                }
              >
                <UserRound className="size-3.5" /> Meus chamados
              </FilterChip>

              <FilterChip
                active={unassigned}
                onClick={() =>
                  update({ unassigned: unassigned ? null : 'true', mine: null, assignedTechnicianId: null })
                }
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
          {isStaff && (
            <label className="text-muted-foreground flex items-center gap-2 text-sm lg:col-span-2">
              <input
                type="checkbox"
                className="accent-primary size-4"
                checked={allSelected}
                ref={(element) => {
                  if (element) {
                    element.indeterminate = !allSelected && selectedIds.length > 0
                  }
                }}
                onChange={() => select(allSelected ? [] : pageIds)}
              />
              Selecionar todos desta página
            </label>
          )}

          {tickets.data.items.map((ticket) =>
            isStaff ? (
              <div key={ticket.id} className="flex items-start gap-2">
                {/* Fora do cartão, que é um link inteiro: controle dentro de link é ambíguo
                    para teclado e leitor de tela, e o clique abriria o chamado. */}
                <input
                  type="checkbox"
                  aria-label={`Selecionar ${ticket.code}`}
                  className="accent-primary mt-5 size-4 shrink-0"
                  checked={selectedIds.includes(ticket.id)}
                  onChange={() => toggleSelected(ticket.id)}
                />
                <div className="min-w-0 flex-1">
                  <TicketCard ticket={ticket} showRequester />
                </div>
              </div>
            ) : (
              <TicketCard key={ticket.id} ticket={ticket} showRequester={false} />
            ),
          )}
        </div>
      )}

      {isStaff && <BulkStatusBar selectedIds={selectedIds} onClear={() => select([])} onDone={select} />}

      {tickets.data && (
        <Pagination
          page={tickets.data.page}
          totalPages={tickets.data.totalPages}
          totalCount={tickets.data.totalCount}
          noun={['chamado', 'chamados']}
          onPageChange={(next) => update({ page: String(next) })}
          pageSize={pageSize}
          onPageSizeChange={(next) => update({ pageSize: next === pageSizeOptions[0] ? null : String(next) })}
        />
      )}
    </div>
  )
}

/** Rótulo do período no resumo de filtros ativos. */
function periodLabel(from: string, to: string): string {
  // As datas vêm como `yyyy-mm-dd`; formatadas pelo meio-dia de São Paulo, não há risco
  // de o fuso do navegador empurrar o rótulo para o dia anterior.
  const show = (day: string) => formatDate(startOfDayInSaoPaulo(day).replace('T00:00:00', 'T12:00:00'))

  if (from && to) {
    return `${show(from)} a ${show(to)}`
  }

  return from ? `A partir de ${show(from)}` : `Até ${show(to)}`
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
