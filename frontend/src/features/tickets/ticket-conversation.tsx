import { useState } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { userRoleLabels } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { useAddComment, useComments } from '@/features/tickets/queries'
import type { TicketDetail } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * Comentários do chamado.
 *
 * O comentário interno recebe marca visual clara. Não é enfeite: um técnico que escreve
 * uma observação da equipe achando que o solicitante não vai ler, e erra o alvo, causa um
 * problema que nenhum código conserta depois. A lista só traz o que a API deixou passar —
 * o solicitante nunca recebe comentário interno, mas quem é da equipe precisa ver na hora
 * qual comentário está restrito.
 */
export function TicketConversation({ ticket }: { ticket: TicketDetail }) {
  const { isStaff } = useSession()
  const comments = useComments(ticket.id)
  const addComment = useAddComment(ticket.id)

  const [content, setContent] = useState('')
  const [isInternal, setIsInternal] = useState(false)

  const closed = ticket.status === 'Closed' || ticket.status === 'Cancelled'

  async function submit() {
    if (content.trim().length === 0) {
      return
    }

    await addComment.mutateAsync({ content, isInternal })

    setContent('')
    setIsInternal(false)
  }

  return (
    <div className="space-y-4">
      {comments.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}

      {comments.data?.length === 0 && (
        <p className="text-muted-foreground text-sm">Nenhum comentário ainda.</p>
      )}

      <ul className="space-y-3">
        {comments.data?.map((comment) => (
          <li
            key={comment.id}
            className={cn(
              'rounded-lg border p-3 text-sm',
              comment.isInternal && 'border-sla-due-soon/40 bg-sla-due-soon/5',
            )}
          >
            <div className="mb-1.5 flex flex-wrap items-center gap-2">
              <span className="font-medium">{comment.authorName}</span>
              <span className="text-muted-foreground text-xs">
                {userRoleLabels[comment.authorRole]}
              </span>
              <span className="text-muted-foreground text-xs">
                {formatDateTime(comment.createdAt)}
              </span>

              {comment.isInternal && (
                <Badge variant="outline" className="border-sla-due-soon/40 text-sla-due-soon">
                  Interno — não visível ao solicitante
                </Badge>
              )}
            </div>

            <p className="whitespace-pre-wrap">{comment.content}</p>
          </li>
        ))}
      </ul>

      {closed ? (
        <p className="text-muted-foreground rounded-lg border border-dashed p-3 text-sm">
          Este chamado está encerrado e não aceita novos comentários.
        </p>
      ) : (
        <div className="space-y-2">
          <Textarea
            value={content}
            onChange={(event) => setContent(event.target.value)}
            placeholder={
              isInternal ? 'Observação interna da equipe…' : 'Escreva uma resposta…'
            }
            rows={4}
            aria-label="Novo comentário"
            className={cn(isInternal && 'border-sla-due-soon/40')}
          />

          <div className="flex flex-wrap items-center gap-3">
            <Button
              onClick={() => void submit()}
              disabled={addComment.isPending || content.trim().length === 0}
            >
              {addComment.isPending ? 'Enviando…' : 'Comentar'}
            </Button>

            {isStaff && (
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={isInternal}
                  onChange={(event) => setIsInternal(event.target.checked)}
                />
                Comentário interno
              </label>
            )}
          </div>

          {addComment.isError && (
            <p role="alert" className="text-destructive text-sm">
              {errorMessage(addComment.error, 'Não foi possível enviar o comentário.')}
            </p>
          )}
        </div>
      )}
    </div>
  )
}
