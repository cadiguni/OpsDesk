import { api } from '@/lib/api'

/**
 * Configuração do envio de e-mail como a API a devolve.
 *
 * Não há campo com o client secret, e não é esquecimento: a API nunca o devolve. A tela
 * só sabe se ele existe, se o servidor ainda consegue lê-lo e quando foi trocado.
 */
export type EmailSettings = {
  isEnabled: boolean
  senderAddress: string | null
  senderName: string | null
  tenantId: string | null
  clientId: string | null
  hasClientSecret: boolean
  clientSecretReadable: boolean
  clientSecretUpdatedAt: string | null
  /** `yyyy-mm-dd`, como o Entra ID mostra a validade. */
  clientSecretExpiresOn: string | null
  portalUrl: string | null
  lastAttemptAt: string | null
  lastSucceededAt: string | null
  lastError: string | null
  pendingCount: number
  failedCount: number
}

export type UpdateEmailSettingsInput = {
  isEnabled: boolean
  senderAddress: string | null
  senderName: string | null
  tenantId: string | null
  clientId: string | null
  /** Ausente mantém o secret gravado. */
  clientSecret?: string
  clientSecretExpiresOn: string | null
  portalUrl: string | null
}

export async function getEmailSettings(): Promise<EmailSettings> {
  const { data } = await api.get<EmailSettings>('/api/admin/settings/email')

  return data
}

export async function updateEmailSettings(input: UpdateEmailSettingsInput): Promise<EmailSettings> {
  const { data } = await api.put<EmailSettings>('/api/admin/settings/email', input)

  return data
}

export async function sendTestEmail(toAddress: string): Promise<void> {
  await api.post('/api/admin/settings/email/test', { toAddress })
}

export type GeneralSettings = {
  organizationName: string
  allowSelfRegistration: boolean
  allowedEmailDomains: string[]
  microsoftSignInEnabled: boolean

  /** O aplicativo do Entra ID da configuração de e-mail está completo. Só leitura. */
  microsoftSignInReady: boolean
}

export type UpdateGeneralSettingsInput = Omit<GeneralSettings, 'microsoftSignInReady'>

export async function getGeneralSettings(): Promise<GeneralSettings> {
  const { data } = await api.get<GeneralSettings>('/api/admin/settings/general')

  return data
}

export async function updateGeneralSettings(input: UpdateGeneralSettingsInput): Promise<GeneralSettings> {
  const { data } = await api.put<GeneralSettings>('/api/admin/settings/general', input)

  return data
}
