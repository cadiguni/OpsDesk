import type { PagedResult } from '@/features/tickets/types'
import { api } from '@/lib/api'

/** Cargo na tela de administração, ativo ou não. Só informativo: não dá permissão. */
export type ManagedJobTitle = {
  id: string
  name: string
  isActive: boolean
  createdAt: string
  /** Quantas pessoas têm o cargo. */
  users: number
}

/** Cargo ativo, para o seletor do cadastro de usuário. */
export type JobTitleOption = {
  id: string
  name: string
}

export type ManagedJobTitleQuery = {
  search?: string
  isActive?: boolean
  page?: number
}

export async function listManagedJobTitles(query: ManagedJobTitleQuery): Promise<PagedResult<ManagedJobTitle>> {
  const { data } = await api.get<PagedResult<ManagedJobTitle>>('/api/admin/job-titles', { params: query })

  return data
}

export async function listJobTitleOptions(): Promise<JobTitleOption[]> {
  const { data } = await api.get<JobTitleOption[]>('/api/admin/job-titles/options')

  return data
}

export async function createJobTitle(name: string): Promise<ManagedJobTitle> {
  const { data } = await api.post<ManagedJobTitle>('/api/admin/job-titles', { name })

  return data
}

export async function renameJobTitle(id: string, name: string): Promise<ManagedJobTitle> {
  const { data } = await api.put<ManagedJobTitle>(`/api/admin/job-titles/${id}`, { name })

  return data
}

export async function changeJobTitleActivation(id: string, isActive: boolean): Promise<ManagedJobTitle> {
  const { data } = await api.post<ManagedJobTitle>(`/api/admin/job-titles/${id}/activation`, { isActive })

  return data
}
