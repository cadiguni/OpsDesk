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

const offsetFormatter = new Intl.DateTimeFormat('en-US', {
  timeZone: TIME_ZONE,
  timeZoneName: 'longOffset',
})

/**
 * Meia-noite de um dia de calendário em São Paulo, como instante ISO com deslocamento.
 *
 * É o que o filtro de período manda à API: "dia 10" começa à meia-noite daqui, não de
 * Greenwich, e um chamado aberto às 23:30 do dia 9 não pode aparecer no dia 10. O
 * deslocamento é consultado ao `Intl` em vez de fixado em -03:00 para que a conta continue
 * certa se o horário de verão voltar.
 *
 * @param day data no formato `yyyy-mm-dd`, como a devolve um `<input type="date">`.
 */
export function startOfDayInSaoPaulo(day: string): string {
  // Meio-dia UTC cai no mesmo dia de calendário em São Paulo, longe de qualquer virada.
  const noon = new Date(`${day}T12:00:00Z`)
  const name = offsetFormatter.formatToParts(noon).find((part) => part.type === 'timeZoneName')

  // "GMT-03:00" vira "-03:00"; "GMT" sozinho é deslocamento zero.
  const offset = name?.value.replace('GMT', '') || '+00:00'

  return `${day}T00:00:00${offset}`
}

/** O dia seguinte a uma data `yyyy-mm-dd`, no mesmo formato. */
export function nextDay(day: string): string {
  const date = new Date(`${day}T12:00:00Z`)
  date.setUTCDate(date.getUTCDate() + 1)

  return date.toISOString().slice(0, 10)
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

/**
 * Iniciais para o avatar de um autor. Duas letras no máximo: com três, o círculo vira
 * uma mancha ilegível no tamanho em que ele aparece.
 */
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)

  if (parts.length === 0) {
    return '?'
  }

  const first = parts[0]![0]!
  const last = parts.length > 1 ? parts[parts.length - 1]![0]! : ''

  return (first + last).toUpperCase()
}
