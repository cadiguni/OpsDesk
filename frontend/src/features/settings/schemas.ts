import { z } from 'zod'

/**
 * Espelha UpdateEmailSettingsRequestValidator do backend, que é quem manda. O que falta
 * para ligar o envio também é conferido lá, porque só o servidor sabe se já existe secret.
 */
export const emailSettingsSchema = z.object({
  isEnabled: z.boolean(),
  senderAddress: z.union([z.literal(''), z.email('Endereço remetente inválido.')]),
  senderName: z.string().trim().max(200, 'Nome de exibição muito longo.'),
  tenantId: z.string().trim().max(100, 'Tenant muito longo.'),
  clientId: z.union([
    z.literal(''),
    z.guid('O client id é um GUID, como o Entra ID o mostra.'),
  ]),
  clientSecret: z.string().max(500, 'Client secret muito longo.'),
  clientSecretExpiresOn: z.string(),
  portalUrl: z.union([
    z.literal(''),
    z.url({ protocol: /^https?$/, error: 'Informe o endereço completo, com http:// ou https://.' }),
  ]),
})

export type EmailSettingsFields = z.infer<typeof emailSettingsSchema>
