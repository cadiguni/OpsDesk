import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Download, FileCheck2, Upload } from 'lucide-react'
import { Link } from 'react-router-dom'

import { Button, buttonVariants } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { useSession } from '@/features/auth/session-context'
import { importTickets, type ImportFormat, type ImportReport } from '@/features/import/api'
import { SpiceworksReconciliation } from '@/features/import/spiceworks-reconciliation'
import { errorMessage } from '@/lib/api'
import { cn } from '@/lib/utils'

const formats: Record<ImportFormat, { label: string; hint: string }> = {
  OpsDesk: {
    label: 'Planilha no modelo do OpsDesk (CSV ou JSON)',
    hint: 'Colunas: titulo e solicitante_email (obrigatórias), descricao, solicitante_nome, categoria, prioridade, status, responsavel_email, criado_em, resolvido_em, fechado_em e codigo_externo.',
  },
  Freshdesk: {
    label: 'Exportação do Freshdesk (CSV)',
    hint: 'Em Tickets › Exportar, inclua Ticket ID, Subject, Description, Status, Priority, Type, Agent, Created time e os campos do contato (nome e e-mail).',
  },
  Freshservice: {
    label: 'Exportação do Freshservice (CSV)',
    hint: 'Em Tickets › Exportar, inclua Ticket Id, Subject, Description, Status, Priority, Category, Agent, Created Date e o e-mail do solicitante.',
  },
}

const template =
  'codigo_externo;titulo;descricao;solicitante_email;solicitante_nome;categoria;prioridade;status;responsavel_email;criado_em;resolvido_em;fechado_em\n' +
  'ANT-1;VPN não conecta;Erro de certificado desde a manhã;ana@empresa.com;Ana Souza;VPN;Alta;Resolvido;tecnico@empresa.com;30/09/2026 09:15;30/09/2026 11:40;\n'

/**
 * Importação de chamados de outra ferramenta (README, seção 13.14). Só gestor.
 *
 * Em dois passos de propósito: a conferência lê o arquivo inteiro e diz o que entraria, o
 * que seria pulado e por quê, sem gravar nada. Só depois aparece o botão que grava.
 */
export function TicketImport() {
  const { role } = useSession()
  const queryClient = useQueryClient()
  const [format, setFormat] = useState<ImportFormat>('OpsDesk')
  const [file, setFile] = useState<File | null>(null)

  const preview = useMutation({ mutationFn: importTickets })
  const run = useMutation({
    mutationFn: importTickets,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['tickets'] }),
  })

  if (role !== 'Manager') {
    return <p className="text-muted-foreground text-sm">Apenas administradores importam chamados.</p>
  }

  function reset() {
    preview.reset()
    run.reset()
  }

  const report = preview.data

  return (
    <div className="max-w-3xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Importar chamados</h1>
        <p className="text-muted-foreground text-sm">
          Traga o histórico de outra ferramenta. Chamado importado fica fora dos indicadores de SLA: ele
          foi atendido antes de o prazo do OpsDesk existir.
        </p>
      </div>

      <form
        className="space-y-4 rounded-xl border bg-card p-4 shadow-sm"
        onSubmit={(event) => {
          event.preventDefault()
          if (file) {
            run.reset()
            preview.mutate({ file, format, dryRun: true })
          }
        }}
      >
        <Field label="Origem" hint={formats[format].hint}>
          {(field) => (
            <Select
              {...field}
              value={format}
              onChange={(event) => {
                setFormat(event.target.value as ImportFormat)
                reset()
              }}
            >
              {Object.entries(formats).map(([value, { label }]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </Select>
          )}
        </Field>

        <Field label="Arquivo" hint="Até 10 MB e 5.000 chamados. No Excel, salve como “CSV UTF-8”, para os acentos chegarem certos.">
          {(field) => (
            <Input
              {...field}
              type="file"
              accept=".csv,.json,text/csv,application/json"
              onChange={(event) => {
                setFile(event.target.files?.[0] ?? null)
                reset()
              }}
            />
          )}
        </Field>

        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" disabled={!file || preview.isPending}>
            <FileCheck2 /> {preview.isPending ? 'Conferindo…' : 'Conferir'}
          </Button>

          {format === 'OpsDesk' && (
            <a
              className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }))}
              href={`data:text/csv;charset=utf-8,${encodeURIComponent('﻿' + template)}`}
              download="modelo-importacao-opsdesk.csv"
            >
              <Download /> Baixar modelo
            </a>
          )}
        </div>

        {preview.isError && (
          <p role="alert" className="text-destructive text-sm">
            {errorMessage(preview.error, 'Não foi possível ler o arquivo.')}
          </p>
        )}
      </form>

      {report && !run.data && (
        <Preview
          report={report}
          importing={run.isPending}
          onImport={() => file && run.mutate({ file, format, dryRun: false })}
        />
      )}

      {run.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(run.error, 'A importação falhou. Nada foi gravado depois do erro; confira e tente de novo.')}
        </p>
      )}

      {run.data && (
        <div role="status" className="space-y-2 rounded-xl border border-emerald-600/30 bg-emerald-600/5 p-4 text-sm">
          <p className="font-medium">
            {plural(run.data.created, 'chamado importado', 'chamados importados')}.
          </p>
          <p className="text-muted-foreground">
            {run.data.newRequesters > 0 &&
              `${plural(run.data.newRequesters, 'solicitante novo cadastrado', 'solicitantes novos cadastrados')} sem senha — quem quiser acompanhar os chamados define a senha em “Esqueci minha senha”. `}
            {run.data.alreadyImported > 0 &&
              `${plural(run.data.alreadyImported, 'linha já tinha sido importada', 'linhas já tinham sido importadas')} e ficou de fora. `}
          </p>
          <Link to="/chamados" className="text-primary text-sm font-medium hover:underline">
            Ver chamados
          </Link>
        </div>
      )}

      <SpiceworksReconciliation />
    </div>
  )
}

function Preview({
  report,
  importing,
  onImport,
}: {
  report: ImportReport
  importing: boolean
  onImport: () => void
}) {
  const skipped = report.rows - report.valid - report.alreadyImported

  return (
    <section aria-label="Conferência" className="space-y-4 rounded-xl border bg-card p-4 shadow-sm">
      <h2 className="font-semibold">Conferência</h2>

      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Stat label="Linhas no arquivo" value={report.rows} />
        <Stat label="Prontas para importar" value={report.valid} />
        <Stat label="Já importadas" value={report.alreadyImported} />
        <Stat label="Solicitantes novos" value={report.newRequesters} />
      </dl>

      {report.missingColumns.length > 0 && (
        <p role="alert" className="text-destructive text-sm">
          O arquivo não tem as colunas obrigatórias: {report.missingColumns.join(', ')}. Confira a origem
          escolhida e o cabeçalho do arquivo.
        </p>
      )}

      {report.issues.length > 0 && (
        <div className="space-y-2">
          <p className="text-sm font-medium">
            {skipped > 0
              ? `${plural(skipped, 'linha será pulada', 'linhas serão puladas')}:`
              : 'Problemas encontrados:'}
          </p>
          <ul className="max-h-64 space-y-1 overflow-y-auto rounded-md border p-3 text-sm">
            {report.issues.map((issue, index) => (
              <li key={index}>
                {issue.line > 0 && <span className="text-muted-foreground font-mono">Linha {issue.line}: </span>}
                {issue.message}
              </li>
            ))}
          </ul>
        </div>
      )}

      {report.valid > 0 && (
        <Button onClick={onImport} disabled={importing}>
          <Upload /> {importing ? 'Importando…' : `Importar ${plural(report.valid, 'chamado', 'chamados')}`}
        </Button>
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
