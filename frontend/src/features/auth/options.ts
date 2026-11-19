import { useQuery } from '@tanstack/react-query'

import { api } from '@/lib/api'

/**
 * O que a tela de login oferece, lido antes de existir sessão. A interface só mostra
 * "Criar conta" e "Esqueci minha senha" quando eles funcionam de verdade.
 */
export type PublicOptions = {
  organizationName: string
  allowSelfRegistration: boolean
  allowedEmailDomains: string[]
  passwordResetAvailable: boolean

  /** O gestor ligou o login Microsoft e o aplicativo do Entra ID está configurado. */
  microsoftSignInAvailable: boolean
}

export async function getPublicOptions(): Promise<PublicOptions> {
  const { data } = await api.get<PublicOptions>('/api/auth/options', { skipAuthRefresh: true })

  return data
}

export function usePublicOptions() {
  return useQuery({
    queryKey: ['auth-options'],
    queryFn: getPublicOptions,
    staleTime: 5 * 60 * 1000,
  })
}

/** Pede o link de redefinição. A resposta é a mesma exista ou não a conta. */
export async function requestPasswordReset(email: string): Promise<void> {
  await api.post('/api/auth/password/forgot', { email }, { skipAuthRefresh: true })
}

export async function resetPassword(input: {
  token: string
  newPassword: string
  newPasswordConfirmation: string
}): Promise<void> {
  await api.post('/api/auth/password/reset', input, { skipAuthRefresh: true })
}
