import { useState } from 'react'
import { X } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Select } from '@/components/ui/select'
import { ticketStatusLabels, type TicketStatus } from '@/domain/enums'
import type { BulkStatusItem } from '@/features/tickets/api'
import { useBulkChangeStatus } from '@/features/tickets/queries'
import { errorMessage } from '@/lib/api'

/** Destinos oferecidos em lote. "Aberto" não é destino: nenhum chamado volta para lá. */
const bulkTargets: TicketStatus[] = ['Closed', 'Resolved', 'Cancelled', 'InProgress', 'WaitingOnRequester', 'Triage']

/** Verbo da confirmação, no infinitivo: "Confirmar: fechar 12 chamados". */
const verbs: Record<TicketStatus, string> = {
  Open: 'reabrir',
  Triage: 'enviar para triagem',
  InProgress: 'pôr em atendimento',
  WaitingOnRequester: 'pôr aguardando o solicitante',
  Resolved: 'marcar como resolvidos',
  Closed: 'fechar',
  Cancelled: 'cancelar',
}

/**
 * Barra de ação da seleção na lista de chamados. Toda mudança em lote pede uma segunda
 * confirmação, como fechar e cancelar na tela do chamado: aqui ela vale para muitos de
 * uma vez, e cada solicitante recebe o aviso como no clique individual.
 *
 * O servidor aplica chamado a chamado. O que não pôde mudar (cancelado não fecha, fechado
 * há tempo demais não reabre) volta com o motivo, e a barra lista esses chamados.
 */
export function BulkStatusBar({
  selectedIds,
  onClear,
  onDone,
}: {
  selectedIds: string[]
  onClear: () => void
  /** Chamado ao terminar, com os que não mudaram, para continuarem selecionados. */
  onDone: (remaining: string[]) => void
}) {
  const bulk = useBulkChangeStatus()
  const [target, setTarget] = useState<TicketStatus>('Closed')
  const [confirming, setConfirming] = useState(false)
  const [outcome, setOutcome] = useState<{ status: TicketStatus; results: BulkStatusItem[] } | null>(null)

  const count = selectedIds.length

  if (count === 0 && !outcome) {
    return null
  }

  function apply() {
    if (!confirming) {
      setConfirming(true)
      return
    }

    setConfirming(false)
    bulk.mutate(
      { ticketIds: selectedIds, status: target },
      {
        onSuccess: (results) => {
          setOutcome({ status: target, results })
          onDone(results.filter((r) => r.outcome !== 'Changed').map((r) => r.ticketId))
        },
      },
    )
  }

  const changed = outcome?.results.filter((r) => r.outcome === 'Changed') ?? []
  const failed = outcome?.results.filter((r) => r.outcome !== 'Changed') ?? []

  return (
    <div
      role="region"
      aria-label="Ações em lote"
      className="bg-card sticky bottom-4 z-10 space-y-2 rounded-xl border p-3 text-sm shadow-lg"
    >
      {count > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          <p className="font-medium">
            {count} {count === 1 ? 'chamado selecionado' : 'chamados selecionados'}
          </p>

          <Select
            aria-label="Novo status"
            className="h-8 w-auto"
            value={target}
            onChange={(event) => {
              setTarget(event.target.value as TicketStatus)
              setConfirming(false)
            }}
          >
            {bulkTargets.map((status) => (
              <option key={status} value={status}>
                {ticketStatusLabels[status]}
              </option>
            ))}
          </Select>

          <Button
            size="sm"
            variant={confirming && target === 'Cancelled' ? 'destructive' : 'default'}
            disabled={bulk.isPending}
            onClick={apply}
          >
            {bulk.isPending
              ? 'Aplicando…'
              : confirming
                ? `Confirmar: ${verbs[target]} ${count} ${count === 1 ? 'chamado' : 'chamados'}`
                : 'Aplicar'}
          </Button>

          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setConfirming(false)
              onClear()
            }}
          >
            Limpar seleção
          </Button>
        </div>
      )}

      {confirming && (
        <p className="text-muted-foreground text-xs">Cada solicitante recebe o aviso da mudança, como na tela do chamado.</p>
      )}

      {bulk.isError && (
        <p role="alert" className="text-destructive">
          {errorMessage(bulk.error, 'Não foi possível aplicar a mudança.')}
        </p>
      )}

      {outcome && (
        <div role="status" className="flex items-start justify-between gap-3">
          <div className="space-y-1">
            {changed.length > 0 && (
              <p>
                {changed.length} {changed.length === 1 ? 'chamado passou' : 'chamados passaram'} para{' '}
                {ticketStatusLabels[outcome.status]}.
              </p>
            )}
            {failed.length > 0 && (
              <>
                <p className="text-destructive">
                  {failed.length} não {failed.length === 1 ? 'mudou e continua selecionado' : 'mudaram e continuam selecionados'}:
                </p>
                <ul className="text-muted-foreground list-disc pl-5 text-xs">
                  {failed.map((item) => (
                    <li key={item.ticketId}>{describeFailure(item, outcome.status)}</li>
                  ))}
                </ul>
              </>
            )}
          </div>
          <Button
            size="sm"
            variant="ghost"
            className="size-7 p-0"
            aria-label="Fechar resumo"
            onClick={() => setOutcome(null)}
          >
            <X />
          </Button>
        </div>
      )}
    </div>
  )
}

function describeFailure(item: BulkStatusItem, target: TicketStatus): string {
  const code = item.code ?? 'Chamado'

  switch (item.outcome) {
    case 'TransitionRejected':
      return item.status
        ? `${code}: está ${ticketStatusLabels[item.status]} e não pode ir para ${ticketStatusLabels[target]}.`
        : `${code}: a mudança não é permitida.`
    case 'ReopenWindowExpired':
      return `${code}: fechado há mais tempo que o prazo de reabertura.`
    default:
      return `${code}: não encontrado.`
  }
}
