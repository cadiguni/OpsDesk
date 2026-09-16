import { Link, useParams } from 'react-router-dom'

import { TicketPriorityBadge, TicketStatusBadge } from '@/components/ticket-badges'
import { buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ticketSourceLabels, ticketStatusLabels } from '@/domain/enums'
import { useTicket } from '@/features/tickets/queries'
import { errorMessage } from '@/lib/api'
import { formatDateTime, formatDeadlineDistance } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * Detalhe do chamado (README, seção 13.5).
 *
 * Nesta etapa é somente leitura. Comentários, mudança de status e atribuição entram na
 * etapa de atendimento — as transições que o perfil pode fazer já vêm da API em
 * `allowedNextStatuses` e estão exibidas aqui como prévia do que virá.
 */
export function TicketDetail() {
  const { id } = useParams<{ id: string }>()
  const ticket = useTicket(id!)

  if (ticket.isPending) {
    return <p className="text-muted-foreground text-sm">Carregando chamado…</p>
  }

  if (ticket.isError) {
    return (
      <div className="space-y-4">
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(ticket.error, 'Não foi possível carregar o chamado.')}
        </p>
        <Link to="/chamados" className={buttonVariants({ variant: 'outline' })}>
          Voltar para a lista
        </Link>
      </div>
    )
  }

  const data = ticket.data

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <p className="text-muted-foreground font-mono text-xs">{data.code}</p>
          <h1 className="text-xl font-semibold tracking-tight">{data.title}</h1>
          <div className="flex flex-wrap items-center gap-2 pt-1">
            <TicketStatusBadge status={data.status} />
            <TicketPriorityBadge priority={data.priority} />
          </div>
        </div>

        <Link to="/chamados" className={buttonVariants({ variant: 'outline', size: 'sm' })}>
          Voltar
        </Link>
      </div>

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Descrição</CardTitle>
            </CardHeader>
            <CardContent>
              {/* whitespace-pre-wrap preserva as quebras de linha do que o solicitante
                  digitou, que costuma ser log colado. */}
              <p className="text-sm whitespace-pre-wrap">{data.description}</p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Andamento</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <p className="text-muted-foreground">
                Comentários e histórico entram na próxima etapa.
              </p>

              {data.allowedNextStatuses.length > 0 && (
                <div className="space-y-1.5">
                  <p className="text-xs font-medium">Transições disponíveis para você</p>
                  <div className="flex flex-wrap gap-1.5">
                    {data.allowedNextStatuses.map((status) => (
                      <span
                        key={status}
                        className="bg-muted text-muted-foreground rounded-md px-2 py-0.5 text-xs"
                      >
                        {ticketStatusLabels[status]}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Dados</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-3 text-sm">
              <Row label="Categoria" value={data.categoryName} />
              <Row label="Solicitante" value={data.requesterName} />
              <Row label="Responsável" value={data.assignedTechnicianName ?? 'Sem responsável'} />
              <Row label="Origem" value={ticketSourceLabels[data.source]} />
              <Row label="Aberto em" value={formatDateTime(data.createdAt)} />
              {data.resolvedAt && <Row label="Resolvido em" value={formatDateTime(data.resolvedAt)} />}
              {data.closedAt && <Row label="Fechado em" value={formatDateTime(data.closedAt)} />}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">SLA</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-3 text-sm">
              <Deadline
                label="Resposta"
                dueAt={data.slaResponseDueAt}
                metAt={data.firstRespondedAt}
                metLabel="respondido"
              />
              <Deadline
                label="Resolução"
                dueAt={data.slaResolutionDueAt}
                metAt={data.resolvedAt}
                metLabel="resolvido"
              />

              {data.slaPaused && (
                <p className="text-sla-due-soon text-xs">
                  O prazo de resolução está pausado enquanto o chamado aguarda o solicitante.
                </p>
              )}

              {data.slaPausedBusinessMinutes > 0 && (
                <Row
                  label="Tempo em espera"
                  value={`${Math.round(data.slaPausedBusinessMinutes / 60)} h úteis`}
                />
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid grid-cols-[7rem_1fr] items-baseline gap-2">
      <span className="text-muted-foreground text-xs">{label}</span>
      <span>{value}</span>
    </div>
  )
}

function Deadline({
  label,
  dueAt,
  metAt,
  metLabel,
}: {
  label: string
  dueAt: string
  metAt: string | null
  metLabel: string
}) {
  const met = metAt !== null
  const overdue = !met && new Date(dueAt) < new Date()

  return (
    <div className="grid grid-cols-[7rem_1fr] items-baseline gap-2">
      <span className="text-muted-foreground text-xs">{label}</span>
      <span className={cn(overdue && 'text-sla-overdue font-medium', met && 'text-sla-ok')}>
        {met ? `${metLabel} em ${formatDateTime(metAt!)}` : formatDeadlineDistance(dueAt)}
      </span>
    </div>
  )
}
