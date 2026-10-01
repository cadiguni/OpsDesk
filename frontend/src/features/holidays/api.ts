import { api } from '@/lib/api'

export type Holiday = {
  id: string
  /** `yyyy-mm-dd`, data local do expediente. */
  date: string
  name: string
  isActive: boolean
  /** Cadastrado pelo gestor. Só esses podem ser apagados. */
  isCustom: boolean
}

export async function listHolidays(year: number): Promise<Holiday[]> {
  const { data } = await api.get<Holiday[]>('/api/admin/holidays', { params: { year } })

  return data
}

export async function addHoliday(input: { date: string; name: string }): Promise<Holiday> {
  const { data } = await api.post<Holiday>('/api/admin/holidays', input)

  return data
}

export async function changeHolidayActivation(id: string, isActive: boolean): Promise<Holiday> {
  const { data } = await api.post<Holiday>(`/api/admin/holidays/${id}/activation`, { isActive })

  return data
}

export async function deleteHoliday(id: string): Promise<void> {
  await api.delete(`/api/admin/holidays/${id}`)
}
