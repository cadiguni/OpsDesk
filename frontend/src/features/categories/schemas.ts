import { z } from 'zod'

/** Espelha SaveCategoryRequestValidator do backend, que é quem manda. */
export const saveCategorySchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Informe o nome da categoria.')
    .max(100, 'O nome pode ter no máximo 100 caracteres.'),
  description: z.string().trim().max(500, 'A descrição pode ter no máximo 500 caracteres.'),
})

export type SaveCategoryFields = z.infer<typeof saveCategorySchema>
