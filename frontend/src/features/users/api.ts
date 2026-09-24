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

export async function changeActivation(userId: string, isActive: boolean): Promise<ManagedUser> {
  const { data } = await api.post<ManagedUser>(`/api/admin/users/${userId}/activation`, {
    isActive,
  })

  return data
}

/**
 * Chamados que impediram a operação, quando a recusa foi por isso.
 *
 * Vêm no corpo do 409 para a tela listar o que reatribuir, em vez de mandar o gestor
 * procurar na fila.
 */
export function blockingTickets(error: unknown): BlockingTicket[] {
  if (!axios.isAxiosError(error) || error.response?.status !== 409) {
    return []
  }

  const tickets = (error.response.data as { tickets?: BlockingTicket[] } | undefined)?.tickets

  return Array.isArray(tickets) ? tickets : []
}
