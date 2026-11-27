import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { FileCheck2, RefreshCw } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { reconcileSpiceworks } from '@/features/import/api'
import { errorMessage } from '@/lib/api'

/**
 * Conciliação com o Spiceworks. Os chamados que a caixa de e-mail abriu a partir de mensagens
 * antigas nasceram com a data da leitura e abertos; a exportação do Spiceworks diz a data real
 * e se o chamado já foi fechado lá. O elo é o Message-ID do e-mail de abertura.
 *
 * Mesmos dois passos da importação: conferir não grava nada.
 */
export function SpiceworksReconciliation() {
  const queryClient = useQueryClient()
  const [file, setFile] = useState<File | null>(null)

  const preview = useMutation({ mutationFn: reconcileSpiceworks })
  const run = useMutation({
    mutationFn: reconcileSpiceworks,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['tickets'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    },
  })

  const report = preview.data
  const pending = report ? report.datesFixed + report.closed : 0

  return (
    <section aria-labelledby="spiceworks" className="space-y-4 rounded-xl border bg-card p-4 shadow-sm">
      <div>
        <h2 id="spiceworks" className="font-semibold">
          Conciliar com o Spiceworks
        </h2>
        <p className="text-muted-foreground text-sm">
          Para os chamados que vieram da caixa de e-mail: a abertura passa a ter a data real, e o que já foi
          fechado no Spiceworks fica fechado aqui, com a data de lá. É correção de origem: ninguém recebe aviso, e
          o histórico do chamado não ganha linha nova. Aplicar de novo não muda nada.
        </p>
      </div>

      <form
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault()
          if (file) {
            run.reset()
            preview.mutate({ file, dryRun: true })
          }
        }}
      >
        <Field label="Exportação do Spiceworks (JSON)" hint="O ticket_export.json da exportação do Help Desk, até 25 MB.">
          {(field) => (
            <Input
              {...field}
              type="file"
              accept=".json,application/json"
              onChange={(event) => {
                setFile(event.target.files?.[0] ?? null)
                preview.reset()
                run.reset()
              }}
            />
          )}
        </Field>

        <Button type="submit" disabled={!file || preview.isPending}>
          <FileCheck2 /> {preview.isPending ? 'Conferindo…' : 'Conferir conciliação'}
        </Button>

        {preview.isError && (
          <p role="alert" className="text-destructive text-sm">
            {errorMessage(preview.error, 'Não foi possível ler a exportação.')}
          </p>
        )}
      </form>

      {report && !run.data && (
        <div aria-label="Conferência da conciliação" role="region" className="space-y-4">
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Stat label="Chamados na exportação" value={report.tickets} />
            <Stat label="Encontrados no OpsDesk" value={report.matched} />
            <Stat label="Com data a corrigir" value={report.datesFixed} />
            <Stat label="A fechar" value={report.closed} />
          </dl>

          <p className="text-muted-foreground text-sm">
            {plural(report.stillOpen, 'continua aberto', 'continuam abertos')} no Spiceworks e{' '}
            {report.stillOpen === 1 ? 'fica' : 'ficam'} como está aqui.{' '}
            {report.notFound > 0 &&
              `${plural(report.notFound, 'chamado de e-mail não tem', 'chamados de e-mail não têm')} correspondente no OpsDesk. `}
            {report.withoutMessageId > 0 &&
              `${plural(report.withoutMessageId, 'foi aberto', 'foram abertos')} sem e-mail no Spiceworks e não ${report.withoutMessageId === 1 ? 'entra' : 'entram'} na conciliação.`}
          </p>

          {report.notes.length > 0 && (
            <div className="space-y-2">
              <p className="text-sm font-medium">Para conferir à mão:</p>
              <ul className="max-h-64 space-y-1 overflow-y-auto rounded-md border p-3 text-sm">
                {report.notes.map((note) => (
                  <li key={note.spiceworksNumber}>
                    <span className="text-muted-foreground font-mono">
                      #{note.spiceworksNumber}
                      {note.code && ` · ${note.code}`}:{' '}
                    </span>
                    {note.message}
                  </li>
                ))}
              </ul>
            </div>
          )}

          {pending > 0 ? (
            <Button onClick={() => file && run.mutate({ file, dryRun: false })} disabled={run.isPending}>
              <RefreshCw /> {run.isPending ? 'Aplicando…' : 'Aplicar conciliação'}
            </Button>
          ) : (
            <p className="text-sm">Nada a corrigir: os chamados já estão conciliados.</p>
          )}
        </div>
      )}

      {run.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(run.error, 'A conciliação falhou no meio. O que já foi aplicado fica; conferir e aplicar de novo continua de onde parou.')}
        </p>
      )}

      {run.data && (
        <p role="status" className="rounded-xl border border-emerald-600/30 bg-emerald-600/5 p-4 text-sm font-medium">
          Conciliação aplicada: {plural(run.data.datesFixed, 'data corrigida', 'datas corrigidas')} e{' '}
          {plural(run.data.closed, 'chamado fechado', 'chamados fechados')}.
        </p>
      )}
    </section>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-lg border p-3">
      <dt className="text-muted-foreground text-xs">{label}</dt>
      <dd className="text-xl font-semibold tabular-nums">{value.toLocaleString('pt-BR')}</dd>
    </div>
  )
}

function plural(count: number, one: string, many: string) {
  return `${count.toLocaleString('pt-BR')} ${count === 1 ? one : many}`
}
