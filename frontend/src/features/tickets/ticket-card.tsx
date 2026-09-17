import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { CircleUser, Clock, Folder, UserRound } from 'lucide-react'

import { TicketPriorityBadge, TicketStatusBadge } from '@/components/ticket-badges'
import type { TicketPriority } from '@/domain/enums'
import { deadlineToneClasses, describeDeadline } from '@/features/tickets/sla-view'
import type { TicketListItem } from '@/features/tickets/types'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * Cartão de chamado da listagem.
 *
 * A hierarquia segue a ordem em que a equipe lê a fila: primeiro o que é (título e
 * código), depois em que pé está (status e prioridade), depois de quem é, e por último o
 * prazo. A faixa colorida à esquerda repete a prioridade em posição fixa, para que uma
 * coluna de cartões possa ser varrida pela borda sem ler texto — o rótulo continua
 * escrito no badge, porque cor sozinha não é informação acessível.
 *
 * O cartão inteiro é o link. Alvo grande funciona melhor no toque e evita a dúvida de
 * onde clicar quando há título e código lado a lado.
 */

const priorityAccent: Record<TicketPriority, string> = {
  Low: 'bg-priority-low',
  Medium: 'bg-priority-medium',
  High: 'bg-priority-high',
  Critical: 'bg-priority-critical',
}

export function TicketCard({ ticket, showRequester }: { ticket: TicketListItem; showRequester: boolean }) {
  const response = describeDeadline(ticket.slaResponseDueAt, ticket.firstRespondedAt, 'respondido')
  const resolution = describeDeadline(ticket.slaResolutionDueAt, ticket.resolvedAt, 'resolvido')

  // Chamado encerrado sai dos indicadores de SLA (README, seção 8): mostrar "vencido"
  // num chamado cancelado seria cobrança de um prazo que ninguém deve mais perseguir.
  const tracksSla = ticket.status !== 'Cancelled' && ticket.status !== 'Closed'

  return (
    <Link
      to={`/chamados/${ticket.id}`}
      className={cn(
        'group relative flex gap-4 overflow-hidden rounded-xl border bg-card p-4 pl-5 shadow-sm',
        'transition-colors hover:border-primary/40 hover:bg-accent/40',
        'focus-visible:ring-ring/50 focus-visible:ring-[3px] focus-visible:outline-none',
      )}
    >
      <span
        aria-hidden
        className={cn('absolute inset-y-0 left-0 w-1.5', priorityAccent[ticket.priority])}
      />

      <div className="min-w-0 flex-1 space-y-3">
        <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-2">
          <div className="min-w-0 space-y-1">
            <p className="text-muted-foreground font-mono text-xs">{ticket.code}</p>
            <h2 className="leading-snug font-medium break-words group-hover:underline">
              {ticket.title}
            </h2>
          </div>

          <div className="flex shrink-0 flex-wrap items-center gap-1.5">
            <TicketStatusBadge status={ticket.status} />
            <TicketPriorityBadge priority={ticket.priority} />
          </div>
        </div>

        <div className="text-muted-foreground flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs">
          <Meta icon={<Folder className="size-3.5" />}>{ticket.categoryName}</Meta>

          {showRequester && (
            <Meta icon={<CircleUser className="size-3.5" />}>{ticket.requesterName}</Meta>
          )}

          <Meta icon={<UserRound className="size-3.5" />}>
            {ticket.assignedTechnicianName ?? (
              <span className="text-sla-due-soon font-medium">Sem responsável</span>
            )}
          </Meta>

          <Meta icon={<Clock className="size-3.5" />}>{formatDateTime(ticket.createdAt)}</Meta>
        </div>

        {tracksSla && (
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t pt-2.5 text-xs">
            <span className="text-muted-foreground">
              Resposta{' '}
              <span className={deadlineToneClasses[response.tone]}>{response.short}</span>
            </span>
            <span className="text-muted-foreground">
              Resolução{' '}
              <span className={deadlineToneClasses[resolution.tone]}>{resolution.short}</span>
            </span>
          </div>
        )}
      </div>
    </Link>
  )
}

function Meta({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <span className="flex min-w-0 items-center gap-1.5">
      {icon}
      <span className="truncate">{children}</span>
    </span>
  )
}

/** Placeholder com a mesma altura do cartão, para a lista não pular ao carregar. */
export function TicketCardSkeleton() {
  return (
    <div className="flex h-[8.5rem] animate-pulse gap-4 rounded-xl border bg-card p-4 pl-5 shadow-sm">
      <div className="flex-1 space-y-3">
        <div className="bg-muted h-3 w-24 rounded" />
        <div className="bg-muted h-4 w-2/3 rounded" />
        <div className="bg-muted h-3 w-1/2 rounded" />
      </div>
    </div>
  )
}
