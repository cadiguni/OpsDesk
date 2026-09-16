import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useLocation, useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session-context'
import { loginSchema, type LoginFields } from '@/features/auth/schemas'
import { errorMessage } from '@/lib/api'

/** Tela de login (README, seção 13.1). */
export function Login() {
  const { signIn } = useSession()
  const navigate = useNavigate()
  const location = useLocation()
  const [failure, setFailure] = useState<string | null>(null)

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
      <Card>
        <CardHeader>
          <CardTitle>Entrar no OpsDesk</CardTitle>
          <CardDescription>Acesse com seu e-mail corporativo.</CardDescription>
        </CardHeader>

        <CardContent>
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
          </form>
        </CardContent>
      </Card>

      <p className="text-muted-foreground mt-6 text-center text-sm">
        Ainda não tem conta?{' '}
        <Link to="/cadastro" className="text-primary font-medium hover:underline">
          Cadastre-se
        </Link>
      </p>
    </div>
  )
}
