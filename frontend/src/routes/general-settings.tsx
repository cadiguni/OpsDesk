import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session-context'
import { microsoftCallbackUrl } from '@/features/auth/microsoft'
import type { GeneralSettings } from '@/features/settings/api'
import { useGeneralSettings, useUpdateGeneralSettings } from '@/features/settings/queries'
import { SettingsTabs } from '@/features/settings/settings-tabs'
import { errorMessage } from '@/lib/api'

/** Configurações gerais (README, seção 13.12): nome da organização, autocadastro e login Microsoft. Só gestor. */
export function GeneralSettingsPage() {
  const { role } = useSession()
  const settings = useGeneralSettings(role === 'Manager')

  if (role !== 'Manager') {
    return <p className="text-muted-foreground text-sm">Apenas gestores alteram as configurações.</p>
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Configurações</h1>
        <p className="text-muted-foreground text-sm">Como o sistema se apresenta e quem pode entrar nele.</p>
      </div>

      <SettingsTabs />

      {settings.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}

      {settings.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(settings.error, 'Não foi possível carregar a configuração.')}
        </p>
      )}

      {/* A chave remonta o formulário quando a configuração salva volta do servidor. */}
      {settings.data && <GeneralForm key={JSON.stringify(settings.data)} settings={settings.data} />}
    </div>
  )
}

function GeneralForm({ settings }: { settings: GeneralSettings }) {
  const update = useUpdateGeneralSettings()
  const [name, setName] = useState(settings.organizationName)
  const [allowSelfRegistration, setAllowSelfRegistration] = useState(settings.allowSelfRegistration)
  const [domains, setDomains] = useState(settings.allowedEmailDomains.join(', '))
  const [microsoftSignIn, setMicrosoftSignIn] = useState(settings.microsoftSignInEnabled)

  return (
    <form
      className="grid max-w-2xl gap-6"
      onSubmit={(event) => {
        event.preventDefault()
        update.mutate({
          organizationName: name,
          allowSelfRegistration,
          allowedEmailDomains: domains
            .split(/[,\s]+/)
            .map((domain) => domain.trim())
            .filter(Boolean),
          microsoftSignInEnabled: microsoftSignIn,
        })
      }}
    >
      <Card>
        <CardHeader>
          <CardTitle>Identidade</CardTitle>
          <CardDescription>Aparece na tela de login, no menu e nos e-mails.</CardDescription>
        </CardHeader>
        <CardContent>
          <Field label="Nome da organização">
            {(field) => (
              <Input {...field} maxLength={100} value={name} onChange={(event) => setName(event.target.value)} />
            )}
          </Field>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Acesso</CardTitle>
          <CardDescription>
            Quem pode criar a própria conta pelo portal. Técnicos e gestores sempre são cadastrados por um gestor, em
            Usuários.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-5">
          <label className="flex items-center gap-2 text-sm font-medium">
            <input
              type="checkbox"
              className="size-4"
              checked={allowSelfRegistration}
              onChange={(event) => setAllowSelfRegistration(event.target.checked)}
            />
            Permitir que as pessoas criem a própria conta
          </label>

          <Field
            label="Domínios de e-mail aceitos"
            hint="Separados por vírgula, como empresa.com.br, empresa.com. Vazio aceita qualquer e-mail."
          >
            {(field) => (
              <Input
                {...field}
                disabled={!allowSelfRegistration}
                placeholder="empresa.com.br"
                value={domains}
                onChange={(event) => setDomains(event.target.value)}
              />
            )}
          </Field>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Conta Microsoft</CardTitle>
          <CardDescription>
            Botão &quot;Entrar com a conta Microsoft&quot; no login. Qualquer pessoa do tenant entra sem criar senha; no
            primeiro acesso a conta nasce como solicitante, e quem já tinha conta com o mesmo e-mail continua com ela.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 text-sm">
          <label className="flex items-center gap-2 font-medium">
            <input
              type="checkbox"
              className="size-4"
              checked={microsoftSignIn}
              onChange={(event) => setMicrosoftSignIn(event.target.checked)}
            />
            Permitir entrar com a conta Microsoft
          </label>

          {!settings.microsoftSignInReady && (
            <p role="note" className="text-amber-700 dark:text-amber-400">
              Usa o aplicativo do Entra ID da aba E-mail: preencha lá o tenant (o ID, não <code>common</code>), o client
              id e o client secret. Enquanto faltar, o botão não aparece no login.
            </p>
          )}

          <div className="text-muted-foreground grid gap-1">
            <span>No aplicativo do Entra ID, em Autenticação, adicione a plataforma Web com este URI de redirecionamento:</span>
            <code className="bg-muted rounded px-2 py-1 break-all">{microsoftCallbackUrl()}</code>
          </div>
        </CardContent>
      </Card>

      {update.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(update.error, 'Não foi possível salvar.')}
        </p>
      )}

      {update.isSuccess && (
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
  )
}
