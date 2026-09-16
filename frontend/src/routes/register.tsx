import { ThemeSelect } from '@/components/theme-select'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session-context'
import { registerSchema, type RegisterFields } from '@/features/auth/schemas'
import { errorMessage } from '@/lib/api'

/** Tela de cadastro (README, seção 13.2). Todo cadastro pelo portal nasce como usuário. */
export function Register() {
  const { signUp } = useSession()
  const navigate = useNavigate()
  const [failure, setFailure] = useState<string | null>(null)

  const form = useForm<RegisterFields>({
    resolver: zodResolver(registerSchema),
    defaultValues: { name: '', email: '', password: '', passwordConfirmation: '' },
  })

  async function onSubmit(fields: RegisterFields) {
    setFailure(null)

    try {
      await signUp(fields)
      void navigate('/', { replace: true })
    } catch (error) {
      setFailure(errorMessage(error, 'Não foi possível criar a conta.'))
    }
  }

  return (
    <div className="mx-auto flex min-h-dvh max-w-md flex-col justify-center px-4 py-10">
      <div className="mb-5 flex justify-end"><ThemeSelect /></div>
      <Card>
        <CardHeader>
          <CardTitle>Criar conta</CardTitle>
          <CardDescription>
            Sua conta é criada com perfil de usuário. Para acesso de técnico ou gestor,
            procure a equipe de TI.
          </CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={form.handleSubmit(onSubmit)} className="grid gap-5" noValidate>
            <Field label="Nome" error={form.formState.errors.name?.message}>
              {(field) => (
                <Input
                  {...field}
                  {...form.register('name')}
                  autoComplete="name"
                  autoFocus
                  placeholder="Seu nome completo"
                />
              )}
            </Field>

            <Field label="E-mail" error={form.formState.errors.email?.message}>
              {(field) => (
                <Input
                  {...field}
                  {...form.register('email')}
                  type="email"
                  autoComplete="email"
                  placeholder="voce@empresa.com"
                />
              )}
            </Field>

            <Field
              label="Senha"
              hint="No mínimo 8 caracteres."
              error={form.formState.errors.password?.message}
            >
              {(field) => (
                <Input
                  {...field}
                  {...form.register('password')}
                  type="password"
                  autoComplete="new-password"
                />
              )}
            </Field>

            <Field
              label="Confirmar senha"
              error={form.formState.errors.passwordConfirmation?.message}
            >
              {(field) => (
                <Input
                  {...field}
                  {...form.register('passwordConfirmation')}
                  type="password"
                  autoComplete="new-password"
                />
              )}
            </Field>

            {failure && (
              <p role="alert" className="text-destructive text-sm">
                {failure}
              </p>
            )}

            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting ? 'Criando conta…' : 'Criar conta'}
            </Button>
          </form>
        </CardContent>
      </Card>

      <p className="text-muted-foreground mt-6 text-center text-sm">
        Já tem conta?{' '}
        <Link to="/entrar" className="text-primary font-medium hover:underline">
          Entrar
        </Link>
      </p>
    </div>
  )
}
