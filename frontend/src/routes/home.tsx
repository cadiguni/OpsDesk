import { TicketPriorityBadge, TicketStatusBadge } from '@/components/ticket-badges'
import { ticketPriorities, ticketStatuses, userRoleLabels } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'

/**
 * Tela inicial provisória.
 *
 * Será substituída pela home do usuário (README, seção 13.3) quando os endpoints de
 * chamado existirem. Por ora confirma que a sessão está de pé e mostra o vocabulário
 * visual de status e prioridade.
 */
export function Home() {
  const { user, isStaff } = useSession()

  return (
    <div className="space-y-10">
      <section className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          Olá, {user?.name.split(' ')[0]}
        </h1>
        <p className="text-muted-foreground max-w-prose text-sm">
          Você está autenticado como <strong>{user && userRoleLabels[user.role]}</strong>.
          {isStaff
            ? ' O painel de atendimento entra na próxima etapa.'
            : ' A abertura de chamados entra na próxima etapa.'}
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-semibold">Status do chamado</h2>
        <div className="flex flex-wrap gap-2">
          {ticketStatuses.map((status) => (
            <TicketStatusBadge key={status} status={status} />
          ))}
        </div>
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-semibold">Prioridades</h2>
        <div className="flex flex-wrap gap-2">
          {ticketPriorities.map((priority) => (
            <TicketPriorityBadge key={priority} priority={priority} />
          ))}
        </div>
      </section>
    </div>
  )
}
