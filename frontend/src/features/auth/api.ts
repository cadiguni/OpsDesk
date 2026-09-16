import { api, refreshSession, setAccessToken } from '@/lib/api'
import type { UserRole } from '@/domain/enums'

export type SessionUser = {
  id: string
  name: string
  email: string
  role: UserRole
}

export type AuthResponse = {
  accessToken: string
  expiresAt: string
  user: SessionUser
}

export type LoginInput = {
  email: string
  password: string
}

export type RegisterInput = {
  name: string
  email: string
  password: string
  passwordConfirmation: string
}

export async function login(input: LoginInput): Promise<SessionUser> {
  const { data } = await api.post<AuthResponse>('/api/auth/login', input)

  setAccessToken(data.accessToken)

  return data.user
}

export async function register(input: RegisterInput): Promise<SessionUser> {
  const { data } = await api.post<AuthResponse>('/api/auth/register', input)

  setAccessToken(data.accessToken)

  return data.user
}

/**
 * Restaura a sessão a partir do cookie de refresh.
 *
 * Delega para `refreshSession`, que garante uma renovação por vez. Chamar o endpoint
 * direto daqui era um bug: o `StrictMode` executa o efeito duas vezes, saíam duas
 * renovações concorrentes, e a segunda apresentava um token já rotacionado — o que o
 * servidor lê como vazamento e responde derrubando todas as sessões.
 */
export async function restoreSession(): Promise<SessionUser | null> {
  const refreshed = await refreshSession()

  return refreshed?.user ?? null
}

export async function logout(): Promise<void> {
  try {
    await api.post('/api/auth/logout', null, { skipAuthRefresh: true })
  } finally {
    // O token local sai mesmo que a chamada falhe: a alternativa é a interface seguir
    // se comportando como autenticada depois de o usuário pedir para sair.
    setAccessToken(null)
  }
}
