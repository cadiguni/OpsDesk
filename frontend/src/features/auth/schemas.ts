import { z } from 'zod'

/**
 * Validação do formulário, espelhando as regras do backend.
 *
 * O backend valida de novo e é ele quem manda: isto existe para dar retorno imediato
 * ao usuário, não para substituir a validação do servidor.
 */

const MINIMUM_PASSWORD_LENGTH = 8

export const loginSchema = z.object({
  // z.email() é a forma do Zod 4; z.string().email() está descontinuado.
  email: z.email('Informe um e-mail válido.'),
  password: z.string().min(1, 'Informe sua senha.'),
})

export type LoginFields = z.infer<typeof loginSchema>

export const registerSchema = z
  .object({
    name: z.string().trim().min(1, 'Informe seu nome.').max(200, 'No máximo 200 caracteres.'),
    email: z.email('Informe um e-mail válido.').max(320, 'No máximo 320 caracteres.'),
    password: z
      .string()
      .min(MINIMUM_PASSWORD_LENGTH, `A senha deve ter no mínimo ${MINIMUM_PASSWORD_LENGTH} caracteres.`)
      .max(128, 'No máximo 128 caracteres.'),
    passwordConfirmation: z.string().min(1, 'Repita a senha.'),
  })
  .refine((fields) => fields.password === fields.passwordConfirmation, {
    message: 'As senhas não conferem.',
    // Sem isto o erro apareceria solto no formulário, e não embaixo do campo de
    // confirmação, que é onde a pessoa está olhando.
    path: ['passwordConfirmation'],
  })

export type RegisterFields = z.infer<typeof registerSchema>
