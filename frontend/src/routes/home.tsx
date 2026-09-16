import { TicketPriorityBadge, TicketStatusBadge } from '@/components/ticket-badges'
import { useApiHealth } from '@/features/health/use-api-health'
import { ticketPriorities, ticketStatuses } from '@/domain/enums'
import { errorMessage } from '@/lib/api'

/**
 * Tela inicial da fundação do projeto.
 *
 * Existe por um motivo prático: provar que a SPA fala com a API e que a API fala com o
 * PostgreSQL. Será substituída pela home do usuário (README, seção 13.3) quando os
 * endpoints de chamado existirem.
 */
export function Home() {
  const health = useApiHealth()

  return (
    <div className="space-y-10">
      <section className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">OpsDesk</h1>
        <p className="text-muted-foreground max-w-prose text-sm">
          Sistema interno de chamados de TI. A base do projeto está de pé: domínio,
          persistência, cálculo de SLA em horas úteis e auditoria automática do chamado.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-semibold">Conexão com a API</h2>

        <div className="rounded-lg border p-4 text-sm">
          {health.isPending && <p className="text-muted-foreground">Consultando a API…</p>}

          {health.isError && (
            <div className="space-y-1">
              <p className="text-destructive font-medium">API indisponível</p>
              <p className="text-muted-foreground">{errorMessage(health.error)}</p>
              <p className="text-muted-foreground">
                Suba o ambiente local com <code className="font-mono">docker compose up</code>.
              </p>
            </div>
          )}

          {health.isSuccess && (
            <div className="space-y-1">
              <p className="text-sla-ok font-medium">API e banco respondendo</p>
              <p className="text-muted-foreground">
                Health check: <code className="font-mono">{health.data.status}</code>
              </p>
            </div>
          )}
        </div>
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
