import type { PagedResult } from '@/features/tickets/types'
import { api } from '@/lib/api'

/** Categoria na tela de administração, ativa ou não. */
export type ManagedCategory = {
  id: string
  name: string
  description: string | null
  isActive: boolean
  createdAt: string
  /** Chamados não terminais na categoria. */
  openTickets: number
  totalTickets: number
}

export type ManagedCategoryQuery = {
  search?: string
  isActive?: boolean
  page?: number
}

export type SaveCategoryInput = {
  name: string
  description?: string
}

export async function listManagedCategories(
  query: ManagedCategoryQuery,
): Promise<PagedResult<ManagedCategory>> {
  const { data } = await api.get<PagedResult<ManagedCategory>>('/api/admin/categories', {
    params: query,
  })

  return data
}

export async function createCategory(input: SaveCategoryInput): Promise<ManagedCategory> {
  const { data } = await api.post<ManagedCategory>('/api/admin/categories', input)

  return data
}

export async function updateCategory(id: string, input: SaveCategoryInput): Promise<ManagedCategory> {
  const { data } = await api.put<ManagedCategory>(`/api/admin/categories/${id}`, input)

  return data
}

export async function changeCategoryActivation(
  id: string,
  isActive: boolean,
): Promise<ManagedCategory> {
  const { data } = await api.post<ManagedCategory>(`/api/admin/categories/${id}/activation`, {
    isActive,
  })

  return data
}
