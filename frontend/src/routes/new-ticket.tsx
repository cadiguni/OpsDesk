import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate } from 'react-router-dom'

import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { ticketPriorities, ticketPriorityLabels } from '@/domain/enums'
import { useCategories, useCreateTicket } from '@/features/tickets/queries'
import { createTicketSchema, type CreateTicketFields } from '@/features/tickets/schemas'
import { errorMessage } from '@/lib/api'

/** Abertura de chamado (README, seção 13.4). */
export function NewTicket() {
  const navigate = useNavigate()
  const categories = useCategories()
  const create = useCreateTicket()

  const form = useForm<CreateTicketFields>({
    resolver: zodResolver(createTicketSchema),
    defaultValues: { title: '', description: '', categoryId: '', priority: 'Medium' },
  })

  async function onSubmit(fields: CreateTicketFields) {
    const ticket = await create.mutateAsync(fields)

    // README, seção 13.4: depois de criar, o usuário vai para o detalhe do chamado.
    void navigate(`/chamados/${ticket.id}`, { replace: true })
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Abrir chamado</h1>
        <p className="text-muted-foreground text-sm">
          Descreva o problema com o máximo de detalhe que puder.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Dados do chamado</CardTitle>
          <CardDescription>
            A equipe de TI revisa a categoria e a prioridade na triagem.
          </CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={form.handleSubmit(onSubmit)} className="grid gap-5" noValidate>
            <Field label="Título" error={form.formState.errors.title?.message}>
              {(field) => (
                <Input
                  {...field}
                  {...form.register('title')}
                  autoFocus
                  placeholder="Resuma o problema em uma linha"
                />
              )}
            </Field>

            <Field
              label="Descrição"
              hint="O que aconteceu, desde quando, e o que você já tentou."
              error={form.formState.errors.description?.message}
            >
              {(field) => (
                <Textarea
                  {...field}
                  {...form.register('description')}
                  rows={6}
                  placeholder="Cole aqui a mensagem de erro, se houver."
                />
              )}
            </Field>

            <div className="grid gap-5 sm:grid-cols-2">
              <Field label="Categoria" error={form.formState.errors.categoryId?.message}>
                {(field) => (
                  <Select {...field} {...form.register('categoryId')} disabled={categories.isPending}>
                    <option value="">
                      {categories.isPending ? 'Carregando…' : 'Selecione'}
                    </option>
                    {categories.data?.map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.name}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>

              <Field label="Prioridade" error={form.formState.errors.priority?.message}>
                {(field) => (
                  <Select {...field} {...form.register('priority')}>
                    {ticketPriorities.map((priority) => (
                      <option key={priority} value={priority}>
                        {ticketPriorityLabels[priority]}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
            </div>

            {categories.isError && (
              <p role="alert" className="text-destructive text-sm">
                {errorMessage(categories.error, 'Não foi possível carregar as categorias.')}
              </p>
            )}

            {create.isError && (
              <p role="alert" className="text-destructive text-sm">
                {errorMessage(create.error, 'Não foi possível abrir o chamado.')}
              </p>
            )}

            <div className="flex items-center gap-3">
              <Button type="submit" disabled={form.formState.isSubmitting}>
                {form.formState.isSubmitting ? 'Abrindo…' : 'Abrir chamado'}
              </Button>

              <Link to="/chamados" className={buttonVariants({ variant: 'ghost' })}>
                Cancelar
              </Link>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
