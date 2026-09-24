import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as ticketsApi from '@/features/tickets/api'
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
