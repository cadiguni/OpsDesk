import { Navigate, Outlet, useLocation } from 'react-router-dom'

import { useSession } from '@/features/auth/session-context'

/**
 * Barra a navegação de quem não tem sessão.
 *
 * Isto é conveniência de interface, não segurança: quem chamar a API direto não passa
 * por aqui. A proteção de verdade é a autorização no backend, e o filtro de visibilidade
 * na query (invariante 1 do CLAUDE.md).
 */
export function ProtectedRoute() {
  const { user, isRestoring } = useSession()
  const location = useLocation()

  // Enquanto a sessão está sendo restaurada pelo cookie não dá para decidir. Redirecionar
  // aqui jogaria para o login todo mundo que recarregou a página estando autenticado.
  if (isRestoring) {
    return (
      <div className="text-muted-foreground grid min-h-dvh place-items-center text-sm">
        Carregando…
      </div>
    )
  }

  if (!user) {
    // Guarda o destino para voltar depois de entrar.
    return <Navigate to="/entrar" replace state={{ from: location.pathname }} />
  }

  return <Outlet />
}
