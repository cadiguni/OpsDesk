import { z } from 'zod'

import { ticketPriorities } from '@/domain/enums'

/** Espelha CreateTicketRequestValidator do backend, que é quem manda. */
export const createTicketSchema = z.object({
  title: z
    .string()
    .trim()
    .min(1, 'Informe um título.')
    .max(200, 'O título deve ter no máximo 200 caracteres.'),
  description: z
    .string()
    .trim()
    .min(10, 'Descreva o problema com pelo menos 10 caracteres.'),
  categoryId: z.string().min(1, 'Escolha uma categoria.'),
  priority: z.enum(ticketPriorities),

  // Vazio quer dizer "eu mesmo". Só a equipe vê este campo, e é o backend que recusa
  // abertura em nome de terceiro para quem não pode.
  requesterId: z.string().optional(),
})

export type CreateTicketFields = z.infer<typeof createTicketSchema>
