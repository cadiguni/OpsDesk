import type { ReactElement } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'

import { isStaff, type UserRole } from '@/domain/enums'
import type { SessionUser } from '@/features/auth/api'
import { SessionContext, type SessionState } from '@/features/auth/session-context'
import type { TicketDetail } from '@/features/tickets/types'

/**
 * Montagem compartilhada dos testes de componente.
 *
 * A sessão entra pelo contexto, e não pelo `SessionProvider` de verdade: o provider
 * restaura a sessão pelo cookie de refresh, e o que estes testes querem saber é como a
 * tela se comporta *para* um perfil, não como a sessão é obtida.
 */
export function userOf(role: UserRole): SessionUser {
  return {
    id: `${role.toLowerCase()}-id`,
    name: `Pessoa ${role}`,
    email: `${role.toLowerCase()}@opsdesk.local`,
    role,
    mustChangePassword: false,
  }
}

function sessionOf(role: UserRole): SessionState {
  const noop = async () => {}

  return {
    user: userOf(role),
    isRestoring: false,
    signIn: noop,
    signUp: noop,
    signOut: noop,
    changePassword: noop,
    mustChangePassword: false,
    role,
    isStaff: isStaff(role),
  }
}

/** O roteador do último render, para os testes que conferem o que foi parar na URL. */
let currentRouter: ReturnType<typeof createMemoryRouter> | null = null

export function locationSearch(): URLSearchParams {
  return new URLSearchParams(currentRouter?.state.location.search ?? '')
}

export function renderWithProviders(
  ui: ReactElement,
  { role = 'Technician', route = '/' }: { role?: UserRole; route?: string } = {},
) {
  // Sem nova tentativa: erro de requisição simulada deve aparecer na hora, e não depois
  // de três tentativas com espera.
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  currentRouter = createMemoryRouter([{ path: '*', element: ui }], { initialEntries: [route] })

  return render(
    <QueryClientProvider client={queryClient}>
      <SessionContext.Provider value={sessionOf(role)}>
        <RouterProvider router={currentRouter} />
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

export function ticketDetail(overrides: Partial<TicketDetail> = {}): TicketDetail {
  return {
    id: 'ticket-1',
    code: 'OPS-000001',
    title: 'VPN não conecta',
    description: 'Desde hoje cedo a VPN recusa a conexão.',
    status: 'InProgress',
    priority: 'Medium',
    source: 'Portal',
    categoryId: 'category-vpn',
    categoryName: 'VPN',
    requesterId: userOf('Requester').id,
    requesterName: userOf('Requester').name,
    assignedTechnicianId: null,
    assignedTechnicianName: null,
    createdAt: '2026-09-10T12:00:00Z',
    updatedAt: '2026-09-10T12:00:00Z',
    slaResponseDueAt: '2026-09-10T20:00:00Z',
    slaResolutionDueAt: '2026-09-15T20:00:00Z',
    firstRespondedAt: null,
    resolvedAt: null,
    closedAt: null,
    slaPaused: false,
    slaPausedBusinessMinutes: 0,
    allowedNextStatuses: [],
    reopenableUntil: null,
    ...overrides,
  }
}
