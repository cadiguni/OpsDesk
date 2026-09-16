import { api } from '@/lib/api'
import type {
  CategoryOption,
  PagedResult,
  TicketDetail,
  TicketListItem,
  TicketQuery,
} from '@/features/tickets/types'
import type { TicketPriority } from '@/domain/enums'

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
