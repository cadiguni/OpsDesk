import { useState } from 'react'
import { Plus } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { useSession } from '@/features/auth/session-context'
import type { ManagedJobTitle } from '@/features/job-titles/api'
import {
  useChangeJobTitleActivation,
  useCreateJobTitle,
  useManagedJobTitles,
  useRenameJobTitle,
} from '@/features/job-titles/queries'
import { errorMessage } from '@/lib/api'
import { cn } from '@/lib/utils'

/**
 * Catálogo de cargos. Só administrador.
 *
 * Cargo é informativo: o que a pessoa pode fazer no portal é o perfil, escolhido na tela
 * de usuários. Não há exclusão, só desativação — quem já tem o cargo continua com ele.
 */
export function JobTitleAdmin() {
  const { role } = useSession()
  const [params, setParams] = useSearchParams()
  const [creating, setCreating] = useState(false)

  const activeParam = params.get('isActive')
  const isActive = activeParam === null ? undefined : activeParam === 'true'
  const page = Number(params.get('page') ?? '1')

  const jobTitles = useManagedJobTitles({ isActive, page }, role === 'Manager')

  if (role !== 'Manager') {
    return <p className="text-muted-foreground text-sm">Apenas administradores gerenciam cargos.</p>
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
          <h1 className="text-xl font-semibold tracking-tight">Cargos</h1>
          <p className="text-muted-foreground text-sm">
            Só informativo. O que cada pessoa pode fazer no portal é o perfil, definido em Usuários.
          </p>
        </div>

        {!creating && (
          <Button onClick={() => setCreating(true)}>
            <Plus /> Novo cargo
          </Button>
        )}
      </div>

      {creating && <CreateJobTitle onDone={() => setCreating(false)} />}

      <div className="flex flex-wrap items-end gap-3 rounded-xl border bg-card p-4 shadow-sm">
        <label className="grid gap-1.5 text-xs font-medium">
          Situação
          <Select value={activeParam ?? ''} onChange={(event) => update({ isActive: event.target.value })}>
            <option value="">Todos</option>
            <option value="true">Ativos</option>
            <option value="false">Desativados</option>
          </Select>
        </label>
      </div>

      {jobTitles.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(jobTitles.error, 'Não foi possível carregar os cargos.')}
        </p>
      )}

      {jobTitles.isPending && <p className="text-muted-foreground text-sm">Carregando cargos…</p>}

      {jobTitles.data?.items.length === 0 && (
        <div className="text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
          Nenhum cargo encontrado.
        </div>
      )}

      {jobTitles.data && jobTitles.data.items.length > 0 && (
        <ul className={cn('divide-y rounded-xl border bg-card shadow-sm', jobTitles.isPlaceholderData && 'opacity-60')}>
          {jobTitles.data.items.map((jobTitle) => (
            <JobTitleRow key={jobTitle.id} jobTitle={jobTitle} />
          ))}
        </ul>
      )}

      {jobTitles.data && jobTitles.data.totalPages > 1 && (
        <div className="flex items-center justify-between text-sm">
          <p className="text-muted-foreground">
            Página {jobTitles.data.page} de {jobTitles.data.totalPages}
          </p>

          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={jobTitles.data.page <= 1}
              onClick={() => update({ page: String(jobTitles.data.page - 1) })}
            >
              Anterior
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={!jobTitles.data.hasNextPage}
              onClick={() => update({ page: String(jobTitles.data.page + 1) })}
            >
              Próxima
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

function CreateJobTitle({ onDone }: { onDone: () => void }) {
  const create = useCreateJobTitle()

  return (
    <div className="rounded-xl border bg-card p-4 shadow-sm">
      <NameForm
        submitLabel="Criar cargo"
        pending={create.isPending}
        error={create.error}
        onCancel={onDone}
        onSubmit={(name) => create.mutate(name, { onSuccess: onDone })}
      />
    </div>
  )
}

function JobTitleRow({ jobTitle }: { jobTitle: ManagedJobTitle }) {
  const [editing, setEditing] = useState(false)
  const rename = useRenameJobTitle()
  const activation = useChangeJobTitleActivation()

  if (editing) {
    return (
      <li className="p-4">
        <NameForm
          initial={jobTitle.name}
          submitLabel="Salvar"
          pending={rename.isPending}
          error={rename.error}
          onCancel={() => {
            rename.reset()
            setEditing(false)
          }}
          onSubmit={(name) => rename.mutate({ id: jobTitle.id, name }, { onSuccess: () => setEditing(false) })}
        />
      </li>
    )
  }

  return (
    <li className="space-y-2 p-4">
      <div className="flex flex-wrap items-center gap-4">
        <div className="min-w-56 flex-1">
          <p className="flex flex-wrap items-center gap-2 font-medium">
            {jobTitle.name}
            {!jobTitle.isActive && <Badge variant="outline">Desativado</Badge>}
          </p>
          <p className="text-muted-foreground mt-1 text-xs">{jobTitle.users} pessoa(s)</p>
        </div>

        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => setEditing(true)}>
            Renomear
          </Button>
          <Button
            variant={jobTitle.isActive ? 'outline' : 'default'}
            size="sm"
            disabled={activation.isPending}
            onClick={() => activation.mutate({ id: jobTitle.id, isActive: !jobTitle.isActive })}
          >
            {jobTitle.isActive ? 'Desativar' : 'Reativar'}
          </Button>
        </div>
      </div>

      {activation.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(activation.error)}
        </p>
      )}
    </li>
  )
}

function NameForm({
  initial = '',
  submitLabel,
  pending,
  error,
  onSubmit,
  onCancel,
}: {
  initial?: string
  submitLabel: string
  pending: boolean
  error: unknown
  onSubmit: (name: string) => void
  onCancel: () => void
}) {
  const [name, setName] = useState(initial)

  return (
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit(name.trim())
      }}
    >
      <label className="grid min-w-60 flex-1 gap-1.5 text-xs font-medium">
        Nome do cargo
        <Input autoFocus maxLength={100} value={name} onChange={(event) => setName(event.target.value)} />
      </label>
      <Button type="submit" size="sm" disabled={pending || !name.trim()}>
        {submitLabel}
      </Button>
      <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
        Cancelar
      </Button>

      {Boolean(error) && (
        <p role="alert" className="text-destructive w-full text-sm">
          {errorMessage(error)}
        </p>
      )}
    </form>
  )
}
