import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { Loader2, Paperclip, TriangleAlert, X } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { uploadAttachment } from '@/features/tickets/api'
import { errorMessage } from '@/lib/api'
import {
  formatFileSize,
  type PendingAttachment,
} from '@/features/attachments/pending-attachment'
import { cn } from '@/lib/utils'

/**
 * Seleção e envio de anexos.
 *
 * O arquivo sobe no momento em que é escolhido, não no envio do formulário: assim a
 * espera acontece enquanto a pessoa ainda está escrevendo, e um arquivo recusado —
 * grande demais, tipo não aceito — aparece na hora, e não depois de um texto longo ter
 * sido digitado e perdido.
 *
 * Três formas de anexar, porque é assim que o arquivo chega na vida real: o botão, o
 * arrastar-e-soltar, e o `Ctrl+V` de um print recém-tirado. A terceira é a que mais
 * importa num service desk — a pessoa aperta PrtScn e cola, sem nunca salvar arquivo
 * nenhum.
 */

export function AttachmentPicker({
  attachments,
  onChange,
  disabled = false,
  /** Elemento cujo `paste` também anexa — normalmente o campo de texto ao lado. */
  pasteTarget,
}: {
  attachments: PendingAttachment[]
  onChange: (next: PendingAttachment[]) => void
  disabled?: boolean
  pasteTarget?: React.RefObject<HTMLElement | null>
}) {
  const inputId = useId()
  const [isDraggingOver, setIsDraggingOver] = useState(false)

  // A lista vive no componente pai, mas o envio é assíncrono e termina depois de várias
  // renderizações. A referência dá ao callback o estado do momento em que ele termina, em
  // vez do estado congelado de quando começou. A escrita fica no efeito: mexer em ref
  // durante a renderização é o tipo de coisa que funciona até o React renderizar duas
  // vezes.
  const latest = useRef(attachments)

  useEffect(() => {
    latest.current = attachments
  })

  const upload = useCallback(
    async (files: File[]) => {
      if (files.length === 0) {
        return
      }

      const started: PendingAttachment[] = files.map((file) => ({
        key: `${file.name}-${file.size}-${crypto.randomUUID()}`,
        name: file.name,
        size: file.size,
        status: 'uploading',
      }))

      onChange([...latest.current, ...started])

      await Promise.all(
        files.map(async (file, index) => {
          const key = started[index]!.key

          try {
            const uploaded = await uploadAttachment(file)

            onChange(
              latest.current.map((item) =>
                item.key === key ? { ...item, status: 'done', id: uploaded.id } : item,
              ),
            )
          } catch (error) {
            onChange(
              latest.current.map((item) =>
                item.key === key
                  ? {
                      ...item,
                      status: 'error',
                      error: errorMessage(error, 'Não foi possível enviar este arquivo.'),
                    }
                  : item,
              ),
            )
          }
        }),
      )
    },
    [onChange],
  )

  // Colar: o print vem no clipboard como arquivo sem nome útil ("image.png"), então o
  // nome é montado aqui para que a lista do chamado não fique com cinco "image.png".
  useEffect(() => {
    const target = pasteTarget?.current

    if (!target || disabled) {
      return
    }

    function onPaste(event: ClipboardEvent) {
      const files = Array.from(event.clipboardData?.files ?? [])

      if (files.length === 0) {
        return
      }

      // Só impede a colagem padrão quando há arquivo: colar texto continua colando texto.
      event.preventDefault()

      void upload(
        files.map((file) =>
          file.name && file.name !== 'image.png'
            ? file
            : new File([file], `print-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '')}.png`, {
                type: file.type,
              }),
        ),
      )
    }

    target.addEventListener('paste', onPaste as EventListener)

    return () => target.removeEventListener('paste', onPaste as EventListener)
  }, [pasteTarget, disabled, upload])

  function remove(key: string) {
    // Some da lista local. O anexo enviado e nunca vinculado fica pendente no servidor,
    // invisível para todo mundo menos para quem enviou — não há o que vazar.
    onChange(attachments.filter((item) => item.key !== key))
  }

  return (
    <div className="space-y-2">
      <div
        onDragOver={(event) => {
          event.preventDefault()
          setIsDraggingOver(true)
        }}
        onDragLeave={() => setIsDraggingOver(false)}
        onDrop={(event) => {
          event.preventDefault()
          setIsDraggingOver(false)

          if (!disabled) {
            void upload(Array.from(event.dataTransfer.files))
          }
        }}
        className={cn(
          'rounded-lg border border-dashed p-3 text-center text-xs transition-colors',
          isDraggingOver ? 'border-primary bg-primary/5' : 'text-muted-foreground',
        )}
      >
        <input
          id={inputId}
          type="file"
          multiple
          className="sr-only"
          disabled={disabled}
          onChange={(event) => {
            void upload(Array.from(event.target.files ?? []))

            // Zera para que escolher o mesmo arquivo de novo dispare o evento outra vez.
            event.target.value = ''
          }}
        />

        <label htmlFor={inputId} className="cursor-pointer">
          <Paperclip className="mr-1.5 inline size-3.5" />
          <span className="text-foreground font-medium underline-offset-2 hover:underline">
            Escolher arquivos
          </span>{' '}
          — ou arraste aqui, ou cole um print com Ctrl+V. Até 10 MB por arquivo.
        </label>
      </div>

      {attachments.length > 0 && (
        <ul className="grid gap-1.5">
          {attachments.map((item) => (
            <li
              key={item.key}
              className={cn(
                'flex items-center gap-2 rounded-lg border px-3 py-2 text-xs',
                item.status === 'error' && 'border-destructive/40 bg-destructive/5',
              )}
            >
              {item.status === 'uploading' ? (
                <Loader2 className="size-3.5 shrink-0 animate-spin" />
              ) : item.status === 'error' ? (
                <TriangleAlert className="text-destructive size-3.5 shrink-0" />
              ) : (
                <Paperclip className="text-muted-foreground size-3.5 shrink-0" />
              )}

              <span className="min-w-0 flex-1 truncate">{item.name}</span>

              <span className="text-muted-foreground shrink-0">
                {item.status === 'error' ? item.error : formatFileSize(item.size)}
              </span>

              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="size-6 shrink-0"
                onClick={() => remove(item.key)}
                aria-label={`Remover ${item.name}`}
              >
                <X className="size-3.5" />
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
