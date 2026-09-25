import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as settingsApi from '@/features/settings/api'
import type { EmailSettings } from '@/features/settings/api'
import { EmailSettingsPage } from '@/routes/email-settings'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/settings/api')

function configured(overrides: Partial<EmailSettings> = {}): EmailSettings {
  return {
    isEnabled: true,
    senderAddress: 'suporte@empresa.com',
    senderName: 'Suporte TI',
    tenantId: 'empresa.onmicrosoft.com',
    clientId: '11111111-2222-3333-4444-555555555555',
    hasClientSecret: true,
    clientSecretReadable: true,
    clientSecretUpdatedAt: '2026-09-20T12:00:00Z',
    clientSecretExpiresOn: '2028-09-01',
    portalUrl: 'https://opsdesk.empresa.com',
    lastAttemptAt: null,
    lastSucceededAt: null,
    lastError: null,
    pendingCount: 0,
    failedCount: 0,
    ...overrides,
  }
}

beforeEach(() => {
  vi.mocked(settingsApi.getEmailSettings).mockReset()
  vi.mocked(settingsApi.updateEmailSettings).mockReset()
  vi.mocked(settingsApi.updateEmailSettings).mockImplementation(async () => configured())
})

describe('client secret só de escrita', () => {
  it('não oferece campo de secret enquanto ninguém pedir para trocar', async () => {
    vi.mocked(settingsApi.getEmailSettings).mockResolvedValue(configured())

    renderWithProviders(<EmailSettingsPage />, { role: 'Manager' })

    expect(await screen.findByText(/configurado em/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/novo client secret/i)).not.toBeInTheDocument()
  })

  it('salvar sem trocar o secret não manda secret nenhum', async () => {
    const user = userEvent.setup()
    vi.mocked(settingsApi.getEmailSettings).mockResolvedValue(configured())

    renderWithProviders(<EmailSettingsPage />, { role: 'Manager' })

    await user.click(await screen.findByRole('button', { name: 'Salvar' }))

    await waitFor(() => expect(settingsApi.updateEmailSettings).toHaveBeenCalled())
    expect(vi.mocked(settingsApi.updateEmailSettings).mock.calls[0]![0]).not.toHaveProperty('clientSecret')
  })

  it('trocar o secret manda o valor digitado', async () => {
    const user = userEvent.setup()
    vi.mocked(settingsApi.getEmailSettings).mockResolvedValue(configured())

    renderWithProviders(<EmailSettingsPage />, { role: 'Manager' })

    await user.click(await screen.findByRole('button', { name: /trocar secret/i }))
    await user.type(screen.getByLabelText(/novo client secret/i), 'secret-novo')
    await user.click(screen.getByRole('button', { name: 'Salvar' }))

    await waitFor(() => expect(settingsApi.updateEmailSettings).toHaveBeenCalled())
    expect(vi.mocked(settingsApi.updateEmailSettings).mock.calls[0]![0]).toMatchObject({ clientSecret: 'secret-novo' })
  })

  it('avisa quando o servidor não consegue mais ler o secret gravado', async () => {
    vi.mocked(settingsApi.getEmailSettings).mockResolvedValue(configured({ clientSecretReadable: false }))

    renderWithProviders(<EmailSettingsPage />, { role: 'Manager' })

    expect(await screen.findByText(/não pode mais ser lido/i)).toBeInTheDocument()
  })
})

describe('validade do secret', () => {
  it('avisa quando o secret está perto de vencer', async () => {
    const soon = new Date(Date.now() + 10 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10)
    vi.mocked(settingsApi.getEmailSettings).mockResolvedValue(configured({ clientSecretExpiresOn: soon }))

    renderWithProviders(<EmailSettingsPage />, { role: 'Manager' })

    expect(await screen.findByText(/o client secret vence em/i)).toBeInTheDocument()
  })

  it('não avisa quando falta muito', async () => {
    vi.mocked(settingsApi.getEmailSettings).mockResolvedValue(configured())

    renderWithProviders(<EmailSettingsPage />, { role: 'Manager' })

    await screen.findByText(/configurado em/i)
    expect(screen.queryByText(/o client secret vence/i)).not.toBeInTheDocument()
  })
})

it('não carrega a configuração para quem não é gestor', async () => {
  renderWithProviders(<EmailSettingsPage />, { role: 'Technician' })

  expect(await screen.findByText(/apenas gestores/i)).toBeInTheDocument()
  expect(settingsApi.getEmailSettings).not.toHaveBeenCalled()
})
