import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { Select } from '@/components/ui/select'
import { ticketStatusLabels } from '@/domain/enums'
import type { TicketStatus } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { useAssign, useChangeStatus, useStaff } from '@/features/tickets/queries'
import type { TicketDetail } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'

/**
 * Ações sobre o chamado: mover o status e definir o responsável.
 *
 * As opções de status vêm de `allowedNextStatuses`, calculado pela API a partir da máquina
 * de estados e do perfil de quem pediu. A interface não replica o grafo — se replicasse,
 * seriam duas fontes de verdade, e a daqui envelheceria primeiro.
 */
export function TicketActions({ ticket }: { ticket: TicketDetail }) {
  const { isStaff, role, user } = useSession()
  const changeStatus = useChangeStatus(ticket.id)
  const assign = useAssign(ticket.id)
  const staff = useStaff(isStaff)

  const [nextStatus, setNextStatus] = useState<TicketStatus | ''>('')

  const isManager = role === 'Manager'
  const isMine = ticket.assignedTechnicianId === user?.id

  return (
    <div className="space-y-4">
      {ticket.allowedNextStatuses.length > 0 && (
        <div className="space-y-2">
          <p className="text-xs font-medium">Mudar status</p>

          <div className="flex flex-wrap gap-2">
            <Select
              value={nextStatus}
              onChange={(event) => setNextStatus(event.target.value as TicketStatus)}
              aria-label="Novo status"
              className="max-w-52"
            >
              <option value="">Selecione</option>
              {ticket.allowedNextStatuses.map((status) => (
                <option key={status} value={status}>
                  {ticketStatusLabels[status]}
                </option>
              ))}
            </Select>

            <Button
              size="sm"
              disabled={nextStatus === '' || changeStatus.isPending}
              onClick={() => {
                if (nextStatus !== '') {
                  changeStatus.mutate(nextStatus, { onSuccess: () => setNextStatus('') })
                }
              }}
            >
              {changeStatus.isPending ? 'Aplicando…' : 'Aplicar'}
            </Button>
          </div>

          {changeStatus.isError && (
            <p role="alert" className="text-destructive text-xs">
              {errorMessage(changeStatus.error, 'Não foi possível mudar o status.')}
            </p>
          )}
        </div>
      )}

      {isStaff && (
        <div className="space-y-2">
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
            <div className="flex flex-wrap gap-2">
              {!isMine && (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={assign.isPending || ticket.assignedTechnicianId !== null}
                  onClick={() => assign.mutate(user?.id ?? null)}
                >
                  {assign.isPending ? 'Assumindo…' : 'Assumir chamado'}
                </Button>
              )}

              {isMine && (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={assign.isPending}
                  onClick={() => assign.mutate(null)}
                >
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
