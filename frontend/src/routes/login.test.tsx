import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { PublicOptions } from '@/features/auth/options'
import { Login } from '@/routes/login'
import { renderWithProviders } from '@/test/render'

// O hook inteiro, e não só a função de busca: o hook chama a função de dentro do próprio
// módulo, e um mock da exportação não chegaria lá.
const state = vi.hoisted(() => ({ options: undefined as PublicOptions | undefined }))

vi.mock('@/features/auth/options', () => ({
  usePublicOptions: () => ({ data: state.options }),
}))

function withOptions(overrides: Partial<PublicOptions>) {
  state.options = {
    organizationName: 'Central de TI ACME',
    allowSelfRegistration: true,
    allowedEmailDomains: [],
    passwordResetAvailable: true,
    ...overrides,
  }
}

describe('tela de login', () => {
  it('mostra o nome da organização e os dois atalhos quando eles funcionam', async () => {
    withOptions({})

    renderWithProviders(<Login />, { route: '/entrar' })

    expect(await screen.findByText('Entrar no Central de TI ACME')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Esqueci minha senha' })).toHaveAttribute('href', '/esqueci-senha')
    expect(screen.getByRole('link', { name: 'Cadastre-se' })).toBeInTheDocument()
  })

  it('não oferece o que não funcionaria', async () => {
    withOptions({ allowSelfRegistration: false, passwordResetAvailable: false })

    renderWithProviders(<Login />, { route: '/entrar' })

    await screen.findByText('Entrar no Central de TI ACME')
    expect(screen.queryByRole('link', { name: 'Esqueci minha senha' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Cadastre-se' })).not.toBeInTheDocument()
  })
})
