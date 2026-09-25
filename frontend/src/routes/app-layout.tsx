import type { ReactNode } from 'react'
import { Headphones, LayoutDashboard, LogOut, Mail, Plus, Tags, Ticket, Users } from 'lucide-react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'

import { ThemeSelect } from '@/components/theme-select'
import { Button, buttonVariants } from '@/components/ui/button'
import { userRoleLabels } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { NotificationBell } from '@/features/notifications/notification-bell'
import { cn } from '@/lib/utils'

export function AppLayout() {
  const { user, signOut, role } = useSession()
  const location = useLocation()

  return (
    <div className="min-h-dvh bg-background md:pl-56">
      <a href="#main-content" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-md focus:bg-card focus:p-3">Pular para o conteúdo</a>
      <aside className="border-b bg-card md:fixed md:inset-y-0 md:left-0 md:flex md:w-56 md:flex-col md:border-r md:border-b-0">
        <Link to="/" className="flex items-center gap-3 px-5 py-5">
          <span className="grid size-10 place-items-center rounded-xl bg-primary text-primary-foreground"><Headphones className="size-5" /></span>
          <div><p className="text-lg font-bold tracking-tight">OpsDesk</p><p className="text-xs text-muted-foreground">Central de atendimento</p></div>
        </Link>
        <nav aria-label="Navegação principal" className="flex gap-1 px-3 pb-3 md:flex-col md:pt-5">
          <SidebarLink to="/chamados" icon={<Ticket className="size-4" />}>Chamados</SidebarLink>
          {role === 'Manager' && <SidebarLink to="/dashboard" icon={<LayoutDashboard className="size-4" />}>Dashboard</SidebarLink>}
          {role === 'Manager' && <SidebarLink to="/usuarios" icon={<Users className="size-4" />}>Usuários</SidebarLink>}
          {role === 'Manager' && <SidebarLink to="/categorias" icon={<Tags className="size-4" />}>Categorias</SidebarLink>}
          {role === 'Manager' && <SidebarLink to="/configuracoes/email" icon={<Mail className="size-4" />}>E-mail</SidebarLink>}
        </nav>
        {user && <div className="mt-auto hidden border-t p-4 md:block">
          <p className="truncate text-sm font-semibold">{user.name}</p>
          <p className="mt-1 text-xs text-muted-foreground">{userRoleLabels[user.role]}</p>
        </div>}
      </aside>
      <header className="border-b bg-card/95">
        <div className="mx-auto flex min-h-16 max-w-screen-2xl flex-wrap items-center justify-between gap-3 px-4 py-3 lg:px-8">
          <span className="text-sm font-medium text-muted-foreground">{location.pathname === '/dashboard' ? 'Visão gerencial' : ['/usuarios', '/categorias', '/configuracoes/email'].includes(location.pathname) ? 'Administração' : 'Central de chamados'}</span>
          <div className="flex flex-wrap items-center gap-3">
            <ThemeSelect />
            <NotificationBell />
            <Link to="/chamados/novo" className={cn(buttonVariants({ size: 'sm' }), 'hidden sm:inline-flex')}><Plus /> Novo chamado</Link>
            <Button variant="ghost" size="sm" onClick={() => void signOut()}><LogOut /> Sair</Button>
          </div>
        </div>
      </header>
      <main id="main-content" tabIndex={-1} className="mx-auto max-w-screen-2xl px-4 py-6 outline-none lg:px-8 lg:py-8">
        <Outlet />
      </main>
    </div>
  )
}

function SidebarLink({ to, icon, children }: { to: string; icon: ReactNode; children: string }) {
  return <NavLink to={to} className={({ isActive }) => cn(
    'flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm transition-colors',
    isActive ? 'bg-primary/10 font-semibold text-primary' : 'text-muted-foreground hover:bg-muted hover:text-foreground',
  )}>{icon}{children}</NavLink>
}
