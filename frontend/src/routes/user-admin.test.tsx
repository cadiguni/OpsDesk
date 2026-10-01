import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as usersApi from '@/features/users/api'
import type { ManagedUser } from '@/features/users/api'
import { UserAdmin } from '@/routes/user-admin'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/users/api', async (original) => ({
  ...(await original<typeof usersApi>()),
  listManagedUsers: vi.fn(),
  createUser: vi.fn(),
}))

const created: ManagedUser = {
  id: 'novo',
  name: 'Pessoa Nova',
  email: 'pessoa@empresa.com',
  role: 'Technician',
  isActive: true,
  mustChangePassword: true,
  createdAt: '2026-10-01T12:00:00Z',
  openAssignedTickets: 0,
}

beforeEach(() => {
  vi.mocked(usersApi.listManagedUsers).mockResolvedValue({
    items: [],
    page: 1,
    pageSize: 25,
    totalCount: 0,
    totalPages: 0,
    hasNextPage: false,
  })
})

async function createSomeone() {
  const user = userEvent.setup()

  renderWithProviders(<UserAdmin />, { role: 'Manager', route: '/usuarios' })

  await user.click(await screen.findByRole('button', { name: /novo usuário/i }))
  await user.type(screen.getByLabelText('Nome'), 'Pessoa Nova')
  await user.type(screen.getByLabelText('E-mail'), 'pessoa@empresa.com')
  await user.click(screen.getByRole('button', { name: 'Cadastrar' }))
}

describe('cadastro pelo gestor', () => {
  it('sem e-mail, mostra a senha provisória uma vez', async () => {
    vi.mocked(usersApi.createUser).mockResolvedValue({ user: created, invitationSent: false, temporaryPassword: 'Abc23xyz9KmN' })

    await createSomeone()

    expect(await screen.findByText('Abc23xyz9KmN')).toBeInTheDocument()
    expect(screen.getByText(/aparece só agora/i)).toBeInTheDocument()
  })

  it('com e-mail, avisa do convite e não mostra senha nenhuma', async () => {
    vi.mocked(usersApi.createUser).mockResolvedValue({ user: created, invitationSent: true, temporaryPassword: null })

    await createSomeone()

    await waitFor(() => expect(screen.getByText(/convite enviado/i)).toBeInTheDocument())
    expect(screen.queryByText(/senha provisória/i)).not.toBeInTheDocument()
  })
})
