import { ticketStatusLabels, ticketStatuses, type TicketStatus } from '@/domain/enums'
import type { NotificationItem } from '@/features/notifications/api'

/**
 * Descreve a notificação em português, como `describeHistoryEntry` faz com o histórico.
 * O backend guarda o tipo e quem agiu; o texto é da interface.
 */
export function describeNotification(item: NotificationItem): string {
  const actor = item.actorName ?? 'O sistema'

  switch (item.kind) {
    case 'Assigned':
      return `${actor} passou o chamado para você`

    case 'Unassigned':
      return `${actor} tirou o chamado de você`

    case 'RequesterReplied':
      return `${actor} respondeu`

    case 'CommentAdded':
      return `${actor} comentou`

    case 'Reopened':
      return `${actor} reabriu o chamado`

    case 'StatusChanged':
      return `${actor} mudou o status para ${statusLabel(item.detail)}`
  }
}

function statusLabel(value: string | null): string {
  return value && (ticketStatuses as readonly string[]).includes(value)
    ? ticketStatusLabels[value as TicketStatus]
    : 'outro'
}
