/**
 * Formatação para a interface.
 *
 * O backend manda todo instante em UTC. A conversão para America/Sao_Paulo acontece
 * aqui e só aqui — é a ponta da regra registrada na decisão 4.4 da arquitetura.
 */

const TIME_ZONE = 'America/Sao_Paulo'

const dateTimeFormatter = new Intl.DateTimeFormat('pt-BR', {
  timeZone: TIME_ZONE,
  dateStyle: 'short',
  timeStyle: 'short',
})

const dateFormatter = new Intl.DateTimeFormat('pt-BR', {
  timeZone: TIME_ZONE,
  dateStyle: 'short',
})

export function formatDateTime(isoInstant: string): string {
  return dateTimeFormatter.format(new Date(isoInstant))
}

export function formatDate(isoInstant: string): string {
  return dateFormatter.format(new Date(isoInstant))
}

/**
 * Distância até um prazo, em texto curto para a coluna de SLA da listagem.
 * Negativo quer dizer vencido.
 */
export function formatDeadlineDistance(isoInstant: string, now = new Date()): string {
  const minutes = Math.round((new Date(isoInstant).getTime() - now.getTime()) / 60_000)
  const overdue = minutes < 0
  const absolute = Math.abs(minutes)

  const text =
    absolute < 60
      ? `${absolute} min`
      : absolute < 60 * 24
        ? `${Math.floor(absolute / 60)} h`
        : `${Math.floor(absolute / (60 * 24))} d`

  return overdue ? `vencido há ${text}` : `em ${text}`
}
