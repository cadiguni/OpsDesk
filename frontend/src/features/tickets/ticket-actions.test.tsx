import { screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as ticketsApi from '@/features/tickets/api'
import { TicketActions } from '@/features/tickets/ticket-actions'
import { renderWithProviders, ticketDetail } from '@/test/render'

vi.mock('@/features/tickets/api')

beforeEach(() => {
  vi.mocked(ticketsApi.listStaff).mockResolvedValue([])
  vi.mocked(ticketsApi.listCategories).mockResolvedValue([])
})

/** Os botões de mudança de status, pelo grupo "Ações". */
function statusButtons(): string[] {
  const group = screen.getByText('Ações').parentElement as HTMLElement

  return within(group)
    .getAllByRole('button')
    .map((button) => button.textContent ?? '')
}

describe('ações de status', () => {
  it('oferece exatamente o que allowedNextStatuses traz', () => {
    renderWithProviders(
      <TicketActions
        ticket={ticketDetail({ status: 'InProgress', allowedNextStatuses: ['WaitingOnRequester', 'Resolved'] })}
      />,
    )

    expect(statusButtons()).toEqual(['Aguardar solicitante', 'Marcar como resolvido'])
  })

  it('não mostra ação nenhuma quando a API não permite nenhuma', () => {
    renderWithProviders(
      <TicketActions ticket={ticketDetail({ status: 'Cancelled', allowedNextStatuses: [] })} />,
    )

    expect(screen.queryByText('Ações')).not.toBeInTheDocument()
  })

  it('chama de reabrir a volta de fechado para atendimento', () => {
    renderWithProviders(
      <TicketActions ticket={ticketDetail({ status: 'Closed', allowedNextStatuses: ['InProgress'] })} />,
    )

    expect(statusButtons()).toEqual(['Reabrir chamado'])
  })

  it('chama de iniciar atendimento a mesma transição vinda de aberto', () => {
    renderWithProviders(
      <TicketActions ticket={ticketDetail({ status: 'Open', allowedNextStatuses: ['InProgress'] })} />,
    )

    expect(statusButtons()).toEqual(['Iniciar atendimento'])
  })
})
