import { Link } from 'react-router-dom'

import { buttonVariants } from '@/components/ui/button'
import {
  ticketPriorityLabels,
  ticketStatusLabels,
  type TicketPriority,
  type TicketStatus,
} from '@/domain/enums'
import { ChartFrame, HorizontalBars, VerticalBars } from '@/features/dashboard/chart-primitives'
import { useDashboard } from '@/features/tickets/queries'
import type { CountByLabel } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'
import { cn } from '@/lib/utils'

/** Dashboard do gestor (README, seção 13.7). */
export function Dashboard() {
  const dashboard = useDashboard()

  if (dashboard.isPending) {
    return <p className="text-muted-foreground text-sm">Carregando indicadores…</p>
  }

  if (dashboard.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(dashboard.error, 'Não foi possível carregar o dashboard.')}
      </p>
    )
  }

  const data = dashboard.data

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Dashboard</h1>
          <p className="text-muted-foreground text-sm">Visão geral da operação.</p>
        </div>

        <Link to="/chamados" className={buttonVariants({ variant: 'outline', size: 'sm' })}>
          Ver chamados
        </Link>
      </div>

      {/* Números não viram gráfico: um total é um número, e desenhá-lo como barra de uma
          coluna só dá mais tinta para a mesma informação. */}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
        <Tile label="Total" value={data.total} />
        <Tile label="Abertos" value={data.open} />
        <Tile label="Em atendimento" value={data.inProgress} />
        <Tile label="Aguardando usuário" value={data.waitingOnRequester} />
        <Tile
          label="Resposta vencida"
          value={data.overdueResponse}
          tone={data.overdueResponse > 0 ? 'overdue' : 'neutral'}
          to="/chamados?overdue=true"
        />
        <Tile
          label="Resolução vencida"
          value={data.overdueResolution}
          tone={data.overdueResolution > 0 ? 'overdue' : 'neutral'}
          to="/chamados?overdue=true"
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <ChartFrame title="Chamados por status" data={translate(data.byStatus, statusLabel)}>
          <VerticalBars data={translate(data.byStatus, statusLabel)} />
        </ChartFrame>

        <ChartFrame title="Chamados por prioridade" data={translate(data.byPriority, priorityLabel)}>
          {/* Ordinal: rampa de um matiz, da menor para a maior severidade. */}
          <VerticalBars data={translate(data.byPriority, priorityLabel)} ordinal />
        </ChartFrame>

        <ChartFrame title="Chamados por categoria" data={data.byCategory}>
          <HorizontalBars data={data.byCategory} />
        </ChartFrame>

        <ChartFrame title="Chamados por técnico" data={data.byTechnician}>
          <HorizontalBars data={data.byTechnician} />
        </ChartFrame>
      </div>
    </div>
  )
}

function Tile({
  label,
  value,
  tone = 'neutral',
  to,
}: {
  label: string
  value: number
  tone?: 'neutral' | 'overdue'
  to?: string
}) {
  const content = (
    <>
      <p className="text-muted-foreground text-xs">{label}</p>
      <p
        className={cn(
          'text-2xl font-semibold tabular-nums',
          tone === 'overdue' && 'text-sla-overdue',
        )}
      >
        {value}
      </p>
    </>
  )

  // O tile de vencidos leva para a lista já filtrada: ver o número e não conseguir
  // chegar nos chamados por trás dele é a frustração clássica de dashboard.
  return to ? (
    <Link to={to} className="hover:bg-accent rounded-lg border p-4 transition-colors">
      {content}
    </Link>
  ) : (
    <div className="rounded-lg border p-4">{content}</div>
  )
}

/**
 * Traduz o rótulo que a API devolve.
 *
 * O backend manda o valor do enum em inglês, porque é o que ele persiste. A tradução é
 * da interface, conforme a convenção de idioma do projeto.
 */
function translate(counts: CountByLabel[], label: (value: string) => string): CountByLabel[] {
  return counts.map((row) => ({ ...row, label: label(row.label) }))
}

function statusLabel(value: string): string {
  return value in ticketStatusLabels ? ticketStatusLabels[value as TicketStatus] : value
}

function priorityLabel(value: string): string {
  return value in ticketPriorityLabels ? ticketPriorityLabels[value as TicketPriority] : value
}
