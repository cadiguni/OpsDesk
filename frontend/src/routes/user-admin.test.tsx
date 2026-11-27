import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as jobTitlesApi from '@/features/job-titles/api'
import * as usersApi from '@/features/users/api'
import type { ManagedUser } from '@/features/users/api'
import { UserAdmin } from '@/routes/user-admin'
import { renderWithProviders, userOf } from '@/test/render'

vi.mock('@/features/users/api', async (original) => ({
  ...(await original<typeof usersApi>()),
  listManagedUsers: vi.fn(),
  createUser: vi.fn(),
  changeJobTitle: vi.fn(),
  changeName: vi.fn(),
}))

vi.mock('@/features/job-titles/api', async (original) => ({
  ...(await original<typeof jobTitlesApi>()),
  listJobTitleOptions: vi.fn(),
}))

const created: ManagedUser = {
  id: 'novo',
  name: 'Pessoa Nova',
  email: 'pessoa@empresa.com',
  role: 'Technician',
  jobTitleId: null,
  jobTitleName: null,
  isActive: true,
  mustChangePassword: true,
  createdAt: '2026-10-01T12:00:00Z',
  openAssignedTickets: 0,
}

function page(items: ManagedUser[]) {
  return { items, page: 1, pageSize: 25, totalCount: items.length, totalPages: 1, hasNextPage: false }
}

beforeEach(() => {
  vi.mocked(usersApi.listManagedUsers).mockResolvedValue(page([]))
  vi.mocked(jobTitlesApi.listJobTitleOptions).mockResolvedValue([
    { id: 'analista', name: 'Analista' },
    { id: 'tech-lead', name: 'Tech Lead' },
  ])
})

async function createSomeone(jobTitle?: string) {
  const user = userEvent.setup()

  renderWithProviders(<UserAdmin />, { role: 'Manager', route: '/usuarios' })

  await user.click(await screen.findByRole('button', { name: /novo usuário/i }))
  await user.type(screen.getByLabelText('Nome'), 'Pessoa Nova')
  await user.type(screen.getByLabelText('E-mail'), 'pessoa@empresa.com')

  if (jobTitle) {
    await screen.findByRole('option', { name: jobTitle })
    await user.selectOptions(screen.getByLabelText('Cargo'), jobTitle)
  }

  await user.click(screen.getByRole('button', { name: 'Cadastrar' }))
}

describe('cadastro pelo administrador', () => {
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

  it('envia o cargo escolhido, e nenhum quando não se escolhe', async () => {
    vi.mocked(usersApi.createUser).mockResolvedValue({ user: created, invitationSent: true, temporaryPassword: null })

    await createSomeone('Tech Lead')

    await waitFor(() =>
      expect(vi.mocked(usersApi.createUser).mock.calls.at(-1)?.[0]).toMatchObject({ jobTitleId: 'tech-lead' }),
    )
  })
})

describe('nome na lista', () => {
  /** O administrador do bootstrap nasce com nome genérico; a própria conta precisa poder corrigi-lo. */
  it('a própria conta troca o nome, já sem espaços nas pontas', async () => {
    const me = userOf('Manager')
    vi.mocked(usersApi.listManagedUsers).mockResolvedValue(
      page([{ ...created, id: me.id, name: 'Gestor', role: 'Manager', mustChangePassword: false }]),
    )
    vi.mocked(usersApi.changeName).mockResolvedValue({ ...created, id: me.id, name: 'Maria Souza' })

    const user = userEvent.setup()
    renderWithProviders(<UserAdmin />, { role: 'Manager', route: '/usuarios' })

    await user.click(await screen.findByRole('button', { name: 'Editar o nome de Gestor' }))
    const input = screen.getByLabelText('Novo nome de Gestor')
    await user.clear(input)
    await user.type(input, '  Maria Souza  ')
    await user.click(screen.getByRole('button', { name: 'Salvar' }))

    await waitFor(() => expect(usersApi.changeName).toHaveBeenCalledWith(me.id, 'Maria Souza'))
    await waitFor(() => expect(screen.queryByLabelText('Novo nome de Gestor')).not.toBeInTheDocument())
  })

  it('cancelar não chama a API', async () => {
    vi.mocked(usersApi.listManagedUsers).mockResolvedValue(page([created]))

    const user = userEvent.setup()
    renderWithProviders(<UserAdmin />, { role: 'Manager', route: '/usuarios' })

    await user.click(await screen.findByRole('button', { name: 'Editar o nome de Pessoa Nova' }))
    await user.type(screen.getByLabelText('Novo nome de Pessoa Nova'), ' Silva')
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(usersApi.changeName).not.toHaveBeenCalled()
    expect(screen.getByText('Pessoa Nova')).toBeInTheDocument()
  })
})

describe('cargo na lista', () => {
  /** Perfil e ativação da própria conta ficam travados; cargo não, porque não dá permissão. */
  it('a própria conta troca o cargo, mas não o perfil', async () => {
    const me = userOf('Manager')
    vi.mocked(usersApi.listManagedUsers).mockResolvedValue(
      page([{ ...created, id: me.id, name: 'Eu mesmo', role: 'Manager', mustChangePassword: false }]),
    )
    vi.mocked(usersApi.changeJobTitle).mockResolvedValue({ ...created, jobTitleId: 'analista', jobTitleName: 'Analista' })

    const user = userEvent.setup()
    renderWithProviders(<UserAdmin />, { role: 'Manager', route: '/usuarios' })

    const jobTitle = await screen.findByLabelText('Cargo de Eu mesmo')
    expect(screen.getByLabelText('Perfil de Eu mesmo')).toBeDisabled()
    expect(jobTitle).toBeEnabled()

    await screen.findByRole('option', { name: 'Analista' })
    await user.selectOptions(jobTitle, 'Analista')

    await waitFor(() => expect(usersApi.changeJobTitle).toHaveBeenCalledWith(me.id, 'analista'))
  })

  it('mostra o cargo desativado que a pessoa já tem', async () => {
    vi.mocked(usersApi.listManagedUsers).mockResolvedValue(
      page([{ ...created, jobTitleId: 'aprendiz', jobTitleName: 'Aprendiz' }]),
    )

    renderWithProviders(<UserAdmin />, { role: 'Manager', route: '/usuarios' })

    const select = await screen.findByLabelText<HTMLSelectElement>('Cargo de Pessoa Nova')
    expect(select.value).toBe('aprendiz')
    expect(screen.getByRole('option', { name: 'Aprendiz' })).toBeInTheDocument()
  })
})
