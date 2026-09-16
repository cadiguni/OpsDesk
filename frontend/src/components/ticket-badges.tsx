import { Badge } from '@/components/ui/badge'
import {
  type TicketPriority,
  type TicketStatus,
  ticketPriorityLabels,
  ticketStatusLabels,
} from '@/domain/enums'
import { cn } from '@/lib/utils'

/**
 * Status e prioridade precisam ser reconhecíveis de relance: o requisito não funcional
 * de usabilidade pede que o técnico identifique chamado crítico e vencido sem ler.
 * Cor sozinha não basta — o rótulo em texto sempre acompanha.
 */

const statusClasses: Record<TicketStatus, string> = {
  Open: 'bg-primary/10 text-primary border-primary/20',
  Triage: 'bg-muted text-muted-foreground',
  InProgress: 'bg-sla-ok/15 text-sla-ok border-sla-ok/30',
  WaitingOnRequester: 'bg-sla-due-soon/15 text-sla-due-soon border-sla-due-soon/30',
  Resolved: 'bg-sla-ok/15 text-sla-ok border-sla-ok/30',
  Closed: 'bg-muted text-muted-foreground',
  Cancelled: 'bg-muted text-muted-foreground line-through',
}

const priorityClasses: Record<TicketPriority, string> = {
  Low: 'bg-priority-low/15 text-priority-low border-priority-low/30',
  Medium: 'bg-priority-medium/15 text-priority-medium border-priority-medium/30',
  High: 'bg-priority-high/15 text-priority-high border-priority-high/30',
  Critical: 'bg-priority-critical/15 text-priority-critical border-priority-critical/40',
}

export function TicketStatusBadge({ status }: { status: TicketStatus }) {
  return (
    <Badge variant="outline" className={cn(statusClasses[status])}>
      {ticketStatusLabels[status]}
    </Badge>
  )
}

export function TicketPriorityBadge({ priority }: { priority: TicketPriority }) {
  return (
    <Badge variant="outline" className={cn(priorityClasses[priority])}>
      {ticketPriorityLabels[priority]}
    </Badge>
  )
}
