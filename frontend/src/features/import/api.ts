import { api } from '@/lib/api'

export const importFormats = ['OpsDesk', 'Freshdesk', 'Freshservice'] as const

export type ImportFormat = (typeof importFormats)[number]

export type ImportIssue = {
  /** Linha no arquivo, contando o cabeçalho. Zero quando o problema é do arquivo inteiro. */
  line: number
  message: string
}

export type ImportReport = {
  dryRun: boolean
  rows: number
  valid: number
  created: number
  alreadyImported: number
  newRequesters: number
  recognizedColumns: string[]
  missingColumns: string[]
  issues: ImportIssue[]
}

/**
 * Confere (`dryRun`) ou importa um arquivo de chamados. A conferência lê e valida tudo sem
 * gravar nada — é ela que a tela mostra antes do botão de importar.
 */
export async function importTickets(input: {
  file: File
  format: ImportFormat
  dryRun: boolean
}): Promise<ImportReport> {
  const form = new FormData()
  form.append('file', input.file)

  const { data } = await api.post<ImportReport>('/api/admin/import/tickets', form, {
    params: { format: input.format, dryRun: input.dryRun },
    // Mesmo motivo do envio de anexo: o multipart precisa do boundary que só o navegador gera.
    headers: { 'Content-Type': undefined },
  })

  return data
}

export type ReconciliationNote = {
  /** Número no Spiceworks; nulo para chamado do OpsDesk ausente da exportação. */
  spiceworksNumber: number | null
  /** Código do chamado no OpsDesk, quando há correspondente. */
  code: string | null
  message: string
}

export type SpiceworksReconciliationReport = {
  dryRun: boolean
  /** Chamados na exportação. */
  tickets: number
  /** Abertos no Spiceworks sem e-mail (pelo portal de lá): não têm como casar. */
  withoutMessageId: number
  matched: number
  notFound: number
  datesFixed: number
  closed: number
  stillOpen: number
  /** Chamados de e-mail em andamento no OpsDesk que a exportação não tem. */
  openOnlyHere: number
  notes: ReconciliationNote[]
}

/**
 * Confere (`dryRun`) ou aplica a conciliação dos chamados vindos do e-mail com a exportação
 * JSON do Spiceworks: data real de abertura e fechamento, e fechado lá fica fechado aqui.
 */
export async function reconcileSpiceworks(input: { file: File; dryRun: boolean }): Promise<SpiceworksReconciliationReport> {
  const form = new FormData()
  form.append('file', input.file)

  const { data } = await api.post<SpiceworksReconciliationReport>('/api/admin/import/spiceworks', form, {
    params: { dryRun: input.dryRun },
    headers: { 'Content-Type': undefined },
  })

  return data
}
