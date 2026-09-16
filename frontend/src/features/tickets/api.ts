import { api } from '@/lib/api'
import type {
  CategoryOption,
  DashboardSummary,
  PagedResult,
  StaffOption,
  TicketComment,
  TicketDetail,
  TicketHistoryEntry,
  TicketListItem,
  TicketQuery,
} from '@/features/tickets/types'
import type { TicketPriority, TicketStatus } from '@/domain/enums'

export type CreateTicketInput = {
  title: string
  description: string
  categoryId: string
  priority: TicketPriority
}

export async function createTicket(input: CreateTicketInput): Promise<TicketDetail> {
  const { data } = await api.post<TicketDetail>('/api/tickets', input)

  return data
}

export async function listTickets(query: TicketQuery): Promise<PagedResult<TicketListItem>> {
  const { data } = await api.get<PagedResult<TicketListItem>>('/api/tickets', {
    params: query,
    // Arrays repetem a chave: ?status=Open&status=Triage, que é o formato que o
    // binding de minimal API espera. O padrão do axios seria status[]=Open.
    paramsSerializer: { indexes: null },
  })

  return data
}

export async function getTicket(id: string): Promise<TicketDetail> {
  const { data } = await api.get<TicketDetail>(`/api/tickets/${id}`)

  return data
}

export async function listCategories(): Promise<CategoryOption[]> {
  const { data } = await api.get<CategoryOption[]>('/api/categories')

  return data
}

export async function listComments(ticketId: string): Promise<TicketComment[]> {
  const { data } = await api.get<TicketComment[]>(`/api/tickets/${ticketId}/comments`)

  return data
}

export async function addComment(
  ticketId: string,
  input: { content: string; isInternal: boolean; closeTicket?: boolean },
): Promise<TicketComment> {
  const { data } = await api.post<TicketComment>(`/api/tickets/${ticketId}/comments`, input)

  return data
}

export async function listHistory(ticketId: string): Promise<TicketHistoryEntry[]> {
  const { data } = await api.get<TicketHistoryEntry[]>(`/api/tickets/${ticketId}/history`)

  return data
}

export async function changeStatus(ticketId: string, status: TicketStatus): Promise<TicketDetail> {
  const { data } = await api.post<TicketDetail>(`/api/tickets/${ticketId}/status`, { status })

  return data
}

export async function assign(
  ticketId: string,
  technicianId: string | null,
): Promise<TicketDetail> {
  const { data } = await api.post<TicketDetail>(`/api/tickets/${ticketId}/assignment`, {
    technicianId,
  })

  return data
}

export async function listStaff(): Promise<StaffOption[]> {
  const { data } = await api.get<StaffOption[]>('/api/staff')

  return data
}

export async function getDashboard(): Promise<DashboardSummary> {
  const { data } = await api.get<DashboardSummary>('/api/dashboard')

  return data
}
