import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import * as notificationsApi from '@/features/notifications/api'
import type { NotificationItem } from '@/features/notifications/api'
import { NotificationBell } from '@/features/notifications/notification-bell'
import { describeNotification } from '@/features/notifications/notification-label'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/notifications/api')

function item(overrides: Partial<NotificationItem>): NotificationItem {
  return {
    id: 'n1',
    kind: 'Assigned',
    ticketId: 't1',
    ticketCode: 'OPS-000001',
    ticketTitle: 'VPN não conecta',
    actorName: 'Gestora Carla',
    detail: null,
    createdAt: '2026-09-25T12:00:00Z',
    readAt: null,
    ...overrides,
  }
}

describe('describeNotification', () => {
  it.each([
    ['Assigned', 'Gestora Carla passou o chamado para você'],
    ['Unassigned', 'Gestora Carla tirou o chamado de você'],
    ['RequesterReplied', 'Gestora Carla respondeu'],
    ['CommentAdded', 'Gestora Carla comentou'],
    ['Reopened', 'Gestora Carla reabriu o chamado'],
  ] as const)('%s', (kind, text) => {
    expect(describeNotification(item({ kind }))).toBe(text)
  })

  it('traduz o status novo', () => {
    expect(describeNotification(item({ kind: 'StatusChanged', detail: 'WaitingOnRequester' }))).toBe(
      'Gestora Carla mudou o status para Aguardando usuário',
    )
  })

  it.each([
    ['SlaDueSoon', 'Response', 'O prazo de resposta está perto de vencer'],
    ['SlaDueSoon', 'Resolution', 'O prazo de resolução está perto de vencer'],
    ['SlaOverdue', 'Resolution', 'O prazo de resolução venceu'],
  ] as const)('alerta de SLA %s %s', (kind, detail, text) => {
    expect(describeNotification(item({ kind, detail, actorName: null }))).toBe(text)
  })

  it('atribui ao sistema quando ninguém agiu', () => {
    expect(describeNotification(item({ actorName: null }))).toBe('O sistema passou o chamado para você')
  })
})

describe('sino', () => {
  it('diz quantas não lidas no nome acessível', async () => {
    vi.mocked(notificationsApi.countUnread).mockResolvedValue(3)

    renderWithProviders(<NotificationBell />)

    expect(await screen.findByRole('link', { name: 'Notificações, 3 não lida(s)' })).toHaveAttribute(
      'href',
      '/notificacoes',
    )
  })

  it('sem não lidas, não mostra contador', async () => {
    vi.mocked(notificationsApi.countUnread).mockResolvedValue(0)

    renderWithProviders(<NotificationBell />)

    expect(await screen.findByRole('link', { name: 'Notificações' })).toBeInTheDocument()
  })
})
