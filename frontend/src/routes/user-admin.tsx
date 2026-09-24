import { Search } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'

import { TicketStatusBadge } from '@/components/ticket-badges'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { userRoleLabels, userRoles, type UserRole } from '@/domain/enums'
import { useSession } from '@/features/auth/session-context'
import { blockingTickets, type ManagedUser } from '@/features/users/api'
import { useChangeActivation, useChangeRole, useManagedUsers } from '@/features/users/queries'
import { errorMessage } from '@/lib/api'
import { formatDate } from '@/lib/format'
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
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Usuários</h1>
        <p className="text-muted-foreground text-sm">
          {users.data ? `${users.data.totalCount} usuário(s)` : 'Carregando…'}
        </p>
      </div>

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

function UserRow({ user }: { user: ManagedUser }) {
  const { user: me } = useSession()
  const changeRole = useChangeRole()
  const changeActivation = useChangeActivation()

  // O servidor recusa mexer na própria conta: é o que garante que sempre sobra um
  // gestor. Desabilitar aqui só poupa a pessoa do clique que daria erro.
  const isSelf = me?.id === user.id
  const hasOpenTickets = user.openAssignedTickets > 0
  const pending = changeRole.isPending || changeActivation.isPending
  const error = changeRole.error ?? changeActivation.error
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
      </div>

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
