import { api } from '@/lib/api'
import type {
  Attachment,
  CategoryOption,
  DashboardSummary,
  PagedResult,
  StaffOption,
  TicketComment,
  TicketDetail,
  TicketHistoryEntry,
  TicketListItem,
  TicketQuery,
  UserOption,
} from '@/features/tickets/types'
import type { TicketPriority, TicketStatus } from '@/domain/enums'

export type CreateTicketInput = {
  title: string
  description: string
  categoryId: string
  priority: TicketPriority

  /** Abertura em nome de outra pessoa. Só a equipe pode preencher; vazio é o próprio. */
  requesterId?: string

  /** Anexos já enviados e pendentes, vinculados ao chamado na abertura. */
  attachmentIds?: string[]
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
  input: {
    content: string
    isInternal: boolean
    closeTicket?: boolean
    attachmentIds?: string[]
  },
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

/**
 * Define ou remove o responsável.
 *
 * Devolve `null` quando a API responde 204: a atribuição valeu, mas o chamado saiu da
 * visibilidade de quem atribuiu — o técnico que encaminha para um colega deixa de
 * enxergar o chamado no mesmo instante.
 */
/**
 * Troca prioridade e categoria. Campo ausente quer dizer "nao mexa".
 *
 * Trocar a prioridade recalcula os prazos de SLA no servidor, contados da abertura do
 * chamado — por isso a resposta inteira volta e substitui o detalhe em cache.
 */
export async function changeClassification(
  ticketId: string,
  input: { priority?: TicketPriority; categoryId?: string },
): Promise<TicketDetail> {
  const { data } = await api.post<TicketDetail>(
    `/api/tickets/${ticketId}/classification`,
    input,
  )

  return data
}

export async function assign(
  ticketId: string,
  technicianId: string | null,
): Promise<TicketDetail | null> {
  const response = await api.post<TicketDetail>(`/api/tickets/${ticketId}/assignment`, {
    technicianId,
  })

  return response.status === 204 ? null : response.data
}

export async function listUsers(search?: string): Promise<UserOption[]> {
  const { data } = await api.get<UserOption[]>('/api/users', {
    params: search ? { search } : undefined,
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

// ----- Anexos -----

/**
 * Envia um arquivo, que fica pendente até a abertura ou o comentário vinculá-lo.
 *
 * O envio acontece antes de existir chamado ou comentário porque o formulário de abertura
 * aceita arquivo antes de o chamado existir — e porque um anexo destinado a uma nota
 * interna não pode passar por um estado em que o solicitante o enxergue.
 */
export async function uploadAttachment(file: File): Promise<Attachment> {
  const form = new FormData()
  form.append('file', file)

  const { data } = await api.post<Attachment>('/api/attachments', form, {
    // O cliente tem `application/json` como padrão, e aqui isso quebraria o envio: o
    // multipart precisa do `boundary`, que só o navegador sabe gerar. Remover o
    // cabeçalho faz o XHR montá-lo sozinho, com o boundary certo.
    headers: { 'Content-Type': undefined },
  })

  return data
}

export async function listAttachments(ticketId: string): Promise<Attachment[]> {
  const { data } = await api.get<Attachment[]>(`/api/tickets/${ticketId}/attachments`)

  return data
}

/**
 * Baixa o conteúdo do anexo como blob.
 *
 * Não dá para apontar `<img src>` ou `<a href>` direto para a URL da API: o token vai no
 * cabeçalho Authorization, que o navegador não manda sozinho ao carregar uma imagem. O
 * conteúdo vem por aqui e vira uma URL de objeto local.
 */
export async function fetchAttachmentBlob(id: string): Promise<Blob> {
  const { data } = await api.get<Blob>(`/api/attachments/${id}`, { responseType: 'blob' })

  return data
}
