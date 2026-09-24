import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Plus, Search } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { useSession } from '@/features/auth/session-context'
import type { ManagedCategory, SaveCategoryInput } from '@/features/categories/api'
import {
  useChangeCategoryActivation,
  useCreateCategory,
  useManagedCategories,
  useUpdateCategory,
} from '@/features/categories/queries'
import { saveCategorySchema, type SaveCategoryFields } from '@/features/categories/schemas'
import { errorMessage } from '@/lib/api'
import { cn } from '@/lib/utils'

/**
 * Administração de categorias (README, seção 13.10). Só gestor.
 *
 * Não há exclusão, só desativação: a categoria sai dos seletores e os chamados que já a
 * usam continuam como estão.
 */
export function CategoryAdmin() {
  const { role } = useSession()
  const [params, setParams] = useSearchParams()
  const [creating, setCreating] = useState(false)

  const search = params.get('search') ?? ''
  const activeParam = params.get('isActive')
  const isActive = activeParam === null ? undefined : activeParam === 'true'
  const page = Number(params.get('page') ?? '1')

  const categories = useManagedCategories(
    { search: search || undefined, isActive, page },
    role === 'Manager',
  )

  if (role !== 'Manager') {
    return <p className="text-muted-foreground text-sm">Apenas gestores administram categorias.</p>
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
          <h1 className="text-xl font-semibold tracking-tight">Categorias</h1>
          <p className="text-muted-foreground text-sm">
            {categories.data ? `${categories.data.totalCount} categoria(s)` : 'Carregando…'}
          </p>
        </div>

        {!creating && (
          <Button onClick={() => setCreating(true)}>
            <Plus /> Nova categoria
          </Button>
        )}
      </div>

      {creating && <CreateCategory onDone={() => setCreating(false)} />}

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
            placeholder="Buscar por nome"
            aria-label="Buscar categorias"
            className="pl-9"
            onBlur={(event) => update({ search: event.target.value })}
          />
        </form>

        <label className="grid gap-1.5 text-xs font-medium">
          Situação
          <Select value={activeParam ?? ''} onChange={(event) => update({ isActive: event.target.value })}>
            <option value="">Todas</option>
            <option value="true">Ativas</option>
            <option value="false">Desativadas</option>
          </Select>
        </label>
      </div>

      {categories.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(categories.error, 'Não foi possível carregar as categorias.')}
        </p>
      )}

      {categories.isPending && <p className="text-muted-foreground text-sm">Carregando categorias…</p>}

      {categories.data?.items.length === 0 && (
        <div className="text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
          Nenhuma categoria encontrada com esses filtros.
        </div>
      )}

      {categories.data && categories.data.items.length > 0 && (
        <ul
          className={cn(
            'divide-y rounded-xl border bg-card shadow-sm',
            categories.isPlaceholderData && 'opacity-60',
          )}
        >
          {categories.data.items.map((category) => (
            <CategoryRow key={category.id} category={category} />
          ))}
        </ul>
      )}

      {categories.data && categories.data.totalPages > 1 && (
        <div className="flex items-center justify-between text-sm">
          <p className="text-muted-foreground">
            Página {categories.data.page} de {categories.data.totalPages}
          </p>

          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={categories.data.page <= 1}
              onClick={() => update({ page: String(categories.data.page - 1) })}
            >
              Anterior
            </Button>

            <Button
              variant="outline"
              size="sm"
              disabled={!categories.data.hasNextPage}
              onClick={() => update({ page: String(categories.data.page + 1) })}
            >
              Próxima
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

function CreateCategory({ onDone }: { onDone: () => void }) {
  const create = useCreateCategory()

  return (
    <div className="rounded-xl border bg-card p-4 shadow-sm">
      <h2 className="mb-4 text-sm font-semibold">Nova categoria</h2>
      <CategoryForm
        submitLabel="Criar categoria"
        pending={create.isPending}
        error={create.error}
        onCancel={onDone}
        onSubmit={(input) => create.mutate(input, { onSuccess: onDone })}
      />
    </div>
  )
}

function CategoryRow({ category }: { category: ManagedCategory }) {
  const [editing, setEditing] = useState(false)
  const updateCategory = useUpdateCategory()
  const activation = useChangeCategoryActivation()

  function onToggleActive() {
    if (
      category.isActive &&
      !window.confirm(
        `Desativar "${category.name}"? Ela sai das opções de abertura e reclassificação. ` +
          'Os chamados que já estão nela continuam como estão.',
      )
    ) {
      return
    }

    activation.mutate({ id: category.id, isActive: !category.isActive })
  }

  if (editing) {
    return (
      <li className="p-4">
        <CategoryForm
          initial={category}
          submitLabel="Salvar"
          pending={updateCategory.isPending}
          error={updateCategory.error}
          onCancel={() => {
            updateCategory.reset()
            setEditing(false)
          }}
          onSubmit={(input) =>
            updateCategory.mutate({ id: category.id, input }, { onSuccess: () => setEditing(false) })
          }
        />
      </li>
    )
  }

  return (
    <li className="space-y-2 p-4">
      <div className="flex flex-wrap items-center gap-4">
        <div className="min-w-56 flex-1">
          <p className="flex flex-wrap items-center gap-2 font-medium">
            {category.name}
            {!category.isActive && <Badge variant="outline">Desativada</Badge>}
          </p>
          {category.description && (
            <p className="text-muted-foreground text-sm">{category.description}</p>
          )}
          <p className="text-muted-foreground mt-1 text-xs">
            {category.totalTickets} chamado(s), {category.openTickets} em aberto
          </p>
        </div>

        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => setEditing(true)}>
            Editar
          </Button>
          <Button
            variant={category.isActive ? 'outline' : 'default'}
            size="sm"
            disabled={activation.isPending}
            onClick={onToggleActive}
          >
            {category.isActive ? 'Desativar' : 'Reativar'}
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

function CategoryForm({
  initial,
  submitLabel,
  pending,
  error,
  onSubmit,
  onCancel,
}: {
  initial?: ManagedCategory
  submitLabel: string
  pending: boolean
  error: unknown
  onSubmit: (input: SaveCategoryInput) => void
  onCancel: () => void
}) {
  const form = useForm<SaveCategoryFields>({
    resolver: zodResolver(saveCategorySchema),
    defaultValues: { name: initial?.name ?? '', description: initial?.description ?? '' },
  })

  return (
    <form
      noValidate
      className="grid gap-4"
      onSubmit={form.handleSubmit((fields) =>
        onSubmit({ name: fields.name, description: fields.description || undefined }),
      )}
    >
      <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
        <Field label="Nome" error={form.formState.errors.name?.message}>
          {(field) => <Input {...field} {...form.register('name')} autoFocus maxLength={100} />}
        </Field>

        <Field label="Descrição" hint="Opcional." error={form.formState.errors.description?.message}>
          {(field) => <Input {...field} {...form.register('description')} maxLength={500} />}
        </Field>
      </div>

      {Boolean(error) && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(error)}
        </p>
      )}

      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={pending}>
          {submitLabel}
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
          Cancelar
        </Button>
      </div>
    </form>
  )
}
