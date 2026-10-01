import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

import { ThemeSelect } from '@/components/theme-select'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { resetPassword } from '@/features/auth/options'
import { errorMessage } from '@/lib/api'

const MinimumLength = 8

/**
 * Definir a senha pelo link do e-mail: redefinição de quem esqueceu, e o convite de quem
 * foi cadastrado pelo gestor (README, seção 13.13). O token vem na URL e vale uma vez.
 */
export function ResetPassword() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''

  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [done, setDone] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const tooShort = password.length > 0 && password.length < MinimumLength
  const mismatch = confirmation.length > 0 && confirmation !== password

  async function submit() {
    setFailure(null)
    setPending(true)

    try {
      await resetPassword({ token, newPassword: password, newPasswordConfirmation: confirmation })
      setDone(true)
    } catch (error) {
      setFailure(errorMessage(error, 'Não foi possível definir a senha.'))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mx-auto flex min-h-dvh max-w-md flex-col justify-center px-4 py-10">
      <div className="mb-5 flex justify-end">
        <ThemeSelect />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Definir senha</CardTitle>
          <CardDescription>Escolha uma senha com pelo menos {MinimumLength} caracteres.</CardDescription>
        </CardHeader>

        <CardContent>
          {!token ? (
            <p className="text-sm">
              Este endereço está incompleto. Abra o link exatamente como chegou no e-mail, ou{' '}
              <Link to="/esqueci-senha" className="text-primary font-medium hover:underline">
                peça outro
              </Link>
              .
            </p>
          ) : done ? (
            <div role="status" className="grid gap-4 text-sm">
              <p>Senha definida. As sessões abertas em outros lugares foram encerradas.</p>
              <Link to="/entrar" className="text-primary font-medium hover:underline">
                Entrar com a senha nova
              </Link>
            </div>
          ) : (
            <form
              className="grid gap-5"
              onSubmit={(event) => {
                event.preventDefault()
                void submit()
              }}
            >
              <Field label="Nova senha" error={tooShort ? `Mínimo de ${MinimumLength} caracteres.` : undefined}>
                {(field) => (
                  <Input
                    {...field}
                    type="password"
                    autoComplete="new-password"
                    autoFocus
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                  />
                )}
              </Field>

              <Field label="Confirmar senha" error={mismatch ? 'As senhas não conferem.' : undefined}>
                {(field) => (
                  <Input
                    {...field}
                    type="password"
                    autoComplete="new-password"
                    value={confirmation}
                    onChange={(event) => setConfirmation(event.target.value)}
                  />
                )}
              </Field>

              {failure && (
                <p role="alert" className="text-destructive text-sm">
                  {failure}{' '}
                  <Link to="/esqueci-senha" className="font-medium underline">
                    Pedir outro link
                  </Link>
                </p>
              )}

              <Button
                type="submit"
                disabled={pending || password.length < MinimumLength || confirmation !== password}
              >
                {pending ? 'Salvando…' : 'Definir senha'}
              </Button>
            </form>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
