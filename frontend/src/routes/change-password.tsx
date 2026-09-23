import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useNavigate } from 'react-router-dom'

import { ThemeSelect } from '@/components/theme-select'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session-context'
import { changePasswordSchema, type ChangePasswordFields } from '@/features/auth/schemas'
import { errorMessage } from '@/lib/api'

/**
 * Troca de senha.
 *
 * Atende dois casos na mesma tela: a troca voluntária e a obrigatória do primeiro acesso,
 * que é a razão de ela existir. A diferença é só o texto — quando a senha é provisória a
 * `ProtectedRoute` não deixa sair daqui, e não há para onde voltar.
 */
export function ChangePassword() {
  const { changePassword, mustChangePassword, signOut } = useSession()
  const navigate = useNavigate()
  const [failure, setFailure] = useState<string | null>(null)

  const form = useForm<ChangePasswordFields>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: { currentPassword: '', newPassword: '', newPasswordConfirmation: '' },
  })

  async function onSubmit(fields: ChangePasswordFields) {
    setFailure(null)

    try {
      await changePassword(fields)
      void navigate('/chamados', { replace: true })
    } catch (error) {
      setFailure(errorMessage(error, 'Não foi possível trocar a senha.'))
    }
  }

  return (
    <div className="mx-auto flex min-h-dvh max-w-md flex-col justify-center px-4 py-10">
      <div className="mb-5 flex justify-end"><ThemeSelect /></div>
      <Card>
        <CardHeader>
          <CardTitle>{mustChangePassword ? 'Defina sua senha' : 'Trocar senha'}</CardTitle>
          <CardDescription>
            {mustChangePassword
              ? 'Sua senha atual foi definida na instalação do sistema e vale apenas uma vez. Escolha uma nova para continuar.'
              : 'Trocar a senha encerra suas outras sessões.'}
          </CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={form.handleSubmit(onSubmit)} className="grid gap-5" noValidate>
            <Field label="Senha atual" error={form.formState.errors.currentPassword?.message}>
              {(field) => (
                <Input
                  {...field}
                  {...form.register('currentPassword')}
                  type="password"
                  autoComplete="current-password"
                  autoFocus
                />
              )}
            </Field>

            <Field label="Nova senha" error={form.formState.errors.newPassword?.message}>
              {(field) => (
                <Input
                  {...field}
                  {...form.register('newPassword')}
                  type="password"
                  autoComplete="new-password"
                />
              )}
            </Field>

            <Field
              label="Repita a nova senha"
              error={form.formState.errors.newPasswordConfirmation?.message}
            >
              {(field) => (
                <Input
                  {...field}
                  {...form.register('newPasswordConfirmation')}
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
              {form.formState.isSubmitting ? 'Salvando…' : 'Salvar nova senha'}
            </Button>
          </form>
        </CardContent>
      </Card>

      {/* Sair é a única outra saída daqui: sem isto, quem abriu a tela por engano — ou
          quem não tem a senha provisória em mãos — ficaria preso numa sessão sem porta. */}
      <p className="text-muted-foreground mt-6 text-center text-sm">
        <button type="button" onClick={() => void signOut()} className="hover:underline">
          Sair da conta
        </button>
      </p>
    </div>
  )
}
