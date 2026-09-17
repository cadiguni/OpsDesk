import { useEffect, useState } from 'react'
import { Download, FileText, Image as ImageIcon, LockKeyhole } from 'lucide-react'

import { fetchAttachmentBlob } from '@/features/tickets/api'
import type { Attachment } from '@/features/tickets/types'
import { formatFileSize } from '@/features/attachments/pending-attachment'
import { cn } from '@/lib/utils'

/**
 * Anexos já gravados, na conversa do chamado.
 *
 * Imagem ganha miniatura porque é o que mais chega — print de erro — e porque abrir um
 * arquivo para descobrir que era a tela errada custa mais do que vale. O resto é linha
 * com nome, tamanho e download.
 *
 * O conteúdo é buscado com o token no cabeçalho e vira URL de objeto local: apontar
 * `src` direto para a API não funcionaria, porque o navegador não manda o Authorization
 * ao carregar imagem.
 */
export function AttachmentList({ attachments }: { attachments: Attachment[] }) {
  if (attachments.length === 0) {
    return null
  }

  return (
    <ul className="mt-3 flex flex-wrap gap-2">
      {attachments.map((attachment) => (
        <li key={attachment.id}>
          {attachment.isImage ? (
            <ImageAttachment attachment={attachment} />
          ) : (
            <FileAttachment attachment={attachment} />
          )}
        </li>
      ))}
    </ul>
  )
}

function ImageAttachment({ attachment }: { attachment: Attachment }) {
  const url = useAttachmentUrl(attachment.id)

  return (
    <figure
      className={cn(
        'w-40 overflow-hidden rounded-lg border',
        attachment.isInternal && 'border-sla-due-soon/50',
      )}
    >
      <button
        type="button"
        onClick={() => void download(attachment)}
        className="bg-muted block h-28 w-full"
        aria-label={`Baixar ${attachment.fileName}`}
      >
        {url ? (
          <img src={url} alt={attachment.fileName} className="h-28 w-full object-cover" />
        ) : (
          <span className="text-muted-foreground grid h-28 place-items-center">
            <ImageIcon className="size-5" />
          </span>
        )}
      </button>

      <figcaption className="text-muted-foreground flex items-center gap-1 px-2 py-1.5 text-xs">
        {attachment.isInternal && <LockKeyhole className="text-sla-due-soon size-3 shrink-0" />}
        <span className="truncate">{attachment.fileName}</span>
      </figcaption>
    </figure>
  )
}

function FileAttachment({ attachment }: { attachment: Attachment }) {
  return (
    <button
      type="button"
      onClick={() => void download(attachment)}
      className={cn(
        'flex items-center gap-2 rounded-lg border px-3 py-2 text-xs transition-colors',
        'hover:border-primary/40 hover:bg-accent/40',
        attachment.isInternal && 'border-sla-due-soon/50',
      )}
    >
      {attachment.isInternal ? (
        <LockKeyhole className="text-sla-due-soon size-4 shrink-0" />
      ) : (
        <FileText className="text-muted-foreground size-4 shrink-0" />
      )}

      <span className="max-w-52 truncate font-medium">{attachment.fileName}</span>
      <span className="text-muted-foreground">{formatFileSize(attachment.sizeInBytes)}</span>
      <Download className="text-muted-foreground size-3.5 shrink-0" />
    </button>
  )
}

/** Baixa o conteúdo e entrega ao navegador com o nome original. */
async function download(attachment: Attachment) {
  const blob = await fetchAttachmentBlob(attachment.id)
  const url = URL.createObjectURL(blob)

  const link = document.createElement('a')
  link.href = url
  link.download = attachment.fileName
  link.click()

  URL.revokeObjectURL(url)
}

/**
 * URL local para a miniatura.
 *
 * A URL de objeto precisa ser revogada: sem isso o blob fica preso na memória da aba
 * enquanto ela estiver aberta, e uma conversa com muitos prints vai acumulando.
 */
function useAttachmentUrl(id: string): string | null {
  const [url, setUrl] = useState<string | null>(null)

  useEffect(() => {
    let objectUrl: string | null = null
    let cancelled = false

    void fetchAttachmentBlob(id)
      .then((blob) => {
        if (cancelled) {
          return
        }

        objectUrl = URL.createObjectURL(blob)
        setUrl(objectUrl)
      })
      .catch(() => {
        // A miniatura cai para o ícone genérico; o download continua disponível.
      })

    return () => {
      cancelled = true

      if (objectUrl) {
        URL.revokeObjectURL(objectUrl)
      }
    }
  }, [id])

  return url
}
