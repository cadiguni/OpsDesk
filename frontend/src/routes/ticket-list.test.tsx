import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as ticketsApi from '@/features/tickets/api'
import type { TicketListItem } from '@/features/tickets/types'
import { TicketList } from '@/routes/ticket-list'
import { locationSearch, renderWithProviders, userOf } from '@/test/render'

vi.mock('@/features/tickets/api')

beforeEach(() => {
  vi.mocked(ticketsApi.listTickets).mockReset()
  vi.mocked(ticketsApi.listTickets).mockResolvedValue({
    items: [],
    page: 1,
    pageSize: 25,
    totalCount: 0,
    totalPages: 0,
    hasNextPage: false,
  })
  vi.mocked(ticketsApi.listCategories).mockResolvedValue([])
  vi.mocked(ticketsApi.listStaff).mockResolvedValue([
    { id: 'tech-2', name: 'Bruno Técnico', role: 'Technician' },
  ])
})

/** O filtro da última chamada à API. */
function lastQuery() {
  return vi.mocked(ticketsApi.listTickets).mock.lastCall?.[0]
}

describe('filtros da lista na URL', () => {
  it('reconstrói a consulta a partir da URL, inclusive depois de recarregar', async () => {
    renderWithProviders(<TicketList />, {
      route: '/chamados?status=Open&status=Triage&priority=High&search=vpn&sort=UpdatedAtDescending&page=2',
    })

    await waitFor(() => expect(ticketsApi.listTickets).toHaveBeenCalled())

    expect(lastQuery()).toMatchObject({
      status: ['Open', 'Triage'],
      priority: ['High'],
      search: 'vpn',
      sort: 'UpdatedAtDescending',
      page: 2,
    })
  })

  it('manda o período como instantes de São Paulo, com o fim exclusivo no dia seguinte', async () => {
    renderWithProviders(<TicketList />, { route: '/chamados?from=2026-09-01&to=2026-09-10' })

    await waitFor(() => expect(ticketsApi.listTickets).toHaveBeenCalled())

    expect(lastQuery()).toMatchObject({
      createdFrom: '2026-09-01T00:00:00-03:00',
      createdBefore: '2026-09-11T00:00:00-03:00',
    })
  })

  it('"Meus chamados" filtra pelo responsável que está autenticado', async () => {
    renderWithProviders(<TicketList />, { route: '/chamados?mine=true' })

    await waitFor(() => expect(ticketsApi.listTickets).toHaveBeenCalled())

    expect(lastQuery()?.assignedTechnicianId).toBe(userOf('Technician').id)
  })

  it('mudar um filtro preserva os outros e volta para a primeira página', async () => {
    const user = userEvent.setup()

    renderWithProviders(<TicketList />, { route: '/chamados?priority=High&search=vpn&page=3' })

    await user.click(await screen.findByRole('button', { name: 'Em triagem' }))

    const params = locationSearch()
    expect(params.getAll('status')).toEqual(['Triage'])
    expect(params.get('priority')).toBe('High')
    expect(params.get('search')).toBe('vpn')
    expect(params.get('page')).toBeNull()

    await waitFor(() => expect(lastQuery()).toMatchObject({ status: ['Triage'], priority: ['High'], page: 1 }))
  })

  it('responsável e "Meus chamados" se excluem', async () => {
    const user = userEvent.setup()

    renderWithProviders(<TicketList />, { route: '/chamados?mine=true' })

    const select = await screen.findByRole('combobox', { name: 'Responsável' })
    await screen.findByRole('option', { name: 'Bruno Técnico' })
    await user.selectOptions(select, 'tech-2')

    const params = locationSearch()
    expect(params.get('assignedTechnicianId')).toBe('tech-2')
    expect(params.get('mine')).toBeNull()
  })

  it('solicitante não recebe os filtros da equipe', async () => {
    renderWithProviders(<TicketList />, { role: 'Requester', route: '/chamados' })

    await screen.findByRole('button', { name: 'Em triagem' })

    expect(screen.queryByRole('combobox', { name: 'Responsável' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /sem responsável/i })).not.toBeInTheDocument()
    expect(ticketsApi.listStaff).not.toHaveBeenCalled()
  })
})

describe('paginação', () => {
  beforeEach(() => {
    vi.mocked(ticketsApi.listTickets).mockResolvedValue({
      items: [],
      page: 3,
      pageSize: 25,
      totalCount: 2437,
      totalPages: 98,
      hasNextPage: true,
    })
  })

  it('mostra o total e vai direto para a página digitada, sem passar do limite', async () => {
    const user = userEvent.setup()

    renderWithProviders(<TicketList />, { route: '/chamados?search=vpn&page=3' })

    expect(await screen.findByText(/2\.437 chamados · página 3 de 98/)).toBeInTheDocument()

    await user.type(screen.getByRole('spinbutton', { name: 'Ir para a página' }), '500{Enter}')

    expect(locationSearch().get('page')).toBe('98')
    expect(locationSearch().get('search')).toBe('vpn')
  })

  it('a última página fica a um clique', async () => {
    const user = userEvent.setup()

    renderWithProviders(<TicketList />, { route: '/chamados?page=3' })

    await user.click(await screen.findByRole('button', { name: 'Última página' }))

    expect(locationSearch().get('page')).toBe('98')
  })

  it('trocar o tamanho da página volta para a primeira e pede o novo tamanho', async () => {
    const user = userEvent.setup()

    renderWithProviders(<TicketList />, { route: '/chamados?page=3' })

    await user.selectOptions(await screen.findByRole('combobox', { name: 'Itens por página' }), '100')

    expect(locationSearch().get('pageSize')).toBe('100')
    expect(locationSearch().get('page')).toBeNull()
    await waitFor(() => expect(lastQuery()).toMatchObject({ page: 1, pageSize: 100 }))
  })
})

function listItem(id: string, code: string, overrides: Partial<TicketListItem> = {}): TicketListItem {
  return {
    id,
    code,
    title: `Chamado ${code}`,
    status: 'InProgress',
    priority: 'Medium',
    categoryName: 'VPN',
    requesterName: 'Ana Usuária',
    assignedTechnicianName: null,
    createdAt: '2026-09-10T12:00:00Z',
    slaResponseDueAt: '2026-09-10T20:00:00Z',
    slaResolutionDueAt: '2026-09-15T20:00:00Z',
    firstRespondedAt: null,
    resolvedAt: null,
    updatedAt: '2026-09-10T12:00:00Z',
    source: 'Portal',
    ...overrides,
  }
}

describe('mudança de status em lote', () => {
  beforeEach(() => {
    vi.mocked(ticketsApi.listTickets).mockResolvedValue({
      items: [
        listItem('t1', 'OPS-000001'),
        listItem('t2', 'OPS-000002'),
        listItem('t3', 'OPS-000003', { status: 'Cancelled' }),
      ],
      page: 1,
      pageSize: 25,
      totalCount: 3,
      totalPages: 1,
      hasNextPage: false,
    })
    vi.mocked(ticketsApi.bulkChangeStatus).mockReset()
  })

  it('o solicitante não vê seleção nenhuma', async () => {
    renderWithProviders(<TicketList />, { role: 'Requester', route: '/chamados' })

    expect(await screen.findByText('Chamado OPS-000001')).toBeInTheDocument()
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('fecha os selecionados depois da confirmação e mantém selecionado o que não mudou', async () => {
    const user = userEvent.setup()
    vi.mocked(ticketsApi.bulkChangeStatus).mockResolvedValue([
      { ticketId: 't1', code: 'OPS-000001', outcome: 'Changed', status: 'Closed' },
      { ticketId: 't2', code: 'OPS-000002', outcome: 'Changed', status: 'Closed' },
      { ticketId: 't3', code: 'OPS-000003', outcome: 'TransitionRejected', status: 'Cancelled' },
    ])

    renderWithProviders(<TicketList />, { route: '/chamados' })

    await user.click(await screen.findByRole('checkbox', { name: 'Selecionar todos desta página' }))
    expect(screen.getByText('3 chamados selecionados')).toBeInTheDocument()

    // O primeiro clique só arma: nada vai para o servidor sem a confirmação.
    await user.click(screen.getByRole('button', { name: 'Aplicar' }))
    expect(ticketsApi.bulkChangeStatus).not.toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: 'Confirmar: fechar 3 chamados' }))

    expect(ticketsApi.bulkChangeStatus).toHaveBeenCalledWith(['t1', 't2', 't3'], 'Closed')
    expect(await screen.findByText('2 chamados passaram para Fechado.')).toBeInTheDocument()
    expect(screen.getByText('OPS-000003: está Cancelado e não pode ir para Fechado.')).toBeInTheDocument()
    expect(screen.getByText('1 chamado selecionado')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Selecionar OPS-000003' })).toBeChecked()
  })

  it('mudar de filtro descarta a seleção', async () => {
    const user = userEvent.setup()

    renderWithProviders(<TicketList />, { route: '/chamados' })

    await user.click(await screen.findByRole('checkbox', { name: 'Selecionar OPS-000002' }))
    expect(screen.getByText('1 chamado selecionado')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Em triagem' }))

    expect(screen.queryByText(/selecionado/)).not.toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Selecionar OPS-000002' })).not.toBeChecked()
  })
})
