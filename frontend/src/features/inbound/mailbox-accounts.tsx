import { useState } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { ticketPriorities, ticketPriorityLabels, type TicketPriority } from '@/domain/enums'
import type { EmailAccount, SaveEmailAccountInput } from '@/features/inbound/api'
import { useEmailAccounts, useSaveEmailAccount } from '@/features/inbound/queries'
import { useCategories } from '@/features/tickets/queries'
import { errorMessage } from '@/lib/api'
import { formatDateTime } from '@/lib/format'

const empty: SaveEmailAccountInput = {
  name: '',
  address: '',
  defaultCategoryId: null,
  defaultPriority: 'Medium',
  internalDomainsOnly: false,
  isActive: true,
}

/**
 * Caixas monitoradas (docs/integracao-email.md): o que chega nelas vira chamado. Usam o
 * aplicativo do Entra ID configurado acima — tenant, client id e secret —, com a permissão
 * Mail.ReadWrite restrita a estas caixas. Não dependem do envio estar ligado.
 */
export function MailboxAccounts({ credentialsReady }: { credentialsReady: boolean }) {
  const accounts = useEmailAccounts(true)
  const [editing, setEditing] = useState<EmailAccount | 'new' | null>(null)

  return (
    <Card>
      <CardHeader>
        <CardTitle>Caixas monitoradas</CardTitle>
        <CardDescription>
          E-mails que chegam aqui viram chamado, e as respostas dos solicitantes entram no chamado certo. Exige a
          permissão Mail.ReadWrite no aplicativo do Entra ID, restrita a estas caixas.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {!credentialsReady && (
          <p className="text-muted-foreground text-sm">
            A leitura só acontece com tenant, client id e client secret preenchidos e salvos acima. O envio pode ficar
            desligado.
          </p>
        )}

        {accounts.isError && (
          <p role="alert" className="text-destructive text-sm">
            {errorMessage(accounts.error, 'Não foi possível carregar as caixas.')}
          </p>
        )}

        {accounts.data && accounts.data.length > 0 && (
          <ul className="divide-y rounded-lg border">
            {accounts.data.map((account) => (
              <li key={account.id} className="flex flex-wrap items-center gap-3 p-3 text-sm">
                <div className="min-w-48 flex-1">
                  <p className="flex items-center gap-2 font-medium">
                    {account.name}
                    {!account.isActive && <Badge variant="outline">Desligada</Badge>}
                  </p>
                  <p className="text-muted-foreground">{account.address}</p>
                  <p className="text-muted-foreground text-xs">
                    {account.lastSyncAt ? `Última leitura em ${formatDateTime(account.lastSyncAt)}` : 'Ainda não lida'}
                  </p>
                  {account.lastError && <p className="text-destructive text-xs break-words">{account.lastError}</p>}
                </div>
                <Button variant="outline" size="sm" onClick={() => setEditing(account)}>
                  Editar
                </Button>
              </li>
            ))}
          </ul>
        )}

        {editing ? (
          <AccountForm account={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />
        ) : (
          <Button variant="outline" size="sm" onClick={() => setEditing('new')}>
            Monitorar uma caixa
          </Button>
        )}
      </CardContent>
    </Card>
  )
}

function AccountForm({ account, onClose }: { account: EmailAccount | null; onClose: () => void }) {
  const save = useSaveEmailAccount()
  const categories = useCategories()
  const [form, setForm] = useState<SaveEmailAccountInput>(account ?? empty)

  return (
    <form
      className="grid gap-3 rounded-lg border p-4"
      onSubmit={(event) => {
        event.preventDefault()
        save.mutate({ id: account?.id ?? null, input: form }, { onSuccess: onClose })
      }}
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="grid gap-1.5 text-xs font-medium">
          Nome
          <Input required value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} placeholder="Suporte TI" />
        </label>
        <label className="grid gap-1.5 text-xs font-medium">
          Endereço da caixa
          <Input
            required
            type="email"
            value={form.address}
            onChange={(event) => setForm({ ...form, address: event.target.value })}
            placeholder="suporte@empresa.com"
          />
        </label>
        <label className="grid gap-1.5 text-xs font-medium">
          Categoria do chamado
          <Select
            value={form.defaultCategoryId ?? ''}
            onChange={(event) => setForm({ ...form, defaultCategoryId: event.target.value || null })}
          >
            <option value="">Outros</option>
            {categories.data?.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </Select>
        </label>
        <label className="grid gap-1.5 text-xs font-medium">
          Prioridade inicial
          <Select
            value={form.defaultPriority}
            onChange={(event) => setForm({ ...form, defaultPriority: event.target.value as TicketPriority })}
          >
            {ticketPriorities.map((priority) => (
              <option key={priority} value={priority}>
                {ticketPriorityLabels[priority]}
              </option>
            ))}
          </Select>
        </label>
      </div>

      <p className="text-muted-foreground text-xs">
        A prioridade nunca vem do texto do e-mail: quem escreve "URGENTE" no assunto não decide a fila. A triagem é da
        equipe.
      </p>

      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          className="size-4"
          checked={form.internalDomainsOnly}
          onChange={(event) => setForm({ ...form, internalDomainsOnly: event.target.checked })}
        />
        Só aceitar remetentes dos domínios da organização (Configurações → Geral)
      </label>

      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          className="size-4"
          checked={form.isActive}
          onChange={(event) => setForm({ ...form, isActive: event.target.checked })}
        />
        Ler esta caixa
      </label>

      {save.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(save.error, 'Não foi possível salvar a caixa.')}
        </p>
      )}

      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={save.isPending}>
          Salvar
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onClose}>
          Cancelar
        </Button>
      </div>
    </form>
  )
}
