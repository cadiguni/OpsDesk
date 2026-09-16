import { useState } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'

import { Button } from '@/components/ui/button'
import type { CountByLabel } from '@/features/tickets/types'

/**
 * Gráficos do dashboard.
 *
 * Três decisões que valem registrar, porque são as que costumam ser feitas ao contrário:
 *
 * 1. **Série de cor única.** Cada gráfico mostra uma medida — contagem de chamados — ao
 *    longo de categorias, e o rótulo do eixo já identifica cada barra. Uma cor por
 *    categoria codificaria identidade que o eixo dá de graça, e o leitor passaria a ter
 *    que consultar legenda para ler o que já está escrito ao lado da barra. A exceção é
 *    prioridade, que é ordinal e recebe rampa sequencial.
 *
 * 2. **Grade e eixos recessivos.** A informação é a barra; régua com o mesmo peso visual
 *    da barra disputa atenção com o dado.
 *
 * 3. **Tabela sempre disponível.** Gráfico é resumo, não a única via de acesso ao número.
 *    Quem usa leitor de tela, quem imprime em preto e branco, e quem só quer o valor
 *    exato precisam de uma saída que não dependa de enxergar pixels coloridos.
 */

const TOOLTIP_STYLE = {
  backgroundColor: 'var(--popover)',
  color: 'var(--popover-foreground)',
  border: '1px solid var(--border)',
  borderRadius: '0.5rem',
  fontSize: '0.8125rem',
} as const

const AXIS_STYLE = { fontSize: 12, fill: 'var(--chart-axis)' } as const

export function ChartFrame({
  title,
  data,
  valueLabel = 'Chamados',
  children,
}: {
  title: string
  data: CountByLabel[]
  valueLabel?: string
  children: React.ReactNode
}) {
  const [showTable, setShowTable] = useState(false)

  return (
    <section className="rounded-lg border p-4">
      <div className="mb-3 flex items-center justify-between gap-2">
        <h2 className="text-sm font-semibold">{title}</h2>

        <Button
          variant="ghost"
          size="sm"
          onClick={() => setShowTable((current) => !current)}
          aria-expanded={showTable}
        >
          {showTable ? 'Ver gráfico' : 'Ver dados'}
        </Button>
      </div>

      {data.length === 0 ? (
        <p className="text-muted-foreground py-8 text-center text-sm">Sem dados ainda.</p>
      ) : showTable ? (
        <table className="w-full text-sm">
          <caption className="sr-only">{title}</caption>
          <thead className="text-muted-foreground text-xs">
            <tr>
              <th scope="col" className="py-1 text-left font-medium">
                Categoria
              </th>
              <th scope="col" className="py-1 text-right font-medium">
                {valueLabel}
              </th>
            </tr>
          </thead>
          <tbody>
            {data.map((row) => (
              <tr key={row.label} className="border-t">
                <td className="py-1.5">{row.label}</td>
                <td className="py-1.5 text-right tabular-nums">{row.count}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        children
      )}
    </section>
  )
}

/** Barras verticais. Para poucas categorias com rótulo curto. */
export function VerticalBars({
  data,
  ordinal = false,
}: {
  data: CountByLabel[]
  ordinal?: boolean
}) {
  return (
    <ResponsiveContainer width="100%" height={240}>
      <BarChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: -20 }}>
        <CartesianGrid vertical={false} stroke="var(--chart-grid)" />
        <XAxis
          dataKey="label"
          tick={AXIS_STYLE}
          stroke="var(--chart-grid)"
          interval={0}
          tickLine={false}
        />
        <YAxis tick={AXIS_STYLE} stroke="var(--chart-grid)" tickLine={false} allowDecimals={false} />
        <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ fill: 'var(--muted)' }} />

        {/* Cantos arredondados só na ponta do dado; a base fica ancorada na linha zero. */}
        <Bar dataKey="count" name="Chamados" radius={[4, 4, 0, 0]} maxBarSize={48}>
          {data.map((row, index) => (
            <Cell
              key={row.label}
              fill={
                ordinal
                  ? `var(--chart-ordinal-${Math.min(index + 1, 4)})`
                  : 'var(--chart-series)'
              }
            />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

/** Barras horizontais. Para rótulo longo, como nome de categoria ou de pessoa. */
export function HorizontalBars({ data }: { data: CountByLabel[] }) {
  // A altura acompanha a quantidade de linhas: altura fixa com muitas categorias
  // comprimiria as barras até elas deixarem de ser comparáveis.
  const height = Math.max(160, data.length * 32 + 24)

  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} layout="vertical" margin={{ top: 4, right: 24, bottom: 0, left: 8 }}>
        <CartesianGrid horizontal={false} stroke="var(--chart-grid)" />
        <XAxis type="number" tick={AXIS_STYLE} stroke="var(--chart-grid)" tickLine={false} allowDecimals={false} />
        <YAxis
          type="category"
          dataKey="label"
          tick={AXIS_STYLE}
          stroke="var(--chart-grid)"
          tickLine={false}
          width={130}
        />
        <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ fill: 'var(--muted)' }} />
        <Bar
          dataKey="count"
          name="Chamados"
          fill="var(--chart-series)"
          radius={[0, 4, 4, 0]}
          maxBarSize={20}
        />
      </BarChart>
    </ResponsiveContainer>
  )
}
