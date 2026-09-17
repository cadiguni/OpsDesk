import { useState, type ReactNode } from 'react'
import { CheckCheck, LockKeyhole, MessageSquare, Send } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { userRoleLabels, type UserRole } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { useAddComment, useComments } from '@/features/tickets/queries'
import type { TicketDetail } from '@/features/tickets/types'
import { errorMessage } from '@/lib/api'
import { formatDateTime, initials } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * Conversa do chamado: a descrição de abertura e os comentários, na mesma linha do tempo.
 *
 * A descrição não é um campo à parte na leitura de quem atende — é a primeira mensagem do
 * solicitante, e separá-la num cartão acima obrigava a ler o chamado em dois lugares e
 * reconstruir a ordem mentalmente. Aqui ela abre a conversa, marcada como abertura.
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

  async function submit(closeTicket = false) {
    if (addComment.isPending || content.trim().length === 0) {
      return
    }

    try {
      await addComment.mutateAsync({ content, isInternal, closeTicket })
      setContent('')
      setIsInternal(false)
    } catch {
      // A mensagem aparece abaixo; mantenha o rascunho para uma nova tentativa.
    }
  }

  return (
    <div className="space-y-4">
      <ol className="space-y-3">
        <li>
          <Message
            authorName={ticket.requesterName}
            authorRole="Requester"
            createdAt={ticket.createdAt}
            badge={<Badge variant="secondary">Abertura do chamado</Badge>}
          >
            {ticket.description}
          </Message>
        </li>

        {comments.data?.map((comment) => (
          <li key={comment.id}>
            <Message
              authorName={comment.authorName}
              authorRole={comment.authorRole}
              createdAt={comment.createdAt}
              internal={comment.isInternal}
              badge={
                comment.isInternal && (
                  <Badge variant="outline" className="border-sla-due-soon/40 text-sla-due-soon">
                    <LockKeyhole className="size-3" /> Interno — não visível ao solicitante
                  </Badge>
                )
              }
            >
              {comment.content}
            </Message>
          </li>
        ))}
      </ol>

      {comments.isPending && <p className="text-muted-foreground text-sm">Carregando respostas…</p>}

      {comments.isError && (
        <p role="alert" className="text-destructive text-sm">
          Não foi possível carregar a conversa.
        </p>
      )}

      {comments.data?.length === 0 && (
        <p className="text-muted-foreground text-sm">Nenhuma resposta ainda.</p>
      )}

      {closed ? (
        <p className="text-muted-foreground rounded-lg border border-dashed p-3 text-sm">
          Este chamado está encerrado e não aceita novos comentários.
        </p>
      ) : (
        <div
          className={cn(
            'overflow-hidden rounded-xl border bg-card',
            isInternal && 'border-sla-due-soon/50 bg-sla-due-soon/5',
          )}
        >
          <div
            className="flex flex-wrap gap-2 border-b bg-muted/40 p-3"
            role="group"
            aria-label="Visibilidade do comentário"
          >
            <Button
              size="sm"
              variant={isInternal ? 'ghost' : 'secondary'}
              aria-pressed={!isInternal}
              disabled={addComment.isPending}
              onClick={() => setIsInternal(false)}
            >
              <MessageSquare className="size-4" /> Resposta pública
            </Button>

            {isStaff && (
              <Button
                size="sm"
                variant={isInternal ? 'secondary' : 'ghost'}
                aria-pressed={isInternal}
                disabled={addComment.isPending}
                onClick={() => setIsInternal(true)}
              >
                <LockKeyhole className="size-4" /> Nota interna
              </Button>
            )}
          </div>

          <p className="text-muted-foreground px-4 pt-3 text-xs">
            {isInternal
              ? 'Visível apenas para técnicos e gestores.'
              : 'Esta resposta ficará visível para o solicitante.'}
          </p>

          <Textarea
            value={content}
            onChange={(event) => setContent(event.target.value)}
            placeholder={isInternal ? 'Observação interna da equipe…' : 'Escreva uma resposta…'}
            rows={6}
            maxLength={10000}
            disabled={addComment.isPending}
            aria-label="Novo comentário"
            className="rounded-none border-0 bg-transparent p-4 shadow-none"
          />

          <div className="flex flex-wrap items-center justify-end gap-2 border-t bg-muted/30 p-3">
            <Button
              onClick={() => void submit()}
              disabled={addComment.isPending || content.trim().length === 0}
            >
              <Send className="size-4" /> {addComment.isPending ? 'Enviando…' : 'Enviar'}
            </Button>

            {ticket.allowedNextStatuses.includes('Closed') && (
              <Button
                variant="outline"
                onClick={() => void submit(true)}
                disabled={addComment.isPending || content.trim().length === 0}
              >
                <CheckCheck className="size-4" /> Enviar e fechar
              </Button>
            )}
          </div>

          {addComment.isError && (
            <p role="alert" className="text-destructive p-3 text-sm">
              {errorMessage(addComment.error, 'Não foi possível enviar o comentário.')}
            </p>
          )}
        </div>
      )}
    </div>
  )
}

/**
 * Uma entrada da conversa.
 *
 * O avatar carrega a cor do perfil: numa conversa longa é ele que diz, de relance, se a
 * última palavra foi da equipe ou do solicitante.
 */
function Message({
  authorName,
  authorRole,
  createdAt,
  internal = false,
  badge,
  children,
}: {
  authorName: string
  authorRole: UserRole
  createdAt: string
  internal?: boolean
  badge?: ReactNode
  children: string
}) {
  return (
    <article
      className={cn(
        'flex gap-3 rounded-xl border bg-background/40 p-4 text-sm leading-relaxed',
        internal && 'border-sla-due-soon/40 bg-sla-due-soon/5',
      )}
    >
      <span
        aria-hidden
        className={cn(
          'grid size-9 shrink-0 place-items-center rounded-full text-xs font-semibold',
          authorRole === 'Requester'
            ? 'bg-muted text-muted-foreground'
            : 'bg-primary/10 text-primary',
        )}
      >
        {initials(authorName)}
      </span>

      <div className="min-w-0 flex-1">
        <div className="mb-1.5 flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="font-medium">{authorName}</span>
          <span className="text-muted-foreground text-xs">{userRoleLabels[authorRole]}</span>
          <span className="text-muted-foreground text-xs">·</span>
          <span className="text-muted-foreground text-xs">{formatDateTime(createdAt)}</span>
          {badge}
        </div>

        {/* whitespace-pre-wrap preserva as quebras de linha do que foi digitado, que
            costuma ser log colado. */}
        <p className="whitespace-pre-wrap break-words">{children}</p>
      </div>
    </article>
  )
}
