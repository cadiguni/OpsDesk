import { useState } from 'react'
import {
  Ban,
  CircleCheck,
  ClipboardList,
  Lock,
  PlayCircle,
  RotateCcw,
  UserRoundCheck,
} from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Select } from '@/components/ui/select'
import type { TicketStatus } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { useAssign, useChangeStatus, useStaff } from '@/features/tickets/queries'
import type { TicketDetail } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'
import { cn } from '@/lib/utils'

/**
 * Ações sobre o chamado: mover o status e definir o responsável.
 *
 * As opções vêm de `allowedNextStatuses`, calculado pela API a partir da máquina de
 * estados e do perfil de quem pediu. A interface não replica o grafo — se replicasse,
 * seriam duas fontes de verdade, e a daqui envelheceria primeiro.
 *
 * Cada transição virou um botão com o nome da ação, não um select com o nome do estado.
 * "Selecione → Em triagem → Aplicar" são três passos que escondem o que a pessoa quer
 * fazer; "Enviar para triagem" é um. Vale sobretudo para a triagem, que é o passo que a
 * equipe pulava por não estar visível em lugar nenhum.
 */

type StatusAction = {
  label: string
  icon: typeof PlayCircle
  /** Ações que encerram ou descartam o chamado pedem uma segunda confirmação. */
  confirm?: string
  destructive?: boolean
}

const statusActions: Record<TicketStatus, StatusAction> = {
  Open: { label: 'Reabrir', icon: RotateCcw },
  Triage: { label: 'Enviar para triagem', icon: ClipboardList },
  InProgress: { label: 'Iniciar atendimento', icon: PlayCircle },
  WaitingOnRequester: { label: 'Aguardar solicitante', icon: UserRoundCheck },
  Resolved: { label: 'Marcar como resolvido', icon: CircleCheck },
  Closed: { label: 'Fechar chamado', icon: Lock, confirm: 'Confirmar fechamento' },
  Cancelled: {
    label: 'Cancelar chamado',
    icon: Ban,
    confirm: 'Confirmar cancelamento',
    destructive: true,
  },
}

export function TicketActions({ ticket }: { ticket: TicketDetail }) {
  const { isStaff, role, user } = useSession()
  const changeStatus = useChangeStatus(ticket.id)
  const assign = useAssign(ticket.id)
  const staff = useStaff(isStaff)

  // Qual ação está aguardando o segundo clique. Um clique fora, ou em outra ação,
  // desarma — o estado de confirmação não deve sobreviver à mudança de intenção.
  const [confirming, setConfirming] = useState<TicketStatus | null>(null)

  const isManager = role === 'Manager'
  const isMine = ticket.assignedTechnicianId === user?.id

  function activate(status: TicketStatus) {
    const action = statusActions[status]

    if (action.confirm && confirming !== status) {
      setConfirming(status)
      return
    }

    changeStatus.mutate(status, { onSettled: () => setConfirming(null) })
  }

  return (
    <div className="space-y-4">
      {ticket.allowedNextStatuses.length > 0 && (
        <div className="space-y-2">
          <p className="text-xs font-medium">Ações</p>

          <div className="grid gap-2">
            {ticket.allowedNextStatuses.map((status) => {
              const action = statusActions[status]
              const Icon = action.icon
              const awaiting = confirming === status

              return (
                <Button
                  key={status}
                  size="sm"
                  variant={awaiting ? 'default' : 'outline'}
                  disabled={changeStatus.isPending}
                  onClick={() => activate(status)}
                  onBlur={() => awaiting && setConfirming(null)}
                  className={cn(
                    'justify-start',
                    action.destructive &&
                      !awaiting &&
                      'text-destructive hover:bg-destructive/10 hover:text-destructive',
                    awaiting && action.destructive && 'bg-destructive hover:bg-destructive/90',
                  )}
                >
                  <Icon />
                  {awaiting ? action.confirm : action.label}
                </Button>
              )
            })}
          </div>

          {changeStatus.isError && (
            <p role="alert" className="text-destructive text-xs">
              {errorMessage(changeStatus.error, 'Não foi possível mudar o status.')}
            </p>
          )}
        </div>
      )}

      {isStaff && (
        <div className="space-y-2 border-t pt-4">
          <p className="text-xs font-medium">Responsável</p>

          {isManager ? (
            <Select
              value={ticket.assignedTechnicianId ?? ''}
              onChange={(event) => assign.mutate(event.target.value || null)}
              disabled={assign.isPending || staff.isPending}
              aria-label="Técnico responsável"
            >
              <option value="">Sem responsável</option>
              {staff.data?.map((person) => (
                <option key={person.id} value={person.id}>
                  {person.name}
                </option>
              ))}
            </Select>
          ) : (
            // Técnico assume para si ou devolve o próprio chamado; não movimenta o de
            // outra pessoa. A API recusa de todo jeito, e a interface não oferece.
            <div className="grid gap-2">
              {!isMine && (
                <Button
                  size="sm"
                  variant="outline"
                  className="justify-start"
                  disabled={assign.isPending || ticket.assignedTechnicianId !== null}
                  onClick={() => assign.mutate(user?.id ?? null)}
                >
                  <UserRoundCheck />
                  {assign.isPending ? 'Assumindo…' : 'Assumir chamado'}
                </Button>
              )}

              {isMine && (
                <Button
                  size="sm"
                  variant="outline"
                  className="justify-start"
                  disabled={assign.isPending}
                  onClick={() => assign.mutate(null)}
                >
                  <RotateCcw />
                  Devolver para a fila
                </Button>
              )}
            </div>
          )}

          {assign.isError && (
            <p role="alert" className="text-destructive text-xs">
              {errorMessage(assign.error, 'Não foi possível alterar o responsável.')}
            </p>
          )}
        </div>
      )}
    </div>
  )
}
