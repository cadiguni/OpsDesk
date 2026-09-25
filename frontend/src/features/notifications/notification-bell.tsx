import { Bell } from 'lucide-react'
import { Link } from 'react-router-dom'

import { buttonVariants } from '@/components/ui/button'
import { useUnreadCount } from '@/features/notifications/queries'
import { cn } from '@/lib/utils'

/**
 * Sino com o contador de não lidas.
 *
 * Só para a equipe: é o responsável pelo chamado quem recebe aviso no portal, e o
 * solicitante recebe por e-mail (README, seção 18). Um sino que nunca toca, na tela do
 * solicitante, seria um enfeite consultando o servidor a cada minuto.
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
