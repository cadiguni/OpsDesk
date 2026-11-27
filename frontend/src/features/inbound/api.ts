import type { TicketPriority } from '@/domain/enums'
import type { PagedResult } from '@/features/tickets/types'
import { api } from '@/lib/api'

export type InboundEmailStatus = 'Pending' | 'Processed' | 'Spam' | 'Duplicate' | 'Failed'

export type InboundEmailItem = {
  id: string
  accountName: string
  fromName: string
  fromAddress: string
  subject: string
  receivedAt: string
  status: InboundEmailStatus
  spamScore: number
  spamReason: string | null
  ticketId: string | null
  ticketCode: string | null
  processingError: string | null
  attempts: number
}

export type InboundEmailDetail = {
  email: InboundEmailItem
  /** Sempre texto puro: o sistema não guarda nem mostra HTML de e-mail recebido. */
  bodyText: string
  reviewedByName: string | null
  reviewedAt: string | null
}

export type SpamReviewAction = 'NotSpam' | 'NotSpamAndAllowSender' | 'ConfirmAndBlockSender' | 'BlockDomain'

export type SpamRule = {
  id: string
  type: 'SenderAddress' | 'SenderDomain' | 'SubjectContains' | 'BodyContains'
  pattern: string
  action: 'Allow' | 'Block'
  isActive: boolean
  createdByName: string | null
  createdAt: string
}

export type EmailAccount = {
  id: string
  name: string
  address: string
  defaultCategoryId: string | null
  defaultPriority: TicketPriority
  internalDomainsOnly: boolean
  isActive: boolean
  lastSyncAt: string | null
  lastError: string | null
}

export type SaveEmailAccountInput = Omit<EmailAccount, 'id' | 'lastSyncAt' | 'lastError'>

export async function listInbound(query: {
  status?: InboundEmailStatus
  search?: string
  page?: number
}): Promise<PagedResult<InboundEmailItem>> {
  const { data } = await api.get<PagedResult<InboundEmailItem>>('/api/inbound', { params: query })

  return data
}

export async function getInbound(id: string): Promise<InboundEmailDetail> {
  const { data } = await api.get<InboundEmailDetail>(`/api/inbound/${id}`)

  return data
}

export async function reviewInbound(id: string, action: SpamReviewAction): Promise<InboundEmailItem> {
  const { data } = await api.post<InboundEmailItem>(`/api/inbound/${id}/review`, { action })

  return data
}

export async function reprocessInbound(id: string): Promise<InboundEmailItem> {
  const { data } = await api.post<InboundEmailItem>(`/api/inbound/${id}/reprocess`)

  return data
}

export async function deleteInbound(id: string): Promise<void> {
  await api.delete(`/api/inbound/${id}`)
}

export async function listSpamRules(): Promise<SpamRule[]> {
  const { data } = await api.get<SpamRule[]>('/api/inbound/rules')

  return data
}

export async function setSpamRuleActive(id: string, isActive: boolean): Promise<void> {
  await api.post(`/api/inbound/rules/${id}/activation`, { isActive })
}

export async function listEmailAccounts(): Promise<EmailAccount[]> {
  const { data } = await api.get<EmailAccount[]>('/api/admin/settings/email/accounts')

  return data
}

export async function saveEmailAccount(id: string | null, input: SaveEmailAccountInput): Promise<EmailAccount> {
  const { data } = id
    ? await api.put<EmailAccount>(`/api/admin/settings/email/accounts/${id}`, input)
    : await api.post<EmailAccount>('/api/admin/settings/email/accounts', input)

  return data
}

/** Resultado de um lote da recuperação dos anexos de mensagens antigas. */
export type AttachmentRecoveryBatch = {
  examined: number
  recovered: number
  attachments: number
  notFound: number
  remaining: number
  /** De onde o próximo lote continua; nulo quando acabou. */
  cursor: string | null
  error: string | null
}

export async function getPendingAttachmentRecovery(): Promise<number> {
  const { data } = await api.get<{ pending: number }>('/api/inbound/attachment-recovery')

  return data.pending
}

export async function runAttachmentRecovery(after: string | null): Promise<AttachmentRecoveryBatch> {
  const { data } = await api.post<AttachmentRecoveryBatch>('/api/inbound/attachment-recovery', null, {
    params: after ? { after } : undefined,
  })

  return data
}
