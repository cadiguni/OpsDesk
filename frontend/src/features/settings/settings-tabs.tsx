import { NavLink } from 'react-router-dom'

import { cn } from '@/lib/utils'

/** Abas das configurações do sistema. Cada aba é uma rota, para o endereço ser compartilhável. */
export function SettingsTabs() {
  return (
    <nav aria-label="Seções das configurações" className="flex gap-1 border-b">
      <Tab to="/configuracoes/geral">Geral</Tab>
      <Tab to="/configuracoes/email">E-mail</Tab>
    </nav>
  )
}

function Tab({ to, children }: { to: string; children: string }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        cn(
          '-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors',
          isActive ? 'border-primary text-primary' : 'text-muted-foreground hover:text-foreground border-transparent',
        )
      }
    >
      {children}
    </NavLink>
  )
}
