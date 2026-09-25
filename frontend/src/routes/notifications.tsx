import { useNavigate, useSearchParams } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import type { NotificationItem } from '@/features/notifications/api'
import { describeNotification } from '@/features/notifications/notification-label'
import { useMarkAllRead, useMarkRead, useNotifications } from '@/features/notifications/queries'
import { errorMessage } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

/** Notificações do responsável pelo chamado (README, seção 18, versão 1.2). */
export function Notifications() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()

  const unreadOnly = params.get('unreadOnly') === 'true'
  const page = Number(params.get('page') ?? '1')

  const notifications = useNotifications({ unreadOnly: unreadOnly || undefined, page })
  const markRead = useMarkRead()
  const markAllRead = useMarkAllRead()

  function open(item: NotificationItem) {
    if (!item.readAt) {
      markRead.mutate(item.id)
    }

    void navigate(`/chamados/${item.ticketId}`)
  }

  function setPage(next: number) {
    const search = new URLSearchParams(params)
    search.set('page', String(next))
    setParams(search, { replace: true })
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Notificações</h1>
          <p className="text-muted-foreground text-sm">
            Chamados sob sua responsabilidade que mudaram.
          </p>
        </div>

        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            aria-pressed={unreadOnly}
            onClick={() => setParams(unreadOnly ? {} : { unreadOnly: 'true' }, { replace: true })}
          >
            {unreadOnly ? 'Mostrar todas' : 'Só não lidas'}
          </Button>
          <Button
            size="sm"
            disabled={markAllRead.isPending}
            onClick={() => markAllRead.mutate()}
          >
            Marcar todas como lidas
          </Button>
        </div>
      </div>

      {notifications.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(notifications.error, 'Não foi possível carregar as notificações.')}
        </p>
      )}

      {notifications.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}

      {notifications.data?.items.length === 0 && (
        <div className="text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
          {unreadOnly ? 'Nenhuma notificação não lida.' : 'Nenhuma notificação ainda.'}
        </div>
      )}

      {notifications.data && notifications.data.items.length > 0 && (
        <ul className="divide-y rounded-xl border bg-card shadow-sm">
          {notifications.data.items.map((item) => (
            <li key={item.id}>
              <button
                type="button"
                onClick={() => open(item)}
                className={cn(
                  'hover:bg-accent/40 flex w-full items-start gap-3 p-4 text-left text-sm transition-colors',
                  !item.readAt && 'bg-primary/5',
                )}
              >
                <span
                  aria-hidden
                  className={cn('mt-1.5 size-2 shrink-0 rounded-full', item.readAt ? 'bg-transparent' : 'bg-primary')}
                />
                <span className="min-w-0 flex-1">
                  <span className={cn('block', !item.readAt && 'font-semibold')}>
                    {describeNotification(item)}
                    {!item.readAt && <span className="sr-only"> (não lida)</span>}
                  </span>
                  <span className="text-muted-foreground block truncate">
                    {item.ticketCode} · {item.ticketTitle}
                  </span>
                </span>
                <span className="text-muted-foreground shrink-0 text-xs">{formatDateTime(item.createdAt)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {notifications.data && notifications.data.totalPages > 1 && (
        <div className="flex items-center justify-between text-sm">
          <p className="text-muted-foreground">
            Página {notifications.data.page} de {notifications.data.totalPages}
          </p>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled={notifications.data.page <= 1} onClick={() => setPage(page - 1)}>
              Anterior
            </Button>
            <Button variant="outline" size="sm" disabled={!notifications.data.hasNextPage} onClick={() => setPage(page + 1)}>
              Próxima
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
