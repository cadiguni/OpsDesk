import { createContext, useContext } from 'react'

import type { UserRole } from '@/domain/enums'
import type * as authApi from '@/features/auth/api'
import type { SessionUser } from '@/features/auth/api'

export type SessionState = {
  user: SessionUser | null

  /** A sessão ainda está sendo restaurada a partir do cookie. */
  isRestoring: boolean

  signIn: (input: authApi.LoginInput) => Promise<void>
  signUp: (input: authApi.RegisterInput) => Promise<void>
  signOut: () => Promise<void>
  changePassword: (input: authApi.ChangePasswordInput) => Promise<void>

  /** Senha provisória: a navegação fica presa na tela de troca até isto ser falso. */
  mustChangePassword: boolean

  /** Perfil do usuário, ou `null` quando não há sessão. */
  role: UserRole | null

  /** Técnico ou gestor. Governa o que a interface mostra, nunca o que a API permite. */
  isStaff: boolean
}

export const SessionContext = createContext<SessionState | null>(null)

export function useSession(): SessionState {
  const session = useContext(SessionContext)

  if (!session) {
    throw new Error('useSession precisa estar dentro de um SessionProvider.')
  }

  return session
}
