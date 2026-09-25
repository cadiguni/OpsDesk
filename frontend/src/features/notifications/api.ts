import type { PagedResult } from '@/features/tickets/types'
import { api } from '@/lib/api'

export type NotificationKind =
  | 'Assigned'
  | 'Unassigned'
  | 'RequesterReplied'
  | 'CommentAdded'
  | 'StatusChanged'
  | 'Reopened'
  | 'SlaDueSoon'
  | 'SlaOverdue'

export type NotificationItem = {
  id: string
  kind: NotificationKind
  ticketId: string
  ticketCode: string
  ticketTitle: string
  actorName: string | null
  /** O status novo em `StatusChanged`; o prazo (`Response`, `Resolution`) nos alertas de SLA. */
  detail: string | null
  createdAt: string
  readAt: string | null
}

export async function listNotifications(query: {
  unreadOnly?: boolean
  page?: number
}): Promise<PagedResult<NotificationItem>> {
  const { data } = await api.get<PagedResult<NotificationItem>>('/api/notifications', {
    params: query,
  })

  return data
}

export async function countUnread(): Promise<number> {
  const { data } = await api.get<{ count: number }>('/api/notifications/unread-count')

  return data.count
}

export async function markRead(id: string): Promise<void> {
  await api.post(`/api/notifications/${id}/read`)
}

export async function markAllRead(): Promise<void> {
  await api.post('/api/notifications/read-all')
}
