import { Link, useSearchParams } from 'react-router-dom'

import { TicketPriorityBadge, TicketStatusBadge } from '@/components/ticket-badges'
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
import type { TicketSort } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'
import { formatDateTime, formatDeadlineDistance } from '@/lib/format'

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

      <div className="grid gap-4 rounded-xl border bg-card p-5 shadow-sm sm:grid-cols-2 lg:grid-cols-4">
        <label className="grid gap-1.5 text-xs font-medium">
          Buscar
          <Input
            defaultValue={search}
            placeholder="Código ou título"
            onBlur={(event) => update({ search: event.target.value })}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                update({ search: event.currentTarget.value })
              }
            }}
          />
        </label>

        <label className="grid gap-1.5 text-xs font-medium">
          Status
          <Select
            value={status[0] ?? ''}
            onChange={(event) => update({ status: event.target.value ? [event.target.value] : [] })}
          >
            <option value="">Todos</option>
            {ticketStatuses.map((value) => (
              <option key={value} value={value}>
                {ticketStatusLabels[value]}
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

        {/* Sem responsável e vencidos são as duas filas que o painel do técnico usa. */}
        {isStaff && (
          <div className="flex items-end gap-4 text-sm">
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={unassigned}
                onChange={(event) => update({ unassigned: event.target.checked ? 'true' : null })}
              />
              Sem responsável
            </label>

            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={overdue}
                onChange={(event) => update({ overdue: event.target.checked ? 'true' : null })}
              />
              Vencidos
            </label>
          </div>
        )}
      </div>

      {tickets.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(tickets.error, 'Não foi possível carregar os chamados.')}
        </p>
      )}

      {tickets.data?.items.length === 0 && (
        <p className="text-muted-foreground rounded-lg border p-8 text-center text-sm">
          Nenhum chamado encontrado com esses filtros.
        </p>
      )}

      {tickets.data && tickets.data.items.length > 0 && (
        <div className="overflow-x-auto rounded-xl border bg-card shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs">
              <tr>
                <th className="px-4 py-3 text-left font-medium">Código</th>
                <th className="px-4 py-3 text-left font-medium">Título</th>
                <th className="px-4 py-3 text-left font-medium">Status</th>
                <th className="px-4 py-3 text-left font-medium">Prioridade</th>
                <th className="px-4 py-3 text-left font-medium">Categoria</th>
                {isStaff && <th className="px-4 py-3 text-left font-medium">Solicitante</th>}
                <th className="px-4 py-3 text-left font-medium">Responsável</th>
                <th className="px-4 py-3 text-left font-medium">Resolução</th>
                <th className="px-4 py-3 text-left font-medium">Aberto em</th>
              </tr>
            </thead>

            <tbody>
              {tickets.data.items.map((ticket) => {
                const overdueResolution = !ticket.resolvedAt &&
                  new Date(ticket.slaResolutionDueAt) < new Date()

                return (
                  <tr key={ticket.id} className="border-t transition-colors hover:bg-muted/50">
                    <td className="px-4 py-3 font-mono text-xs">
                      <Link to={`/chamados/${ticket.id}`} className="text-primary hover:underline">
                        {ticket.code}
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <Link to={`/chamados/${ticket.id}`} className="hover:underline">
                        {ticket.title}
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <TicketStatusBadge status={ticket.status} />
                    </td>
                    <td className="px-4 py-3">
                      <TicketPriorityBadge priority={ticket.priority} />
                    </td>
                    <td className="text-muted-foreground px-4 py-3">{ticket.categoryName}</td>
                    {isStaff && (
                      <td className="text-muted-foreground px-4 py-3">{ticket.requesterName}</td>
                    )}
                    <td className="text-muted-foreground px-4 py-3">
                      {ticket.assignedTechnicianName ?? '—'}
                    </td>
                    <td
                      className={
                        overdueResolution ? 'text-sla-overdue px-4 py-3 font-medium' : 'px-4 py-3'
                      }
                    >
                      {ticket.resolvedAt
                        ? 'resolvido'
                        : formatDeadlineDistance(ticket.slaResolutionDueAt)}
                    </td>
                    <td className="text-muted-foreground px-4 py-3 whitespace-nowrap">
                      {formatDateTime(ticket.createdAt)}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
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
