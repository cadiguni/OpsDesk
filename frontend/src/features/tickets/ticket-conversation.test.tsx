import { screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as ticketsApi from '@/features/tickets/api'
import { TicketConversation } from '@/features/tickets/ticket-conversation'
import type { Attachment, TicketComment } from '@/features/tickets/types'
import { renderWithProviders, ticketDetail, userOf } from '@/test/render'

vi.mock('@/features/tickets/api')

const publicReply: TicketComment = {
  id: 'comment-public',
  authorId: userOf('Technician').id,
  authorName: 'Técnica Ana',
  authorRole: 'Technician',
  content: 'Reiniciei o concentrador, pode testar?',
  isInternal: false,
  createdAt: '2026-09-10T13:00:00Z',
}

const internalNote: TicketComment = {
  id: 'comment-internal',
  authorId: userOf('Technician').id,
  authorName: 'Técnica Ana',
  authorRole: 'Technician',
  content: 'Suspeito do certificado do usuário.',
  isInternal: true,
  createdAt: '2026-09-10T13:05:00Z',
}

function attachment(overrides: Partial<Attachment>): Attachment {
  return {
    id: 'attachment',
    commentId: null,
    fileName: 'arquivo.txt',
    contentType: 'text/plain',
    sizeInBytes: 1024,
    isInternal: false,
    isImage: false,
    uploadedById: userOf('Technician').id,
    uploadedByName: 'Técnica Ana',
    createdAt: '2026-09-10T13:05:00Z',
    ...overrides,
  }
}

beforeEach(() => {
  vi.mocked(ticketsApi.listComments).mockResolvedValue([publicReply, internalNote])
  vi.mocked(ticketsApi.listAttachments).mockResolvedValue([
    attachment({ id: 'a-public', commentId: publicReply.id, fileName: 'print-publico.txt' }),
    attachment({ id: 'a-internal', commentId: internalNote.id, fileName: 'log-interno.txt', isInternal: true }),
  ])
})

/** O `<article>` da mensagem que contém o texto. */
async function messageWith(text: string) {
  return (await screen.findByText(text)).closest('article') as HTMLElement
}

describe('marcação de comentário e anexo internos', () => {
  it('marca a nota interna, e só ela', async () => {
    renderWithProviders(<TicketConversation ticket={ticketDetail()} />)

    const internal = await messageWith(internalNote.content)
    const reply = await messageWith(publicReply.content)

    expect(within(internal).getByText(/não visível ao solicitante/i)).toBeInTheDocument()
    expect(within(reply).queryByText(/não visível ao solicitante/i)).not.toBeInTheDocument()
  })

  it('anuncia o anexo interno, e só ele', async () => {
    renderWithProviders(<TicketConversation ticket={ticketDetail()} />)

    const internal = await messageWith(internalNote.content)
    const reply = await messageWith(publicReply.content)

    expect(await within(internal).findByRole('button', { name: /anexo interno:\s*log-interno\.txt/i })).toBeInTheDocument()
    expect(within(reply).getByRole('button', { name: /print-publico\.txt/i })).not.toHaveAccessibleName(/interno/i)
  })

  it('não oferece nota interna ao solicitante', async () => {
    vi.mocked(ticketsApi.listComments).mockResolvedValue([publicReply])

    renderWithProviders(<TicketConversation ticket={ticketDetail()} />, { role: 'Requester' })

    await screen.findByText(publicReply.content)
    expect(screen.queryByRole('button', { name: /nota interna/i })).not.toBeInTheDocument()
  })
})

describe('reabertura pela resposta', () => {
  it('avisa o solicitante que responder a um chamado resolvido o reabre', async () => {
    renderWithProviders(
      <TicketConversation ticket={ticketDetail({ status: 'Resolved', allowedNextStatuses: ['Closed'] })} />,
      { role: 'Requester' },
    )

    expect(await screen.findByText(/enviar uma resposta vai reabrir o chamado/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /enviar e fechar/i })).toBeInTheDocument()
  })

  it('deixa o solicitante responder a chamado fechado dentro da janela', async () => {
    const tomorrow = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()

    renderWithProviders(
      <TicketConversation ticket={ticketDetail({ status: 'Closed', reopenableUntil: tomorrow })} />,
      { role: 'Requester' },
    )

    expect(await screen.findByText(/enviar uma resposta vai reabri-lo/i)).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: /novo comentário/i })).toBeInTheDocument()
  })

  it('manda abrir chamado novo quando a janela passou', async () => {
    const yesterday = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString()

    renderWithProviders(
      <TicketConversation ticket={ticketDetail({ status: 'Closed', reopenableUntil: yesterday })} />,
      { role: 'Requester' },
    )

    expect(await screen.findByRole('link', { name: /abra um novo chamado/i })).toHaveAttribute(
      'href',
      '/chamados/novo',
    )
    expect(screen.queryByRole('textbox', { name: /novo comentário/i })).not.toBeInTheDocument()
  })

  it('não deixa a equipe comentar em chamado fechado sem reabrir', async () => {
    const tomorrow = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()

    renderWithProviders(
      <TicketConversation ticket={ticketDetail({ status: 'Closed', reopenableUntil: tomorrow })} />,
      { role: 'Technician' },
    )

    expect(await screen.findByText(/reabra-o pelas ações do chamado/i)).toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: /novo comentário/i })).not.toBeInTheDocument()
  })
})
