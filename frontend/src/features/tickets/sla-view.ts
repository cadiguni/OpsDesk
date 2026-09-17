import { formatDateTime, formatDeadlineDistance } from '@/lib/format'

/**
 * Leitura de prazo para a interface.
 *
 * O cálculo de SLA é do backend e mora no `IBusinessCalendar` — aqui não se calcula
 * prazo nenhum, só se traduz o par (prazo, marco cumprido) em um rótulo e um tom de cor.
 * Centralizar isso evita que lista e detalhe divirjam no que consideram "vencido".
 */

export type DeadlineTone = 'met' | 'overdue' | 'due-soon' | 'pending'

export type DeadlineView = {
  tone: DeadlineTone
  /** Texto curto para chip de lista: "em 3 h", "vencido há 1 d", "resolvido". */
  short: string
  /** Texto longo para o painel do detalhe, com data absoluta quando já cumprido. */
  long: string
}

/**
 * Limite para "vence logo", em horas de relógio.
 *
 * Deliberadamente em horas corridas, e não em horas úteis: o cliente não tem o
 * calendário de expediente, e replicar a conta aqui quebraria a invariante 6. O tom
 * amarelo é um alerta visual, não um número que alguém vá auditar — a data exata
 * continua ao lado.
 */
const DUE_SOON_HOURS = 4

export function describeDeadline(
  dueAt: string,
  metAt: string | null,
  metLabel: string,
  now = new Date(),
): DeadlineView {
  if (metAt !== null) {
    return {
      tone: 'met',
      short: metLabel,
      long: `${metLabel} em ${formatDateTime(metAt)}`,
    }
  }

  const remainingHours = (new Date(dueAt).getTime() - now.getTime()) / 3_600_000
  const distance = formatDeadlineDistance(dueAt, now)

  return {
    tone: remainingHours < 0 ? 'overdue' : remainingHours < DUE_SOON_HOURS ? 'due-soon' : 'pending',
    short: distance,
    long: `${distance} · ${formatDateTime(dueAt)}`,
  }
}

export const deadlineToneClasses: Record<DeadlineTone, string> = {
  met: 'text-sla-ok',
  overdue: 'text-sla-overdue font-medium',
  'due-soon': 'text-sla-due-soon font-medium',
  pending: 'text-muted-foreground',
}
