import { screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as inboundApi from '@/features/inbound/api'
import type { InboundEmailItem } from '@/features/inbound/api'
import { InboundEmails } from '@/routes/inbound-emails'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/inbound/api')

function item(overrides: Partial<InboundEmailItem>): InboundEmailItem {
  return {
    id: 'm1',
    accountName: 'Suporte TI',
    fromName: 'Vendas',
    fromAddress: 'vendas@spam.example',
    subject: 'Promoção imperdível',
    receivedAt: '2026-10-01T12:00:00Z',
    status: 'Spam',
    spamScore: 6,
    spamReason: 'Conteúdo suspeito: 12 links',
    ticketId: null,
    ticketCode: null,
    processingError: null,
    attempts: 0,
    ...overrides,
  }
}

function withMessage(message: InboundEmailItem, bodyText: string) {
  vi.mocked(inboundApi.listInbound).mockResolvedValue({
    items: [message],
    page: 1,
    pageSize: 25,
    totalCount: 1,
    totalPages: 1,
    hasNextPage: false,
  })
  vi.mocked(inboundApi.getInbound).mockResolvedValue({ email: message, bodyText, reviewedByName: null, reviewedAt: null })
}

beforeEach(() => {
  vi.mocked(inboundApi.listInbound).mockReset()
  vi.mocked(inboundApi.getInbound).mockReset()
})

describe('e-mails recebidos', () => {
  it('mostra o corpo como texto, sem interpretar HTML', async () => {
    withMessage(item({}), '<img src=x onerror="alert(1)"><b>Compre já</b>')

    renderWithProviders(<InboundEmails />, { role: 'Technician', route: '/emails?id=m1' })

    expect(await screen.findByText('<img src=x onerror="alert(1)"><b>Compre já</b>')).toBeInTheDocument()
    expect(document.querySelector('article img')).toBeNull()
    expect(document.querySelector('article b')).toBeNull()
  })

  it('oferece as ações de revisão para mensagem retida', async () => {
    withMessage(item({}), 'Oferta')

    renderWithProviders(<InboundEmails />, { role: 'Technician', route: '/emails?id=m1' })

    expect(await screen.findByRole('button', { name: 'Não é spam' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Bloquear o domínio' })).toBeInTheDocument()
    // Excluir é só do gestor.
    expect(screen.queryByRole('button', { name: 'Excluir' })).not.toBeInTheDocument()
  })

  it('oferece processar de novo a mensagem que falhou', async () => {
    withMessage(item({ status: 'Failed', processingError: 'Sem política de SLA', attempts: 3 }), 'Texto')

    renderWithProviders(<InboundEmails />, { role: 'Manager', route: '/emails?aba=Failed&id=m1' })

    expect(await screen.findByRole('button', { name: 'Processar de novo' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Excluir' })).toBeInTheDocument()
  })

  it('solicitante não vê a tela', () => {
    renderWithProviders(<InboundEmails />, { role: 'Requester', route: '/emails' })

    expect(screen.getByText(/apenas a equipe/i)).toBeInTheDocument()
    expect(inboundApi.listInbound).not.toHaveBeenCalled()
  })
})
