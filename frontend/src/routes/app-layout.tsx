import { Link, Outlet } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { userRoleLabels } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'

export function AppLayout() {
  const { user, signOut } = useSession()

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

      <main className="mx-auto max-w-5xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
