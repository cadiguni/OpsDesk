import { api, REFRESH_PATH, setAccessToken } from '@/lib/api'
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
 * `skipAuthRefresh` evita que o 401 desta chamada — o caso normal de quem não está
 * autenticado — dispare o interceptor e vire uma segunda tentativa de renovação.
 */
export async function restoreSession(): Promise<SessionUser | null> {
  try {
    const { data } = await api.post<AuthResponse>(REFRESH_PATH, null, {
      skipAuthRefresh: true,
    })

    setAccessToken(data.accessToken)

    return data.user
  } catch {
    setAccessToken(null)

    return null
  }
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
