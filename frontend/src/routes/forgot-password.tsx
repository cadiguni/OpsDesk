import { useState } from 'react'
import { Link } from 'react-router-dom'

import { ThemeSelect } from '@/components/theme-select'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { requestPasswordReset, usePublicOptions } from '@/features/auth/options'
import { errorMessage } from '@/lib/api'

/**
 * "Esqueci minha senha" (README, seção 13.13).
 *
 * A confirmação é a mesma exista ou não a conta: a tela não pode servir para descobrir
 * quem trabalha na empresa.
 */
export function ForgotPassword() {
  const options = usePublicOptions()
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit() {
    setFailure(null)
    setPending(true)

    try {
      await requestPasswordReset(email)
      setSent(true)
    } catch (error) {
      setFailure(errorMessage(error, 'Não foi possível enviar o pedido.'))
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
          <CardTitle>Esqueci minha senha</CardTitle>
          <CardDescription>Enviamos um link para você definir uma senha nova.</CardDescription>
        </CardHeader>

        <CardContent>
          {options.data && !options.data.passwordResetAvailable ? (
            <p className="text-sm">
              A redefinição por e-mail não está disponível. Peça à equipe de TI para redefinir sua senha.
            </p>
          ) : sent ? (
            <p role="status" className="text-sm">
              Se existir uma conta com <strong>{email}</strong>, o link chega em alguns minutos. Ele vale por uma
              hora. Confira também a caixa de spam.
            </p>
          ) : (
            <form
              className="grid gap-5"
              onSubmit={(event) => {
                event.preventDefault()
                void submit()
              }}
            >
              <Field label="E-mail">
                {(field) => (
                  <Input
                    {...field}
                    type="email"
                    autoComplete="email"
                    autoFocus
                    required
                    value={email}
                    onChange={(event) => setEmail(event.target.value)}
                  />
                )}
              </Field>

              {failure && (
                <p role="alert" className="text-destructive text-sm">
                  {failure}
                </p>
              )}

              <Button type="submit" disabled={pending || !email}>
                {pending ? 'Enviando…' : 'Enviar link'}
              </Button>
            </form>
          )}
        </CardContent>
      </Card>

      <p className="text-muted-foreground mt-6 text-center text-sm">
        <Link to="/entrar" className="text-primary font-medium hover:underline">
          Voltar para o login
        </Link>
      </p>
    </div>
  )
}
