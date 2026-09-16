import {
  ticketPriorityLabels,
  ticketStatusLabels,
  type TicketPriority,
  type TicketStatus,
} from '@/domain/enums'
import type { TicketHistoryEntry } from '@/features/tickets/types'

/**
 * Descreve um evento do histórico em português.
 *
 * O backend guarda o valor cru — `"Open"`, `"InProgress"`, ou um identificador quando o
 * campo é categoria ou responsável. Guardar o identificador em vez do nome é deliberado:
 * nome de categoria pode ser editado depois, e o histórico passaria a mentir. A tradução
 * para algo legível acontece aqui, e para os identificadores a interface mostra o evento
 * sem tentar resolvê-los — resolver exigiria uma consulta por linha do histórico.
 */
export function describeHistoryEntry(entry: TicketHistoryEntry): string {
  const author = entry.changedByName ?? 'Sistema'

  switch (entry.action) {
    case 'Created':
      return `${author} abriu o chamado`

    case 'StatusChanged':
      return `${author} mudou o status de ${statusLabel(entry.previousValue)} para ${statusLabel(entry.newValue)}`

    case 'PriorityChanged':
      return `${author} mudou a prioridade de ${priorityLabel(entry.previousValue)} para ${priorityLabel(entry.newValue)}`

    case 'CategoryChanged':
      return `${author} mudou a categoria`

    case 'TechnicianAssigned':
      return `${author} definiu o responsável`

    case 'TechnicianUnassigned':
      return `${author} removeu o responsável`

    case 'CommentAdded':
      return `${author} comentou`

    case 'InternalCommentAdded':
      return `${author} registrou um comentário interno`

    case 'Resolved':
      return `${author} marcou como resolvido`

    case 'Closed':
      return `${author} fechou o chamado`

    case 'Cancelled':
      return `${author} cancelou o chamado`
  }
}

function statusLabel(value: string | null): string {
  return value && value in ticketStatusLabels
    ? ticketStatusLabels[value as TicketStatus]
    : (value ?? '—')
}

function priorityLabel(value: string | null): string {
  return value && value in ticketPriorityLabels
    ? ticketPriorityLabels[value as TicketPriority]
    : (value ?? '—')
}
