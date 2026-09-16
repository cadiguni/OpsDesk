import { Link, NavLink, Outlet } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { userRoleLabels } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { cn } from '@/lib/utils'

export function AppLayout() {
  const { user, signOut, role } = useSession()

  return (
    <div className="min-h-dvh bg-background">
      <header className="border-b">
        <div className="mx-auto flex h-14 max-w-5xl items-center gap-3 px-4">
          <Link to="/" className="flex items-center gap-3">
            <span className="grid size-7 place-items-center rounded-md bg-primary text-sm font-bold text-primary-foreground">
              O
            </span>
            <div className="leading-tight">
              <p className="text-sm font-semibold">OpsDesk</p>
              <p className="text-muted-foreground text-xs">Chamados de TI</p>
            </div>
          </Link>

          {user && (
            <nav className="ml-4 flex items-center gap-1 text-sm">
              <HeaderLink to="/chamados">Chamados</HeaderLink>

              {/* O dashboard é do gestor. Esconder o link é conveniência: a API recusa
                  o acesso de qualquer forma, pela política de rota. */}
              {role === 'Manager' && <HeaderLink to="/dashboard">Dashboard</HeaderLink>}
            </nav>
          )}

          {user && (
            <div className="ml-auto flex items-center gap-4">
              <div className="hidden text-right leading-tight sm:block">
                <p className="text-sm font-medium">{user.name}</p>
                <p className="text-muted-foreground text-xs">{userRoleLabels[user.role]}</p>
              </div>

              <Button variant="outline" size="sm" onClick={() => void signOut()}>
                Sair
              </Button>
            </div>
          )}
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}

function HeaderLink({ to, children }: { to: string; children: string }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        cn(
          'rounded-md px-2.5 py-1.5 transition-colors',
          isActive ? 'bg-accent font-medium' : 'text-muted-foreground hover:text-foreground',
        )
      }
    >
      {children}
    </NavLink>
  )
}
