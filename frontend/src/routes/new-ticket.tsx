import { useRef, useState } from 'react'
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
import { useSession } from '@/features/auth/session-context'
import { AttachmentPicker } from '@/features/attachments/attachment-picker'
import {
  isUploading,
  uploadedIds,
  type PendingAttachment,
} from '@/features/attachments/pending-attachment'
import { useCategories, useCreateTicket, useUsers } from '@/features/tickets/queries'
import { createTicketSchema, type CreateTicketFields } from '@/features/tickets/schemas'
import { errorMessage } from '@/lib/api'

/** Abertura de chamado (README, seção 13.4). */
export function NewTicket() {
  const navigate = useNavigate()
  const { isStaff, user } = useSession()
  const categories = useCategories()
  const users = useUsers(isStaff)
  const create = useCreateTicket()

  const [attachments, setAttachments] = useState<PendingAttachment[]>([])

  // Colar print funciona no campo de descrição, que é onde a pessoa está digitando.
  const description = useRef<HTMLTextAreaElement>(null)

  const form = useForm<CreateTicketFields>({
    resolver: zodResolver(createTicketSchema),
    defaultValues: {
      title: '',
      description: '',
      categoryId: '',
      priority: 'Medium',
      requesterId: '',
    },
  })

  async function onSubmit(fields: CreateTicketFields) {
    const ticket = await create.mutateAsync({
      ...fields,

      // Campo vazio, ou preenchido com o próprio usuário, é abertura comum: não manda
      // nada e deixa o backend usar quem está autenticado.
      requesterId:
        fields.requesterId && fields.requesterId !== user?.id ? fields.requesterId : undefined,
      attachmentIds: uploadedIds(attachments),
    })

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
              {(field) => {
                const { ref, ...registration } = form.register('description')

                return (
                  <Textarea
                    {...field}
                    {...registration}
                    ref={(element) => {
                      // O formulário e o colador de print precisam do mesmo elemento: um
                      // para validar, outro para escutar o Ctrl+V.
                      ref(element)
                      description.current = element
                    }}
                    rows={6}
                    placeholder="Cole aqui a mensagem de erro, ou um print — Ctrl+V anexa a imagem."
                  />
                )
              }}
            </Field>

            <Field label="Anexos" hint="Print da tela, log, planilha. Opcional.">
              {() => (
                <AttachmentPicker
                  attachments={attachments}
                  onChange={setAttachments}
                  disabled={form.formState.isSubmitting}
                  pasteTarget={description}
                />
              )}
            </Field>

            {/* Só a equipe abre chamado no nome de outra pessoa — é o atendimento por
                telefone ou presencial, em que quem digita não é quem tem o problema. O
                solicitante nem vê o campo, e o backend recusa se ele tentar. */}
            {isStaff && (
              <Field
                label="Solicitante"
                hint="De quem é o problema. O chamado aparece na lista dessa pessoa e as respostas vão para ela."
                error={form.formState.errors.requesterId?.message}
              >
                {(field) => (
                  <Select {...field} {...form.register('requesterId')} disabled={users.isPending}>
                    <option value="">
                      {users.isPending ? 'Carregando…' : 'Eu mesmo'}
                    </option>
                    {users.data?.map((person) => (
                      <option key={person.id} value={person.id}>
                        {person.name} — {person.email}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
            )}

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

            {users.isError && (
              <p role="alert" className="text-destructive text-sm">
                {errorMessage(users.error, 'Não foi possível carregar os usuários.')}
              </p>
            )}

            {create.isError && (
              <p role="alert" className="text-destructive text-sm">
                {errorMessage(create.error, 'Não foi possível abrir o chamado.')}
              </p>
            )}

            <div className="flex items-center gap-3">
              <Button
                type="submit"
                disabled={form.formState.isSubmitting || isUploading(attachments)}
              >
                {form.formState.isSubmitting
                  ? 'Abrindo…'
                  : isUploading(attachments)
                    ? 'Enviando anexos…'
                    : 'Abrir chamado'}
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
