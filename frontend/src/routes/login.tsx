import { ThemeSelect } from '@/components/theme-select'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom'

import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { microsoftSignInError, microsoftSignInUrl } from '@/features/auth/microsoft'
import { usePublicOptions } from '@/features/auth/options'
import { useSession } from '@/features/auth/session-context'
import { loginSchema, type LoginFields } from '@/features/auth/schemas'
import { errorMessage } from '@/lib/api'

/** Tela de login (README, seção 13.1). */
export function Login() {
  const { signIn } = useSession()
  const options = usePublicOptions()
  const organization = options.data?.organizationName ?? 'OpsDesk'
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams] = useSearchParams()
  // O retorno do login Microsoft que não deu certo volta para cá com o motivo na URL.
  const [failure, setFailure] = useState<string | null>(() => microsoftSignInError(searchParams.get('microsoft')))

  const form = useForm<LoginFields>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  })

  // Para onde voltar depois de entrar: a rota protegida que redirecionou para cá, ou a
  // home. Sem isto, quem abre um link de chamado sem sessão perde o destino no login.
  const destination = (location.state as { from?: string } | null)?.from ?? '/'

  async function onSubmit(fields: LoginFields) {
    setFailure(null)

    try {
      await signIn(fields)
      void navigate(destination, { replace: true })
    } catch (error) {
      setFailure(errorMessage(error, 'Não foi possível entrar.'))
    }
  }

  return (
    <div className="mx-auto flex min-h-dvh max-w-md flex-col justify-center px-4 py-10">
      <div className="mb-5 flex justify-end"><ThemeSelect /></div>
      <Card>
        <CardHeader>
          <CardTitle>Entrar no {organization}</CardTitle>
          <CardDescription>Acesse com seu e-mail corporativo.</CardDescription>
        </CardHeader>

        <CardContent className="grid gap-5">
          {options.data?.microsoftSignInAvailable && (
            <>
              {/* Navegação, não chamada da API: o navegador passa pela Microsoft e volta. */}
              <a
                href={microsoftSignInUrl(window.location.origin + destination)}
                className={buttonVariants({ variant: 'outline' })}
              >
                <MicrosoftLogo />
                Entrar com a conta Microsoft
              </a>

              <div className="text-muted-foreground flex items-center gap-3 text-xs">
                <span className="bg-border h-px flex-1" />
                ou com e-mail e senha
                <span className="bg-border h-px flex-1" />
              </div>
            </>
          )}

          <form onSubmit={form.handleSubmit(onSubmit)} className="grid gap-5" noValidate>
            <Field label="E-mail" error={form.formState.errors.email?.message}>
              {(field) => (
                <Input
                  {...field}
                  {...form.register('email')}
                  type="email"
                  autoComplete="email"
                  autoFocus
                  placeholder="voce@empresa.com"
                />
              )}
            </Field>

            <Field label="Senha" error={form.formState.errors.password?.message}>
              {(field) => (
                <Input
                  {...field}
                  {...form.register('password')}
                  type="password"
                  autoComplete="current-password"
                />
              )}
            </Field>

            {failure && (
              <p role="alert" className="text-destructive text-sm">
                {failure}
              </p>
            )}

            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting ? 'Entrando…' : 'Entrar'}
            </Button>

            {/* Só aparece quando funciona: sem e-mail configurado, o link não chegaria. */}
            {options.data?.passwordResetAvailable && (
              <Link to="/esqueci-senha" className="text-primary text-center text-sm hover:underline">
                Esqueci minha senha
              </Link>
            )}
          </form>
        </CardContent>
      </Card>

      {options.data?.allowSelfRegistration !== false && (
        <p className="text-muted-foreground mt-6 text-center text-sm">
          Ainda não tem conta?{' '}
          <Link to="/cadastro" className="text-primary font-medium hover:underline">
            Cadastre-se
          </Link>
        </p>
      )}
    </div>
  )
}

/** Os quatro quadrados, como a Microsoft pede que o botão de login os mostre. */
function MicrosoftLogo() {
  return (
    <svg viewBox="0 0 21 21" aria-hidden="true">
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  )
}
