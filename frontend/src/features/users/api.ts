import axios from 'axios'

import type { TicketStatus, UserRole } from '@/domain/enums'
import type { PagedResult } from '@/features/tickets/types'
import { api } from '@/lib/api'

/** Usuário na tela de administração, ativo ou não. */
export type ManagedUser = {
  id: string
  name: string
  email: string
  role: UserRole
  /** Cargo, só informativo. Não muda o que a pessoa pode fazer: isso é o `role`. */
  jobTitleId: string | null
  jobTitleName: string | null
  isActive: boolean
  mustChangePassword: boolean
  createdAt: string
  /** Chamados não terminais sob responsabilidade da pessoa. Acima de zero, bloqueia desativar. */
  openAssignedTickets: number
}

export type ManagedUserQuery = {
  search?: string
  role?: UserRole
  isActive?: boolean
  page?: number
}

/** Chamado em aberto que a API devolve ao recusar desativação ou rebaixamento. */
export type BlockingTicket = {
  id: string
  code: string
  title: string
  status: TicketStatus
}

export async function listManagedUsers(
  query: ManagedUserQuery,
): Promise<PagedResult<ManagedUser>> {
  const { data } = await api.get<PagedResult<ManagedUser>>('/api/admin/users', { params: query })

  return data
}

export async function changeRole(userId: string, role: UserRole): Promise<ManagedUser> {
  const { data } = await api.post<ManagedUser>(`/api/admin/users/${userId}/role`, { role })

  return data
}

/** Troca o nome exibido. Vale também para a própria conta. */
export async function changeName(userId: string, name: string): Promise<ManagedUser> {
  const { data } = await api.post<ManagedUser>(`/api/admin/users/${userId}/name`, { name })

  return data
}

/** Troca o cargo; `null` tira. Vale também para a própria conta. */
export async function changeJobTitle(userId: string, jobTitleId: string | null): Promise<ManagedUser> {
  const { data } = await api.post<ManagedUser>(`/api/admin/users/${userId}/job-title`, { jobTitleId })

  return data
}

export async function changeActivation(userId: string, isActive: boolean): Promise<ManagedUser> {
  const { data } = await api.post<ManagedUser>(`/api/admin/users/${userId}/activation`, {
    isActive,
  })

  return data
}

/**
 * Como a pessoa recebeu o acesso: convite por e-mail, ou senha provisória — que o servidor
 * devolve uma única vez, e que a tela mostra uma única vez.
 */
export type AccessGranted = {
  user: ManagedUser
  invitationSent: boolean
  temporaryPassword: string | null
}

export type UserAuditAction =
  | 'Created'
  | 'RoleChanged'
  | 'Deactivated'
  | 'Reactivated'
  | 'PasswordChanged'
  | 'PasswordReset'
  | 'PasswordSetByLink'
  | 'MicrosoftAccountLinked'
  | 'JobTitleChanged'
  | 'NameChanged'

export type UserAuditItem = {
  action: UserAuditAction
  previousValue: string | null
  newValue: string | null
  changedByName: string | null
  createdAt: string
}

export async function createUser(input: {
  name: string
  email: string
  role: UserRole
  jobTitleId?: string | null
}): Promise<AccessGranted> {
  const { data } = await api.post<AccessGranted>('/api/admin/users', input)

  return data
}

export async function resetUserPassword(userId: string): Promise<AccessGranted> {
  const { data } = await api.post<AccessGranted>(`/api/admin/users/${userId}/password-reset`)

  return data
}

export async function getUserHistory(userId: string): Promise<UserAuditItem[]> {
  const { data } = await api.get<UserAuditItem[]>(`/api/admin/users/${userId}/history`)

  return data
}

/**
 * Chamados que impediram a operação, quando a recusa foi por isso.
 *
 * Vêm no corpo do 409 para a tela listar o que reatribuir, em vez de mandar o administrador
 * procurar na fila.
 */
export function blockingTickets(error: unknown): BlockingTicket[] {
  if (!axios.isAxiosError(error) || error.response?.status !== 409) {
    return []
  }

  const tickets = (error.response.data as { tickets?: BlockingTicket[] } | undefined)?.tickets

  return Array.isArray(tickets) ? tickets : []
}
