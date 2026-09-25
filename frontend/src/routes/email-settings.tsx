import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { AlertTriangle, CheckCircle2, Mail } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session-context'
import type { EmailSettings } from '@/features/settings/api'
import { useEmailSettings, useSendTestEmail, useUpdateEmailSettings } from '@/features/settings/queries'
import { emailSettingsSchema, type EmailSettingsFields } from '@/features/settings/schemas'
import { errorMessage } from '@/lib/api'
import { formatDate, formatDateTime } from '@/lib/format'

/** Dias antes do vencimento do secret em que o aviso aparece. */
const EXPIRY_WARNING_DAYS = 30

/**
 * Configuração do envio de e-mail (README, seção 18, versão 1.2). Só gestor.
 *
 * O client secret é só de escrita: a API nunca o devolve, e a tela nunca o mostra. Salvar
 * sem digitar um secret novo mantém o atual.
 */
export function EmailSettingsPage() {
  const { role } = useSession()
  const settings = useEmailSettings(role === 'Manager')

  if (role !== 'Manager') {
    return <p className="text-muted-foreground text-sm">Apenas gestores alteram as configurações.</p>
  }

  if (settings.isPending) {
    return <p className="text-muted-foreground text-sm">Carregando configuração…</p>
  }

  if (settings.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(settings.error, 'Não foi possível carregar a configuração.')}
      </p>
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Configurações de e-mail</h1>
        <p className="text-muted-foreground text-sm">
          Envio pelo Microsoft 365. O solicitante recebe e-mail a cada resposta pública e mudança de status.
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        {/* A chave remonta o formulário quando a configuração salva volta do servidor. */}
        <SettingsForm key={settings.data.clientSecretUpdatedAt ?? 'novo'} settings={settings.data} />
        <div className="space-y-6">
          <DeliveryStatus settings={settings.data} />
          <TestEmail settings={settings.data} />
        </div>
      </div>
    </div>
  )
}

function SettingsForm({ settings }: { settings: EmailSettings }) {
  const update = useUpdateEmailSettings()
  const [replacingSecret, setReplacingSecret] = useState(!settings.hasClientSecret)
  const [saved, setSaved] = useState(false)

  const form = useForm<EmailSettingsFields>({
    resolver: zodResolver(emailSettingsSchema),
    defaultValues: {
      isEnabled: settings.isEnabled,
      senderAddress: settings.senderAddress ?? '',
      senderName: settings.senderName ?? '',
      tenantId: settings.tenantId ?? '',
      clientId: settings.clientId ?? '',
      // Sempre vazio: não há de onde preenchê-lo, e é assim que tem de ser.
      clientSecret: '',
      clientSecretExpiresOn: settings.clientSecretExpiresOn ?? '',
      portalUrl: settings.portalUrl ?? '',
    },
  })

  function onSubmit(fields: EmailSettingsFields) {
    setSaved(false)

    update.mutate(
      {
        isEnabled: fields.isEnabled,
        senderAddress: fields.senderAddress || null,
        senderName: fields.senderName || null,
        tenantId: fields.tenantId || null,
        clientId: fields.clientId || null,
        // Só vai quando foi digitado. Ausente, o servidor mantém o gravado.
        ...(replacingSecret && fields.clientSecret ? { clientSecret: fields.clientSecret } : {}),
        clientSecretExpiresOn: fields.clientSecretExpiresOn || null,
        portalUrl: fields.portalUrl || null,
      },
      {
        onSuccess: () => {
          setSaved(true)
          form.resetField('clientSecret')
        },
      },
    )
  }

  const errors = form.formState.errors

  return (
    <Card>
      <CardHeader>
        <CardTitle>Envio</CardTitle>
        <CardDescription>
          Dados do aplicativo registrado no Entra ID, com a permissão Mail.Send restrita à caixa de suporte.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form noValidate onSubmit={form.handleSubmit(onSubmit)} className="grid gap-5">
          <label className="flex items-center gap-2 text-sm font-medium">
            <input type="checkbox" className="size-4" {...form.register('isEnabled')} />
            Enviar e-mails aos solicitantes
          </label>

          <div className="grid gap-5 md:grid-cols-2">
            <Field label="Endereço remetente" hint="A caixa de suporte." error={errors.senderAddress?.message}>
              {(field) => <Input {...field} {...form.register('senderAddress')} type="email" placeholder="suporte@empresa.com" />}
            </Field>
            <Field label="Nome de exibição" error={errors.senderName?.message}>
              {(field) => <Input {...field} {...form.register('senderName')} placeholder="Suporte TI" />}
            </Field>
            <Field label="Tenant" hint="ID do diretório ou domínio .onmicrosoft.com." error={errors.tenantId?.message}>
              {(field) => <Input {...field} {...form.register('tenantId')} autoComplete="off" />}
            </Field>
            <Field label="Client id" error={errors.clientId?.message}>
              {(field) => <Input {...field} {...form.register('clientId')} autoComplete="off" />}
            </Field>
          </div>

          <div className="grid gap-2 rounded-lg border p-4">
            <p className="text-sm font-medium">Client secret</p>

            {settings.hasClientSecret && !replacingSecret ? (
              <div className="flex flex-wrap items-center justify-between gap-3">
                <p className="text-muted-foreground text-sm">
                  {settings.clientSecretReadable
                    ? `Configurado${settings.clientSecretUpdatedAt ? ` em ${formatDateTime(settings.clientSecretUpdatedAt)}` : ''}.`
                    : 'O secret gravado não pode mais ser lido pelo servidor. Informe-o de novo.'}
                </p>
                <Button type="button" variant="outline" size="sm" onClick={() => setReplacingSecret(true)}>
                  Trocar secret
                </Button>
              </div>
            ) : (
              <Field
                label="Novo client secret"
                hint="O valor não é exibido de novo depois de salvo."
                error={errors.clientSecret?.message}
              >
                {(field) => (
                  <Input {...field} {...form.register('clientSecret')} type="password" autoComplete="new-password" />
                )}
              </Field>
            )}

            <Field label="Validade do secret" hint="Como aparece no Entra ID. Serve ao aviso de vencimento." error={errors.clientSecretExpiresOn?.message}>
              {(field) => <Input {...field} {...form.register('clientSecretExpiresOn')} type="date" />}
            </Field>
          </div>

          <Field label="Endereço do portal" hint="Para o link do e-mail até o chamado." error={errors.portalUrl?.message}>
            {(field) => <Input {...field} {...form.register('portalUrl')} type="url" placeholder="https://opsdesk.empresa.com" />}
          </Field>

          {update.isError && (
            <p role="alert" className="text-destructive text-sm">
              {errorMessage(update.error, 'Não foi possível salvar a configuração.')}
            </p>
          )}

          {saved && !update.isPending && (
            <p role="status" className="text-sm text-emerald-600 dark:text-emerald-400">
              Configuração salva.
            </p>
          )}

          <div>
            <Button type="submit" disabled={update.isPending}>
              {update.isPending ? 'Salvando…' : 'Salvar'}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

function DeliveryStatus({ settings }: { settings: EmailSettings }) {
  const daysToExpiry = settings.clientSecretExpiresOn ? daysUntil(settings.clientSecretExpiresOn) : null

  return (
    <Card>
      <CardHeader>
        <CardTitle>Situação</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        <p>{settings.isEnabled ? 'Envio ligado.' : 'Envio desligado. Nenhum e-mail entra na fila.'}</p>

        {daysToExpiry !== null && daysToExpiry <= EXPIRY_WARNING_DAYS && (
          <p role="alert" className="text-destructive flex gap-2">
            <AlertTriangle className="mt-0.5 size-4 shrink-0" />
            {daysToExpiry < 0
              ? 'O client secret venceu. O envio para até um secret novo ser salvo.'
              : `O client secret vence em ${daysToExpiry} dia(s). Gere um novo no Entra ID e troque aqui.`}
          </p>
        )}

        {settings.lastSucceededAt && (
          <p className="flex gap-2">
            <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-emerald-600" />
            Último envio bem-sucedido em {formatDateTime(settings.lastSucceededAt)}.
          </p>
        )}

        {settings.lastError && (
          <div className="text-destructive">
            <p className="font-medium">Última falha{settings.lastAttemptAt ? ` (${formatDateTime(settings.lastAttemptAt)})` : ''}:</p>
            <p className="break-words">{settings.lastError}</p>
          </div>
        )}

        <p className="text-muted-foreground">
          Na fila: {settings.pendingCount} · Desistidos: {settings.failedCount}
          {settings.clientSecretExpiresOn && ` · Secret vale até ${formatDate(`${settings.clientSecretExpiresOn}T12:00:00Z`)}`}
        </p>
      </CardContent>
    </Card>
  )
}

function TestEmail({ settings }: { settings: EmailSettings }) {
  const { user } = useSession()
  const send = useSendTestEmail()
  const [to, setTo] = useState(user?.email ?? '')

  return (
    <Card>
      <CardHeader>
        <CardTitle>Testar</CardTitle>
        <CardDescription>Envia agora, com a configuração salva — não a do formulário.</CardDescription>
      </CardHeader>
      <CardContent>
        <form
          className="grid gap-3"
          onSubmit={(event) => {
            event.preventDefault()
            send.mutate(to)
          }}
        >
          <Field label="Enviar para">
            {(field) => <Input {...field} type="email" value={to} onChange={(event) => setTo(event.target.value)} />}
          </Field>

          <Button type="submit" variant="outline" disabled={send.isPending || !settings.isEnabled || !to}>
            <Mail /> {send.isPending ? 'Enviando…' : 'Enviar e-mail de teste'}
          </Button>

          {!settings.isEnabled && <p className="text-muted-foreground text-xs">Ligue e salve o envio para testar.</p>}

          {send.isSuccess && (
            <p role="status" className="text-sm text-emerald-600 dark:text-emerald-400">
              Enviado. Confira a caixa de entrada.
            </p>
          )}

          {send.isError && (
            <p role="alert" className="text-destructive text-sm break-words">
              {errorMessage(send.error, 'O envio falhou.')}
            </p>
          )}
        </form>
      </CardContent>
    </Card>
  )
}

/** Dias corridos até uma data `yyyy-mm-dd`. Negativo quando já passou. */
function daysUntil(day: string): number {
  const target = new Date(`${day}T12:00:00Z`).getTime()

  return Math.ceil((target - Date.now()) / (24 * 60 * 60 * 1000))
}
