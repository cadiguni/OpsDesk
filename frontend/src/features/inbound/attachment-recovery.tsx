import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Paperclip } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { runAttachmentRecovery } from '@/features/inbound/api'
import { usePendingAttachmentRecovery } from '@/features/inbound/queries'
import { errorMessage } from '@/lib/api'

type Progress = {
  examined: number
  recovered: number
  attachments: number
  notFound: number
  total: number
}

/**
 * Traz os anexos e as imagens das mensagens lidas antes de o OpsDesk guardar o identificador
 * delas no Microsoft 365 (docs/integracao-email.md, 4.7.1). O servidor trabalha em lotes
 * pequenos, e esta tela os encadeia até acabar, mostrando o andamento.
 *
 * Some quando não há mais nada a recuperar, e só aparece para o administrador.
 */
export function AttachmentRecovery({ credentialsReady }: { credentialsReady: boolean }) {
  const queryClient = useQueryClient()
  const pending = usePendingAttachmentRecovery(true)
  const [running, setRunning] = useState(false)
  const [progress, setProgress] = useState<Progress | null>(null)
  const [error, setError] = useState<string | null>(null)

  if (!pending.data && !progress) {
    return null
  }

  async function run() {
    const total = pending.data ?? 0
    let current: Progress = { examined: 0, recovered: 0, attachments: 0, notFound: 0, total }
    let cursor: string | null = null

    setRunning(true)
    setError(null)
    setProgress(current)

    try {
      do {
        const batch = await runAttachmentRecovery(cursor)

        current = {
          ...current,
          examined: current.examined + batch.examined,
          recovered: current.recovered + batch.recovered,
          attachments: current.attachments + batch.attachments,
          notFound: current.notFound + batch.notFound,
        }
        setProgress(current)

        if (batch.error) {
          setError(batch.error)
          break
        }

        cursor = batch.cursor
      } while (cursor)
    } catch (failure) {
      setError(errorMessage(failure, 'A recuperação foi interrompida.'))
    } finally {
      setRunning(false)
      void queryClient.invalidateQueries({ queryKey: ['inbound'] })
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Anexos de e-mails antigos</CardTitle>
        <CardDescription>
          E-mails lidos antes de o OpsDesk trazer anexos chegaram sem os prints e arquivos. Aqui eles são buscados de
          novo na caixa monitorada e entram no chamado, na abertura ou na resposta certa. Mensagem já apagada da caixa
          não tem de onde vir. Nada disso avisa o solicitante nem entra no histórico.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        {pending.data ? (
          <p>
            {pending.data.toLocaleString('pt-BR')} {pending.data === 1 ? 'mensagem' : 'mensagens'} a verificar.
          </p>
        ) : null}

        {progress && (
          <p role="status">
            {running ? 'Verificando' : 'Verificadas'} {progress.examined.toLocaleString('pt-BR')} de{' '}
            {progress.total.toLocaleString('pt-BR')}: {progress.attachments.toLocaleString('pt-BR')}{' '}
            {progress.attachments === 1 ? 'anexo trazido' : 'anexos trazidos'} em {progress.recovered}{' '}
            {progress.recovered === 1 ? 'mensagem' : 'mensagens'}
            {progress.notFound > 0 && `, ${progress.notFound} não encontradas na caixa`}.
          </p>
        )}

        {error && (
          <p role="alert" className="text-destructive break-words">
            {error}
          </p>
        )}

        <Button variant="outline" onClick={() => void run()} disabled={running || !credentialsReady || !pending.data}>
          <Paperclip /> {running ? 'Trazendo anexos…' : 'Trazer anexos'}
        </Button>

        {!credentialsReady && (
          <p className="text-muted-foreground text-xs">Configure o aplicativo do Entra ID acima para buscar na caixa.</p>
        )}
      </CardContent>
    </Card>
  )
}
