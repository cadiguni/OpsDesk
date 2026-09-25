import { Bell } from 'lucide-react'
import { Link } from 'react-router-dom'

import { buttonVariants } from '@/components/ui/button'
import { useUnreadCount } from '@/features/notifications/queries'
import { cn } from '@/lib/utils'

/**
 * Sino com o contador de não lidas, para todo perfil: o responsável recebe os avisos do
 * atendimento e os alertas de SLA; o solicitante, os mesmos avisos que vão por e-mail —
 * que chegam aqui mesmo com o e-mail desligado ou parado no spam (README, seção 18).
 */
export function NotificationBell() {
  const unread = useUnreadCount(true)
  const count = unread.data ?? 0

  // O número vai no nome acessível: o selo visual sozinho não chega a leitor de tela.
  const label = count === 0 ? 'Notificações' : `Notificações, ${count} não lida(s)`

  return (
    <Link
      to="/notificacoes"
      aria-label={label}
      title={label}
      className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }), 'relative')}
    >
      <Bell />
      {count > 0 && (
        <span
          aria-hidden
          className="bg-destructive text-destructive-foreground absolute -top-1 -right-1 grid min-w-5 place-items-center rounded-full px-1 text-[10px] font-semibold"
        >
          {count > 99 ? '99+' : count}
        </span>
      )}
    </Link>
  )
}
