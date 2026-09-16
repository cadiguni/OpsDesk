import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'

import { isStaff } from '@/domain/enums'
import { setSessionLostHandler } from '@/lib/api'
import * as authApi from '@/features/auth/api'
import type { SessionUser } from '@/features/auth/api'
import { SessionContext, type SessionState } from '@/features/auth/session-context'

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null)
  const [isRestoring, setIsRestoring] = useState(true)
  const queryClient = useQueryClient()

  const clearSession = useCallback(() => {
    setUser(null)

    // O cache guarda respostas filtradas pelo usuário anterior. Mantê-lo depois de
    // trocar de sessão mostraria dados de quem saiu para quem entrou.
    queryClient.clear()
  }, [queryClient])

  // Ao carregar a página o token de acesso não existe — ele vive em memória e se foi com
  // o reload. A sessão é reconstruída pelo cookie de refresh, que sobrevive.
  useEffect(() => {
    let active = true

    void authApi.restoreSession().then((restored) => {
      if (!active) {
        return
      }

      setUser(restored)
      setIsRestoring(false)
    })

    return () => {
      active = false
    }
  }, [])

  // Quando a renovação falha no meio da navegação, o interceptor avisa e a interface
  // volta para a tela de login em vez de ficar tentando chamadas que sempre darão 401.
  useEffect(() => {
    setSessionLostHandler(clearSession)

    return () => setSessionLostHandler(null)
  }, [clearSession])

  const signIn = useCallback(
    async (input: authApi.LoginInput) => {
      setUser(await authApi.login(input))
    },
    [],
  )

  const signUp = useCallback(
    async (input: authApi.RegisterInput) => {
      setUser(await authApi.register(input))
    },
    [],
  )

  const signOut = useCallback(async () => {
    await authApi.logout()
    clearSession()
  }, [clearSession])

  const value = useMemo<SessionState>(
    () => ({
      user,
      isRestoring,
      signIn,
      signUp,
      signOut,
      role: user?.role ?? null,
      isStaff: user ? isStaff(user.role) : false,
    }),
    [user, isRestoring, signIn, signUp, signOut],
  )

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}
