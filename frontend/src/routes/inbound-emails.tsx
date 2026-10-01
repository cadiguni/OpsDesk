import { Search } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session-context'
import type { InboundEmailItem, InboundEmailStatus, SpamReviewAction, SpamRule } from '@/features/inbound/api'
import {
  useDeleteInbound,
  useInboundDetail,
  useInboundList,
  useReprocessInbound,
  useReviewInbound,
  useSetSpamRuleActive,
  useSpamRules,
} from '@/features/inbound/queries'
import { errorMessage } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

const tabs: { value: InboundEmailStatus | 'rules'; label: string }[] = [
  { value: 'Spam', label: 'SPAM' },
  { value: 'Failed', label: 'Com falha' },
  { value: 'Processed', label: 'Viraram chamado' },
  { value: 'Duplicate', label: 'Duplicados' },
  { value: 'rules', label: 'Regras' },
]

/**
 * E-mails recebidos pelas caixas monitoradas, e a caixa de SPAM (docs/integracao-email.md,
 * seção 5). Técnicos e gestores.
 *
 * O corpo da mensagem é mostrado só como texto: o sistema nem guarda o HTML de e-mail
 * recebido, que é por onde chegaria o conteúdo mal-intencionado.
 */
export function InboundEmails() {
  const { isStaff } = useSession()
  const [params, setParams] = useSearchParams()
  const tab = (params.get('aba') as InboundEmailStatus | 'rules' | null) ?? 'Spam'
  const selected = params.get('id')
  const search = params.get('search') ?? ''

  if (!isStaff) {
    return <p className="text-muted-foreground text-sm">Apenas a equipe vê os e-mails recebidos.</p>
  }

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)

    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }

    setParams(next, { replace: true })
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">E-mails recebidos</h1>
        <p className="text-muted-foreground text-sm">
          O que chegou às caixas monitoradas. Mensagem legítima vira chamado sozinha; o que parece spam espera aqui
          pela revisão da equipe.
        </p>
      </div>

      <nav aria-label="Situação" className="flex flex-wrap gap-1 border-b">
        {tabs.map((item) => (
          <button
            key={item.value}
            type="button"
            aria-pressed={tab === item.value}
            onClick={() => update({ aba: item.value, id: null, page: null })}
            className={cn(
              '-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors',
              tab === item.value
                ? 'border-primary text-primary'
                : 'text-muted-foreground hover:text-foreground border-transparent',
            )}
          >
            {item.label}
          </button>
        ))}
      </nav>

      {tab === 'rules' ? (
        <RulesList />
      ) : (
        <>
          <form
            className="relative max-w-md"
            onSubmit={(event) => {
              event.preventDefault()
              const field = event.currentTarget.elements.namedItem('search')
              update({ search: field instanceof HTMLInputElement ? field.value : null })
            }}
          >
            <Search aria-hidden className="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" />
            <Input
              name="search"
              key={search}
              defaultValue={search}
              aria-label="Buscar e-mails"
              placeholder="Remetente, assunto ou motivo"
              className="pl-9"
            />
          </form>

          <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
            <MessageList status={tab} search={search} selected={selected} onSelect={(id) => update({ id })} />
            {selected ? <MessageDetail id={selected} onDone={() => update({ id: null })} /> : null}
          </div>
        </>
      )}
    </div>
  )
}

function MessageList({
  status,
  search,
  selected,
  onSelect,
}: {
  status: InboundEmailStatus
  search: string
  selected: string | null
  onSelect: (id: string) => void
}) {
  const list = useInboundList({ status, search: search || undefined })

  if (list.isPending) {
    return <p className="text-muted-foreground text-sm">Carregando…</p>
  }

  if (list.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(list.error, 'Não foi possível carregar os e-mails.')}
      </p>
    )
  }

  if (list.data.items.length === 0) {
    return (
      <div className="text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
        Nenhuma mensagem aqui.
      </div>
    )
  }

  return (
    <ul className="divide-y self-start rounded-xl border bg-card shadow-sm">
      {list.data.items.map((item) => (
        <li key={item.id}>
          <button
            type="button"
            aria-current={selected === item.id}
            onClick={() => onSelect(item.id)}
            className={cn(
              'hover:bg-accent/40 w-full p-4 text-left text-sm transition-colors',
              selected === item.id && 'bg-primary/5',
            )}
          >
            <span className="flex items-center justify-between gap-2">
              <span className="truncate font-medium">{item.fromName || item.fromAddress}</span>
              <span className="text-muted-foreground shrink-0 text-xs">{formatDateTime(item.receivedAt)}</span>
            </span>
            <span className="block truncate">{item.subject || '(sem assunto)'}</span>
            <span className="text-muted-foreground block truncate text-xs">{summary(item)}</span>
          </button>
        </li>
      ))}
    </ul>
  )
}

function summary(item: InboundEmailItem): string {
  switch (item.status) {
    case 'Spam':
      return item.spamReason ?? 'Retido'
    case 'Failed':
      return item.processingError ?? 'Falhou'
    case 'Processed':
      return item.ticketCode ? `Chamado ${item.ticketCode}` : 'Processado'
    default:
      return item.fromAddress
  }
}

const reviewActions: { action: SpamReviewAction; label: string; variant: 'default' | 'outline' }[] = [
  { action: 'NotSpam', label: 'Não é spam', variant: 'default' },
  { action: 'NotSpamAndAllowSender', label: 'Não é spam e liberar remetente', variant: 'outline' },
  { action: 'ConfirmAndBlockSender', label: 'Confirmar spam e bloquear remetente', variant: 'outline' },
  { action: 'BlockDomain', label: 'Bloquear o domínio', variant: 'outline' },
]

function MessageDetail({ id, onDone }: { id: string; onDone: () => void }) {
  const { role } = useSession()
  const detail = useInboundDetail(id)
  const review = useReviewInbound()
  const reprocess = useReprocessInbound()
  const remove = useDeleteInbound()
  const error = review.error ?? reprocess.error ?? remove.error

  if (detail.isPending) {
    return <p className="text-muted-foreground text-sm">Carregando…</p>
  }

  if (detail.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(detail.error, 'Não foi possível abrir a mensagem.')}
      </p>
    )
  }

  const { email, bodyText, reviewedByName, reviewedAt } = detail.data
  const busy = review.isPending || reprocess.isPending || remove.isPending

  return (
    <article aria-label="Mensagem" className="space-y-4 rounded-xl border bg-card p-5 shadow-sm">
      <header className="space-y-1">
        <h2 className="text-lg font-semibold">{email.subject || '(sem assunto)'}</h2>
        <p className="text-sm">
          {email.fromName && <span className="font-medium">{email.fromName} </span>}
          <span className="text-muted-foreground">&lt;{email.fromAddress}&gt;</span>
        </p>
        <p className="text-muted-foreground text-xs">
          {email.accountName} · {formatDateTime(email.receivedAt)}
        </p>
      </header>

      {email.status === 'Spam' && (
        <p className="text-sm">
          <Badge variant="outline">Retido</Badge> {email.spamReason}
          {email.spamScore > 0 && <span className="text-muted-foreground"> · pontuação {email.spamScore}</span>}
        </p>
      )}

      {email.status === 'Failed' && (
        <p role="alert" className="text-destructive text-sm">
          Falhou {email.attempts} vez(es): {email.processingError}
        </p>
      )}

      {email.ticketId && (
        <p className="text-sm">
          Virou o chamado{' '}
          <Link to={`/chamados/${email.ticketId}`} className="text-primary font-medium hover:underline">
            {email.ticketCode}
          </Link>
          .
        </p>
      )}

      {reviewedByName && reviewedAt && (
        <p className="text-muted-foreground text-xs">
          Revisado por {reviewedByName} em {formatDateTime(reviewedAt)}.
        </p>
      )}

      {/* Texto puro, num bloco que não interpreta nada: é conteúdo de fora. */}
      <pre className="bg-muted/40 max-h-96 overflow-auto rounded-lg p-4 text-sm whitespace-pre-wrap break-words font-sans">
        {bodyText || '(mensagem sem texto)'}
      </pre>

      <div className="flex flex-wrap gap-2">
        {email.status === 'Spam' &&
          reviewActions.map((item) => (
            <Button
              key={item.action}
              size="sm"
              variant={item.variant}
              disabled={busy}
              onClick={() => review.mutate({ id, action: item.action })}
            >
              {item.label}
            </Button>
          ))}

        {email.status === 'Failed' && (
          <Button size="sm" disabled={busy} onClick={() => reprocess.mutate(id)}>
            Processar de novo
          </Button>
        )}

        {role === 'Manager' && (
          <Button
            size="sm"
            variant="ghost"
            disabled={busy}
            onClick={() => {
              if (window.confirm('Excluir esta mensagem de vez?')) {
                remove.mutate(id, { onSuccess: onDone })
              }
            }}
          >
            Excluir
          </Button>
        )}
      </div>

      {error && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(error)}
        </p>
      )}
    </article>
  )
}

const ruleTypeLabels: Record<SpamRule['type'], string> = {
  SenderAddress: 'Remetente',
  SenderDomain: 'Domínio',
  SubjectContains: 'Assunto contém',
  BodyContains: 'Corpo contém',
}

function RulesList() {
  const rules = useSpamRules(true)
  const toggle = useSetSpamRuleActive()

  if (rules.isPending) {
    return <p className="text-muted-foreground text-sm">Carregando…</p>
  }

  if (rules.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(rules.error, 'Não foi possível carregar as regras.')}
      </p>
    )
  }

  if (rules.data.length === 0) {
    return (
      <div className="text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
        Nenhuma regra ainda. Elas nascem das ações na caixa de SPAM: liberar remetente, confirmar spam, bloquear domínio.
      </div>
    )
  }

  return (
    <div className="overflow-x-auto rounded-lg border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50 text-muted-foreground text-left text-xs">
          <tr>
            <th scope="col" className="px-4 py-2 font-medium">Regra</th>
            <th scope="col" className="px-4 py-2 font-medium">Ação</th>
            <th scope="col" className="px-4 py-2 font-medium">Criada por</th>
            <th scope="col" className="px-4 py-2 font-medium"><span className="sr-only">Ligada</span></th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {rules.data.map((rule) => (
            <tr key={rule.id} className={cn(!rule.isActive && 'opacity-60')}>
              <td className="px-4 py-2">
                <span className="text-muted-foreground">{ruleTypeLabels[rule.type]}</span> {rule.pattern}
              </td>
              <td className="px-4 py-2">{rule.action === 'Allow' ? 'Liberar' : 'Bloquear'}</td>
              <td className="text-muted-foreground px-4 py-2">
                {rule.createdByName ?? '—'} · {formatDateTime(rule.createdAt)}
              </td>
              <td className="px-4 py-2 text-right">
                <Button
                  size="sm"
                  variant="ghost"
                  disabled={toggle.isPending}
                  onClick={() => toggle.mutate({ id: rule.id, isActive: !rule.isActive })}
                >
                  {rule.isActive ? 'Desligar' : 'Religar'}
                </Button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
