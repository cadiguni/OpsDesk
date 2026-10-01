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
