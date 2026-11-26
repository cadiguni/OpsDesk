import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'

import * as jobTitlesApi from '@/features/job-titles/api'
import type { ManagedJobTitle } from '@/features/job-titles/api'
import { JobTitleAdmin } from '@/routes/job-title-admin'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/job-titles/api', async (original) => ({
  ...(await original<typeof jobTitlesApi>()),
  listManagedJobTitles: vi.fn(),
  createJobTitle: vi.fn(),
  changeJobTitleActivation: vi.fn(),
}))

const analyst: ManagedJobTitle = {
  id: 'analista',
  name: 'Analista',
  isActive: true,
  createdAt: '2026-11-26T00:00:00Z',
  users: 3,
}

beforeEach(() => {
  vi.mocked(jobTitlesApi.listManagedJobTitles).mockResolvedValue({
    items: [analyst],
    page: 1,
    pageSize: 25,
    totalCount: 1,
    totalPages: 1,
    hasNextPage: false,
  })
})

it('não carrega nada para quem não é administrador', () => {
  renderWithProviders(<JobTitleAdmin />, { role: 'Technician', route: '/cargos' })

  expect(screen.getByText(/apenas administradores/i)).toBeInTheDocument()
  expect(jobTitlesApi.listManagedJobTitles).not.toHaveBeenCalled()
})

it('lista os cargos com quantas pessoas têm cada um', async () => {
  renderWithProviders(<JobTitleAdmin />, { role: 'Manager', route: '/cargos' })

  expect(await screen.findByText('Analista')).toBeInTheDocument()
  expect(screen.getByText('3 pessoa(s)')).toBeInTheDocument()
})

it('cria um cargo', async () => {
  vi.mocked(jobTitlesApi.createJobTitle).mockResolvedValue({ ...analyst, id: 'novo', name: 'Aprendiz', users: 0 })

  const user = userEvent.setup()
  renderWithProviders(<JobTitleAdmin />, { role: 'Manager', route: '/cargos' })

  await user.click(await screen.findByRole('button', { name: /novo cargo/i }))
  await user.type(screen.getByLabelText('Nome do cargo'), '  Aprendiz ')
  await user.click(screen.getByRole('button', { name: 'Criar cargo' }))

  await waitFor(() => expect(vi.mocked(jobTitlesApi.createJobTitle).mock.calls[0]?.[0]).toBe('Aprendiz'))
})

it('desativa um cargo', async () => {
  vi.mocked(jobTitlesApi.changeJobTitleActivation).mockResolvedValue({ ...analyst, isActive: false })

  const user = userEvent.setup()
  renderWithProviders(<JobTitleAdmin />, { role: 'Manager', route: '/cargos' })

  await user.click(await screen.findByRole('button', { name: 'Desativar' }))

  await waitFor(() => expect(jobTitlesApi.changeJobTitleActivation).toHaveBeenCalledWith('analista', false))
})
