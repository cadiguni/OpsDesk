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
    microsoftSignInAvailable: false,
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

  it('oferece a conta Microsoft só quando o gestor ligou e o aplicativo está configurado', async () => {
    withOptions({ microsoftSignInAvailable: true })

    renderWithProviders(<Login />, { route: '/entrar' })

    const button = await screen.findByRole('link', { name: 'Entrar com a conta Microsoft' })
    const href = new URL(button.getAttribute('href')!)

    // Vai à API, que leva à Microsoft; o destino volta como endereço completo, porque a API
    // pode estar em outra origem e precisa saber para onde devolver o navegador.
    expect(href.pathname).toBe('/api/auth/microsoft/start')
    expect(href.searchParams.get('returnTo')).toBe(`${window.location.origin}/`)
  })

  it('sem o login Microsoft disponível, não mostra o botão', async () => {
    withOptions({ microsoftSignInAvailable: false })

    renderWithProviders(<Login />, { route: '/entrar' })

    await screen.findByText('Entrar no Central de TI ACME')
    expect(screen.queryByRole('link', { name: 'Entrar com a conta Microsoft' })).not.toBeInTheDocument()
  })

  it('explica o retorno do login Microsoft que não deu certo', async () => {
    withOptions({ microsoftSignInAvailable: true })

    renderWithProviders(<Login />, { route: '/entrar?microsoft=desativada' })

    expect(await screen.findByRole('alert')).toHaveTextContent('Sua conta está desativada.')
  })
})
