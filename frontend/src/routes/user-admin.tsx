import { useState } from 'react'
import { Copy, History, KeyRound, Search, UserPlus } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'

import { TicketStatusBadge } from '@/components/ticket-badges'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { userRoleLabels, userRoles, type UserRole } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { blockingTickets, type AccessGranted, type ManagedUser, type UserAuditAction } from '@/features/users/api'
import {
  useChangeActivation,
  useChangeRole,
  useCreateUser,
  useManagedUsers,
  useResetUserPassword,
  useUserHistory,
} from '@/features/users/queries'
import { errorMessage } from '@/lib/api'
import { formatDate, formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * Administração de usuários (README, seção 13.9). Só gestor.
 *
 * Os filtros vivem na URL, como na lista de chamados. As regras — não mexer na própria
 * conta, não tirar da equipe quem tem chamado em aberto — são do servidor; a tela só as
 * antecipa para a pessoa não descobrir pelo erro.
 */
export function UserAdmin() {
  const { role } = useSession()
  const [params, setParams] = useSearchParams()

  const search = params.get('search') ?? ''
  const roleFilter = (params.get('role') as UserRole | null) ?? undefined
  const activeParam = params.get('isActive')
  const isActive = activeParam === null ? undefined : activeParam === 'true'
  const page = Number(params.get('page') ?? '1')

  const [creating, setCreating] = useState(false)

  const users = useManagedUsers(
    { search: search || undefined, role: roleFilter, isActive, page },
    role === 'Manager',
  )

  if (role !== 'Manager') {
    return <p className="text-muted-foreground text-sm">Apenas gestores administram usuários.</p>
  }

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)

    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') {
        next.delete(key)
      } else {
        next.set(key, value)
      }
    }

    if (!('page' in changes)) {
      next.delete('page')
    }

    setParams(next, { replace: true })
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Usuários</h1>
          <p className="text-muted-foreground text-sm">
            {users.data ? `${users.data.totalCount} usuário(s)` : 'Carregando…'}
          </p>
        </div>

        {!creating && (
          <Button onClick={() => setCreating(true)}>
            <UserPlus /> Novo usuário
          </Button>
        )}
      </div>

      {creating && <NewUser onClose={() => setCreating(false)} />}

      <div className="flex flex-wrap items-end gap-3 rounded-xl border bg-card p-4 shadow-sm">
        <form
          className="relative min-w-60 flex-1"
          onSubmit={(event) => {
            event.preventDefault()
            const field = event.currentTarget.elements.namedItem('search')
            update({ search: field instanceof HTMLInputElement ? field.value : null })
          }}
        >
          <Search
            aria-hidden
            className="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2"
          />
          <Input
            name="search"
            defaultValue={search}
            key={search}
            placeholder="Buscar por nome ou e-mail"
            aria-label="Buscar usuários"
            className="pl-9"
            onBlur={(event) => update({ search: event.target.value })}
          />
        </form>

        <label className="grid gap-1.5 text-xs font-medium">
          Perfil
          <Select value={roleFilter ?? ''} onChange={(event) => update({ role: event.target.value })}>
            <option value="">Todos</option>
            {userRoles.map((value) => (
              <option key={value} value={value}>
                {userRoleLabels[value]}
              </option>
            ))}
          </Select>
        </label>

        <label className="grid gap-1.5 text-xs font-medium">
          Situação
          <Select value={activeParam ?? ''} onChange={(event) => update({ isActive: event.target.value })}>
            <option value="">Todas</option>
            <option value="true">Ativos</option>
            <option value="false">Desativados</option>
          </Select>
        </label>
      </div>

      {users.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(users.error, 'Não foi possível carregar os usuários.')}
        </p>
      )}

      {users.isPending && <p className="text-muted-foreground text-sm">Carregando usuários…</p>}

      {users.data?.items.length === 0 && (
        <div className="text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
          Nenhum usuário encontrado com esses filtros.
        </div>
      )}

      {users.data && users.data.items.length > 0 && (
        <ul className={cn('divide-y rounded-xl border bg-card shadow-sm', users.isPlaceholderData && 'opacity-60')}>
          {users.data.items.map((user) => (
            <UserRow key={user.id} user={user} />
          ))}
        </ul>
      )}

      {users.data && users.data.totalPages > 1 && (
        <div className="flex items-center justify-between text-sm">
          <p className="text-muted-foreground">
            Página {users.data.page} de {users.data.totalPages}
          </p>

          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={users.data.page <= 1}
              onClick={() => update({ page: String(users.data.page - 1) })}
            >
              Anterior
            </Button>

            <Button
              variant="outline"
              size="sm"
              disabled={!users.data.hasNextPage}
              onClick={() => update({ page: String(users.data.page + 1) })}
            >
              Próxima
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

/** Cadastro pelo gestor: convite por e-mail quando há envio configurado, senha provisória quando não. */
function NewUser({ onClose }: { onClose: () => void }) {
  const create = useCreateUser()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<UserRole>('Requester')

  if (create.data) {
    return (
      <div className="rounded-xl border bg-card p-4 shadow-sm">
        <AccessNotice access={create.data} />
        <Button variant="outline" size="sm" className="mt-4" onClick={onClose}>
          Concluir
        </Button>
      </div>
    )
  }

  return (
    <form
      className="flex flex-wrap items-end gap-3 rounded-xl border bg-card p-4 shadow-sm"
      onSubmit={(event) => {
        event.preventDefault()
        create.mutate({ name, email, role })
      }}
    >
      <label className="grid min-w-52 flex-1 gap-1.5 text-xs font-medium">
        Nome
        <Input required maxLength={200} value={name} onChange={(event) => setName(event.target.value)} />
      </label>
      <label className="grid min-w-60 flex-1 gap-1.5 text-xs font-medium">
        E-mail
        <Input type="email" required maxLength={320} value={email} onChange={(event) => setEmail(event.target.value)} />
      </label>
      <label className="grid gap-1.5 text-xs font-medium">
        Perfil
        <Select value={role} onChange={(event) => setRole(event.target.value as UserRole)}>
          {userRoles.map((value) => (
            <option key={value} value={value}>
              {userRoleLabels[value]}
            </option>
          ))}
        </Select>
      </label>
      <Button type="submit" disabled={create.isPending || !name.trim() || !email.trim()}>
        Cadastrar
      </Button>
      <Button type="button" variant="ghost" onClick={onClose}>
        Cancelar
      </Button>

      {create.isError && (
        <p role="alert" className="text-destructive w-full text-sm">
          {errorMessage(create.error, 'Não foi possível cadastrar.')}
        </p>
      )}
    </form>
  )
}

/**
 * Resultado de um cadastro ou redefinição. A senha provisória aparece aqui e em nenhum
 * outro lugar: o servidor não a guarda em claro e não a devolve de novo.
 */
function AccessNotice({ access }: { access: AccessGranted }) {
  const [copied, setCopied] = useState(false)

  if (access.invitationSent) {
    return (
      <p role="status" className="text-sm">
        Convite enviado para <strong>{access.user.email}</strong>. O link para definir a senha vale por três dias.
      </p>
    )
  }

  return (
    <div role="status" className="space-y-2 text-sm">
      <p>
        Senha provisória de <strong>{access.user.email}</strong>. Ela aparece só agora: entregue à pessoa por um canal
        seguro. No primeiro login, a troca é obrigatória.
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <code className="bg-muted rounded-md px-3 py-1.5 font-mono text-base tracking-wider">
          {access.temporaryPassword}
        </code>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => {
            void navigator.clipboard?.writeText(access.temporaryPassword ?? '').then(() => setCopied(true))
          }}
        >
          <Copy /> {copied ? 'Copiada' : 'Copiar'}
        </Button>
      </div>
    </div>
  )
}

const auditLabels: Record<UserAuditAction, string> = {
  Created: 'Conta criada',
  RoleChanged: 'Perfil alterado',
  Deactivated: 'Conta desativada',
  Reactivated: 'Conta reativada',
  PasswordChanged: 'Trocou a própria senha',
  PasswordReset: 'Senha redefinida',
  PasswordSetByLink: 'Senha definida pelo link do e-mail',
  MicrosoftAccountLinked: 'Passou a entrar com a conta Microsoft',
}

function UserHistory({ userId }: { userId: string }) {
  const history = useUserHistory(userId, true)

  if (history.isPending) {
    return <p className="text-muted-foreground text-xs">Carregando histórico…</p>
  }

  if (history.isError || !history.data) {
    return <p className="text-destructive text-xs">{errorMessage(history.error, 'Não foi possível carregar o histórico.')}</p>
  }

  if (history.data.length === 0) {
    return <p className="text-muted-foreground text-xs">Nenhum evento registrado.</p>
  }

  return (
    <ol className="border-muted space-y-1 border-l-2 pl-3 text-xs">
      {history.data.map((entry, index) => (
        <li key={index}>
          <span className="font-medium">{auditLabels[entry.action]}</span>
          {entry.action === 'RoleChanged' && entry.previousValue && entry.newValue && (
            <> de {roleLabel(entry.previousValue)} para {roleLabel(entry.newValue)}</>
          )}
          {entry.action === 'Created' && entry.newValue && <> como {roleLabel(entry.newValue)}</>}
          <span className="text-muted-foreground">
            {' '}
            · {entry.changedByName ?? 'pela própria pessoa ou pelo sistema'} · {formatDateTime(entry.createdAt)}
          </span>
        </li>
      ))}
    </ol>
  )
}

function roleLabel(value: string): string {
  return (userRoles as readonly string[]).includes(value) ? userRoleLabels[value as UserRole] : value
}

function UserRow({ user }: { user: ManagedUser }) {
  const { user: me } = useSession()
  const changeRole = useChangeRole()
  const changeActivation = useChangeActivation()
  const resetPassword = useResetUserPassword()
  const [showHistory, setShowHistory] = useState(false)

  // O servidor recusa mexer na própria conta: é o que garante que sempre sobra um
  // gestor. Desabilitar aqui só poupa a pessoa do clique que daria erro.
  const isSelf = me?.id === user.id
  const hasOpenTickets = user.openAssignedTickets > 0
  const pending = changeRole.isPending || changeActivation.isPending || resetPassword.isPending
  const error = changeRole.error ?? changeActivation.error ?? resetPassword.error
  const blocking = blockingTickets(error)

  function onRoleChange(role: UserRole) {
    if (
      role === 'Requester' &&
      !window.confirm(`Tirar ${user.name} da equipe? As sessões abertas dessa pessoa serão encerradas.`)
    ) {
      return
    }

    changeActivation.reset()
    changeRole.mutate({ userId: user.id, role })
  }

  function onToggleActive() {
    if (
      user.isActive &&
      !window.confirm(`Desativar ${user.name}? A pessoa perde o acesso e as sessões abertas são encerradas.`)
    ) {
      return
    }

    changeRole.reset()
    changeActivation.mutate({ userId: user.id, isActive: !user.isActive })
  }

  return (
    <li className="space-y-3 p-4">
      <div className="flex flex-wrap items-center gap-4">
        <div className="min-w-56 flex-1">
          <p className="flex flex-wrap items-center gap-2 font-medium">
            {user.name}
            {isSelf && <Badge variant="secondary">Você</Badge>}
            {!user.isActive && <Badge variant="outline">Desativado</Badge>}
            {user.mustChangePassword && <Badge variant="outline">Senha provisória</Badge>}
          </p>
          <p className="text-muted-foreground truncate text-sm">{user.email}</p>
          <p className="text-muted-foreground mt-1 text-xs">
            Desde {formatDate(user.createdAt)}
            {hasOpenTickets && ` · responsável por ${user.openAssignedTickets} chamado(s) em aberto`}
          </p>
        </div>

        <label className="grid gap-1.5 text-xs font-medium">
          Perfil
          <Select
            value={user.role}
            disabled={isSelf || pending}
            aria-label={`Perfil de ${user.name}`}
            onChange={(event) => onRoleChange(event.target.value as UserRole)}
          >
            {userRoles.map((value) => (
              <option key={value} value={value} disabled={value === 'Requester' && hasOpenTickets}>
                {userRoleLabels[value]}
              </option>
            ))}
          </Select>
        </label>

        <Button
          variant={user.isActive ? 'outline' : 'default'}
          size="sm"
          className="self-end"
          disabled={isSelf || pending || (user.isActive && hasOpenTickets)}
          title={user.isActive && hasOpenTickets ? 'Reatribua os chamados em aberto antes de desativar.' : undefined}
          onClick={onToggleActive}
        >
          {user.isActive ? 'Desativar' : 'Reativar'}
        </Button>

        <Button
          variant="ghost"
          size="sm"
          className="self-end"
          disabled={isSelf || pending}
          onClick={() => {
            if (window.confirm(`Redefinir a senha de ${user.name}? A senha atual deixa de valer e as sessões abertas caem.`)) {
              resetPassword.mutate(user.id)
            }
          }}
        >
          <KeyRound /> Redefinir senha
        </Button>

        <Button
          variant="ghost"
          size="sm"
          className="self-end"
          aria-expanded={showHistory}
          onClick={() => setShowHistory((value) => !value)}
        >
          <History /> Histórico
        </Button>
      </div>

      {resetPassword.data && <AccessNotice access={resetPassword.data} />}

      {showHistory && <UserHistory userId={user.id} />}

      {error && (
        <div role="alert" className="text-destructive space-y-2 text-sm">
          <p>{errorMessage(error)}</p>

          {blocking.length > 0 && (
            <ul className="space-y-1">
              {blocking.map((ticket) => (
                <li key={ticket.id} className="flex flex-wrap items-center gap-2">
                  <Link to={`/chamados/${ticket.id}`} className="font-medium underline underline-offset-2">
                    {ticket.code}
                  </Link>
                  <span className="text-foreground">{ticket.title}</span>
                  <TicketStatusBadge status={ticket.status} />
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </li>
  )
}
