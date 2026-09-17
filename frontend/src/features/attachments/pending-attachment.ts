/**
 * Anexo em trânsito: escolhido na tela, enviado ao servidor, ainda não vinculado a um
 * chamado ou comentário.
 *
 * Vive fora do componente por dois motivos: o formulário que hospeda o seletor precisa do
 * tipo para guardar o estado, e um arquivo com só componentes recarrega melhor em
 * desenvolvimento.
 */
export type PendingAttachment = {
  /** Chave local da linha, estável entre renderizações. */
  key: string
  name: string
  size: number
  status: 'uploading' | 'done' | 'error'
  /** Identificador devolvido pela API, presente quando o envio terminou. */
  id?: string
  error?: string
}

/** Identificadores prontos para mandar junto com o chamado ou o comentário. */
export function uploadedIds(attachments: PendingAttachment[]): string[] {
  return attachments.flatMap((item) => (item.status === 'done' && item.id ? [item.id] : []))
}

/** Há envio em andamento: o formulário espera antes de mandar. */
export function isUploading(attachments: PendingAttachment[]): boolean {
  return attachments.some((item) => item.status === 'uploading')
}

/** Tamanho de arquivo em unidade legível, com uma casa decimal a partir de MB. */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }

  if (bytes < 1024 * 1024) {
    return `${Math.round(bytes / 1024)} KB`
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
