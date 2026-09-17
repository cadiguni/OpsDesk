import { useState, type ReactNode } from 'react'
import { ArrowLeft, History, MessageSquare } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'

import { TicketPriorityBadge, TicketStatusBadge } from '@/components/ticket-badges'
import { buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ticketSourceLabels } from '@/domain/enums'
import { describeHistoryEntry } from '@/features/tickets/history-label'
import { useComments, useHistory, useTicket } from '@/features/tickets/queries'
import { deadlineToneClasses, describeDeadline } from '@/features/tickets/sla-view'
import { TicketActions } from '@/features/tickets/ticket-actions'
import { TicketConversation } from '@/features/tickets/ticket-conversation'
import type { TicketDetail as TicketDetailModel } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * Detalhe do chamado (README, seção 13.5).
 *
 * Conversa e histórico ocupam o mesmo espaço, em abas, em vez de empilhar três cartões
 * que empurram as ações para fora da tela. São leituras diferentes do mesmo chamado — a
 * conversa é o que foi dito, o histórico é o que mudou — e raramente se lê as duas ao
 * mesmo tempo.
 *
 * As opções de status vêm prontas da API, calculadas pela máquina de estados: a tela não
 * decide o que é transição válida.
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
      <Link
        to="/chamados"
        className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1.5 text-sm"
      >
        <ArrowLeft className="size-4" /> Voltar para a lista
      </Link>

      <div className="space-y-3 rounded-xl border bg-card p-5 shadow-sm">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-muted-foreground font-mono text-xs">{data.code}</span>
          <TicketStatusBadge status={data.status} />
          <TicketPriorityBadge priority={data.priority} />
        </div>

        <h1 className="text-2xl font-semibold tracking-tight break-words">{data.title}</h1>

        <p className="text-muted-foreground text-sm">
          Aberto por <span className="text-foreground">{data.requesterName}</span> em{' '}
          {formatDateTime(data.createdAt)} ·{' '}
          {data.assignedTechnicianName ? (
            <>
              atendido por <span className="text-foreground">{data.assignedTechnicianName}</span>
            </>
          ) : (
            <span className="text-sla-due-soon font-medium">sem responsável</span>
          )}
        </p>
      </div>

      <div className="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
        <div className="min-w-0">
          <TicketTabs ticket={data} />
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Gerenciar chamado</CardTitle>
            </CardHeader>
            <CardContent>
              <TicketActions ticket={data} />
            </CardContent>
          </Card>

          <TicketSla ticket={data} />

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Propriedades</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-3 text-sm">
              <Row label="Categoria" value={data.categoryName} />
              <Row label="Solicitante" value={data.requesterName} />
              <Row label="Responsável" value={data.assignedTechnicianName ?? 'Sem responsável'} />
              <Row label="Origem" value={ticketSourceLabels[data.source]} />
              <Row label="Aberto em" value={formatDateTime(data.createdAt)} />
              {data.resolvedAt && (
                <Row label="Resolvido em" value={formatDateTime(data.resolvedAt)} />
              )}
              {data.closedAt && <Row label="Fechado em" value={formatDateTime(data.closedAt)} />}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  )
}

/** Conversa e histórico no mesmo espaço. */
function TicketTabs({ ticket }: { ticket: TicketDetailModel }) {
  const [tab, setTab] = useState<'conversation' | 'history'>('conversation')
  const comments = useComments(ticket.id)

  // A descrição de abertura conta como a primeira mensagem da conversa.
  const messageCount = 1 + (comments.data?.length ?? 0)

  return (
    <Card>
      <div className="flex gap-1 border-b p-2" role="tablist" aria-label="Conteúdo do chamado">
        <Tab active={tab === 'conversation'} onClick={() => setTab('conversation')}>
          <MessageSquare className="size-4" /> Conversa
          <span className="text-muted-foreground">({messageCount})</span>
        </Tab>

        <Tab active={tab === 'history'} onClick={() => setTab('history')}>
          <History className="size-4" /> Histórico
        </Tab>
      </div>

      <CardContent className="p-5">
        {tab === 'conversation' ? (
          <TicketConversation ticket={ticket} />
        ) : (
          <TicketHistory ticketId={ticket.id} />
        )}
      </CardContent>
    </Card>
  )
}

function Tab({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={cn(
        'flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
        'focus-visible:ring-ring/50 focus-visible:ring-[3px] focus-visible:outline-none',
        active ? 'bg-primary/10 text-primary' : 'text-muted-foreground hover:bg-accent',
      )}
    >
      {children}
    </button>
  )
}

/** Painel de SLA: os dois prazos, o estado da pausa e o tempo já gasto em espera. */
function TicketSla({ ticket }: { ticket: TicketDetailModel }) {
  const tracked = ticket.status !== 'Cancelled'

  const response = describeDeadline(ticket.slaResponseDueAt, ticket.firstRespondedAt, 'respondido')
  const resolution = describeDeadline(ticket.slaResolutionDueAt, ticket.resolvedAt, 'resolvido')

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">SLA</CardTitle>
      </CardHeader>

      <CardContent className="grid gap-3 text-sm">
        {tracked ? (
          <>
            <Deadline label="Resposta" view={response} />
            <Deadline label="Resolução" view={resolution} />

            {ticket.slaPaused && (
              <p className="text-sla-due-soon text-xs">
                O prazo de resolução está pausado enquanto o chamado aguarda o solicitante. O
                prazo de resposta continua correndo.
              </p>
            )}

            {ticket.slaPausedBusinessMinutes > 0 && (
              <Row
                label="Tempo em espera"
                value={`${Math.round(ticket.slaPausedBusinessMinutes / 60)} h úteis`}
              />
            )}
          </>
        ) : (
          <p className="text-muted-foreground text-xs">
            Chamado cancelado fica fora dos indicadores de SLA.
          </p>
        )}
      </CardContent>
    </Card>
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

function Deadline({ label, view }: { label: string; view: ReturnType<typeof describeDeadline> }) {
  return (
    <div className="grid grid-cols-[7rem_1fr] items-baseline gap-2">
      <span className="text-muted-foreground text-xs">{label}</span>
      <span className={deadlineToneClasses[view.tone]}>{view.long}</span>
    </div>
  )
}

/** Linha do tempo do chamado, a partir do que o interceptor gravou. */
function TicketHistory({ ticketId }: { ticketId: string }) {
  const history = useHistory(ticketId)

  if (history.isPending) {
    return <p className="text-muted-foreground text-sm">Carregando…</p>
  }

  if (history.data?.length === 0) {
    return <p className="text-muted-foreground text-sm">Sem eventos registrados.</p>
  }

  return (
    <ol className="border-primary/20 space-y-4 border-l-2 pl-4 text-sm">
      {history.data?.map((entry) => (
        <li key={entry.id} className="flex flex-wrap items-baseline gap-2">
          <span className="text-muted-foreground shrink-0 font-mono text-xs">
            {formatDateTime(entry.createdAt)}
          </span>
          <span>{describeHistoryEntry(entry)}</span>
        </li>
      ))}
    </ol>
  )
}
