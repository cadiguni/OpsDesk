import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Select } from '@/components/ui/select'
import * as arquibancadaApi from '@/features/arquibancada/api'
import type { FootballMatch, StandingRow } from '@/features/arquibancada/api'
import { RunnerGame } from '@/features/arquibancada/runner/runner-game'
import { errorMessage } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

const TEAM_KEY = 'arquibancada.time'

function readTeam(): number | null {
  try {
    return Number(localStorage.getItem(TEAM_KEY)) || null
  } catch {
    return null
  }
}

function saveTeam(teamId: number) {
  try {
    localStorage.setItem(TEAM_KEY, String(teamId))
  } catch {
    // Sem armazenamento, o time volta ao padrão no próximo acesso.
  }
}

/**
 * A arquibancada: descontração escondida, sem link em menu nenhum. Chega-se aqui pela URL
 * ou pelo atalho do logotipo (sete cliques seguidos).
 */
export function Arquibancada() {
  const [params, setParams] = useSearchParams()
  const tab = params.get('aba') === 'corrida' ? 'corrida' : 'tabela'

  function choose(next: 'tabela' | 'corrida') {
    setParams(next === 'tabela' ? {} : { aba: next }, { replace: true })
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Arquibancada</h1>
        <p className="text-muted-foreground text-sm">Intervalo do expediente. Fica entre nós.</p>
      </div>

      <div role="tablist" aria-label="Arquibancada" className="flex gap-2 border-b">
        {(
          [
            ['tabela', 'Brasileirão'],
            ['corrida', 'Corrida'],
          ] as const
        ).map(([value, label]) => (
          <button
            key={value}
            type="button"
            role="tab"
            aria-selected={tab === value}
            onClick={() => choose(value)}
            className={cn(
              '-mb-px border-b-2 px-3 py-2 text-sm font-medium transition-colors',
              tab === value ? 'border-primary text-foreground' : 'text-muted-foreground hover:text-foreground border-transparent',
            )}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'tabela' ? <Brasileirao /> : <RunnerGame />}
    </div>
  )
}

function Brasileirao() {
  const standings = useQuery({
    queryKey: ['arquibancada', 'standings'],
    queryFn: arquibancadaApi.getStandings,
    staleTime: 5 * 60_000,
  })
  const [chosen, setChosen] = useState<number | null>(readTeam)

  if (standings.isPending) {
    return <p className="text-muted-foreground text-sm">Carregando a tabela…</p>
  }

  if (standings.isError) {
    return (
      <p role="alert" className="text-muted-foreground text-sm">
        {errorMessage(standings.error, 'Não foi possível carregar a tabela agora.')}
      </p>
    )
  }

  const table = standings.data.table
  // Sem escolha guardada, o padrão da casa é o Grêmio.
  const teamId =
    (chosen && table.some((row) => row.team.id === chosen) ? chosen : null) ??
    table.find((row) => /gr[eê]mio/i.test(row.team.name))?.team.id ??
    table[0]?.team.id ??
    null

  function pick(id: number) {
    setChosen(id)
    saveTeam(id)
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
      <Card>
        <CardHeader>
          <CardTitle>
            {standings.data.competition}
            {standings.data.season && ` ${standings.data.season}`}
          </CardTitle>
          {standings.data.currentMatchday && (
            <p className="text-muted-foreground text-sm">Rodada {standings.data.currentMatchday}</p>
          )}
        </CardHeader>
        <CardContent className="overflow-x-auto">
          <StandingsTable rows={table} selected={teamId} onSelect={pick} />
          <p className="text-muted-foreground mt-3 flex flex-wrap gap-4 text-xs">
            <span className="flex items-center gap-1.5"><span className="size-2.5 rounded-full bg-sky-500" /> G4</span>
            <span className="flex items-center gap-1.5"><span className="size-2.5 rounded-full bg-sky-300" /> Pré-Libertadores</span>
            <span className="flex items-center gap-1.5"><span className="size-2.5 rounded-full bg-red-500" /> Z4</span>
          </p>
        </CardContent>
      </Card>

      {teamId !== null && <TeamCard teamId={teamId} rows={table} onSelect={pick} />}
    </div>
  )
}

function zone(position: number, total: number): string {
  if (position <= 4) return 'bg-sky-500'
  if (position <= 6) return 'bg-sky-300'
  if (position > total - 4) return 'bg-red-500'
  return 'bg-transparent'
}

function StandingsTable({
  rows,
  selected,
  onSelect,
}: {
  rows: StandingRow[]
  selected: number | null
  onSelect: (teamId: number) => void
}) {
  return (
    <table className="w-full text-sm">
      <thead className="text-muted-foreground text-xs">
        <tr className="border-b">
          <th className="py-2 pr-2 text-left font-medium">#</th>
          <th className="py-2 pr-2 text-left font-medium">Time</th>
          <th className="px-2 py-2 text-right font-medium" title="Pontos">P</th>
          <th className="px-2 py-2 text-right font-medium" title="Jogos">J</th>
          <th className="hidden px-2 py-2 text-right font-medium sm:table-cell" title="Vitórias">V</th>
          <th className="hidden px-2 py-2 text-right font-medium sm:table-cell" title="Empates">E</th>
          <th className="hidden px-2 py-2 text-right font-medium sm:table-cell" title="Derrotas">D</th>
          <th className="px-2 py-2 text-right font-medium" title="Saldo de gols">SG</th>
          <th className="hidden py-2 pl-2 text-left font-medium md:table-cell">Últimos</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr
            key={row.team.id}
            aria-selected={row.team.id === selected}
            onClick={() => onSelect(row.team.id)}
            className={cn(
              'hover:bg-muted/60 cursor-pointer border-b last:border-0',
              row.team.id === selected && 'bg-primary/10 font-semibold',
            )}
          >
            <td className="py-2 pr-2">
              <span className="flex items-center gap-2 tabular-nums">
                <span className={cn('h-4 w-1 rounded-full', zone(row.position, rows.length))} />
                {row.position}
              </span>
            </td>
            <td className="py-2 pr-2">{row.team.shortName}</td>
            <td className="px-2 py-2 text-right font-semibold tabular-nums">{row.points}</td>
            <td className="px-2 py-2 text-right tabular-nums">{row.played}</td>
            <td className="hidden px-2 py-2 text-right tabular-nums sm:table-cell">{row.won}</td>
            <td className="hidden px-2 py-2 text-right tabular-nums sm:table-cell">{row.draw}</td>
            <td className="hidden px-2 py-2 text-right tabular-nums sm:table-cell">{row.lost}</td>
            <td className="px-2 py-2 text-right tabular-nums">
              {row.goalDifference > 0 ? `+${row.goalDifference}` : row.goalDifference}
            </td>
            <td className="hidden py-2 pl-2 md:table-cell">
              <Form value={row.form} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

const RESULT = {
  W: { label: 'V', className: 'bg-emerald-600 text-white' },
  D: { label: 'E', className: 'bg-zinc-400 text-white' },
  L: { label: 'D', className: 'bg-red-600 text-white' },
} as const

function Form({ value }: { value: string | null }) {
  if (!value) return null

  return (
    <span className="flex gap-1">
      {value
        .split(',')
        .filter((result): result is keyof typeof RESULT => result in RESULT)
        .map((result, index) => (
          <span
            key={index}
            className={cn('grid size-5 place-items-center rounded text-[10px] font-bold', RESULT[result].className)}
          >
            {RESULT[result].label}
          </span>
        ))}
    </span>
  )
}

function TeamCard({
  teamId,
  rows,
  onSelect,
}: {
  teamId: number
  rows: StandingRow[]
  onSelect: (teamId: number) => void
}) {
  const matches = useQuery({
    queryKey: ['arquibancada', 'team', teamId],
    queryFn: () => arquibancadaApi.getTeamMatches(teamId),
    staleTime: 5 * 60_000,
  })
  const sorted = [...rows].sort((a, b) => a.team.shortName.localeCompare(b.team.shortName, 'pt-BR'))

  return (
    <Card>
      <CardHeader className="space-y-3">
        <CardTitle>Jogos do time</CardTitle>
        <Select
          aria-label="Time"
          value={teamId}
          onChange={(event) => onSelect(Number(event.target.value))}
        >
          {sorted.map((row) => (
            <option key={row.team.id} value={row.team.id}>
              {row.team.shortName}
            </option>
          ))}
        </Select>
      </CardHeader>
      <CardContent className="space-y-6">
        {matches.isPending && <p className="text-muted-foreground text-sm">Carregando os jogos…</p>}
        {matches.isError && (
          <p role="alert" className="text-muted-foreground text-sm">
            {errorMessage(matches.error, 'Não foi possível carregar os jogos agora.')}
          </p>
        )}
        {matches.data && (
          <>
            <MatchList title="Próximos jogos" empty="Nenhum jogo marcado." matches={matches.data.upcoming} teamId={teamId} />
            <MatchList title="Últimos jogos" empty="Nenhum jogo encerrado ainda." matches={matches.data.recent} teamId={teamId} />
          </>
        )}
      </CardContent>
    </Card>
  )
}

const LIVE = new Set(['IN_PLAY', 'PAUSED'])

function outcome(match: FootballMatch, teamId: number): keyof typeof RESULT | null {
  if (match.homeGoals === null || match.awayGoals === null) return null

  const own = match.home.id === teamId ? match.homeGoals : match.awayGoals
  const other = match.home.id === teamId ? match.awayGoals : match.homeGoals

  return own > other ? 'W' : own < other ? 'L' : 'D'
}

function MatchList({
  title,
  empty,
  matches,
  teamId,
}: {
  title: string
  empty: string
  matches: FootballMatch[]
  teamId: number
}) {
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold">{title}</h3>
      {matches.length === 0 ? (
        <p className="text-muted-foreground text-sm">{empty}</p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {matches.map((match) => {
            const result = match.status === 'FINISHED' ? outcome(match, teamId) : null
            const finished = match.homeGoals !== null && match.awayGoals !== null

            return (
              <li key={match.id} className="flex items-center gap-3 px-3 py-2 text-sm">
                <span className="text-muted-foreground w-28 shrink-0 text-xs">
                  {formatDateTime(match.utcDate)}
                  {match.matchday && <span className="block">Rodada {match.matchday}</span>}
                </span>
                <span className="min-w-0 flex-1">
                  <span className={cn(match.home.id === teamId && 'font-semibold')}>{match.home.shortName}</span>
                  <span className="text-muted-foreground px-1.5 tabular-nums">
                    {finished ? `${match.homeGoals} × ${match.awayGoals}` : '×'}
                  </span>
                  <span className={cn(match.away.id === teamId && 'font-semibold')}>{match.away.shortName}</span>
                </span>
                {LIVE.has(match.status) && <Badge variant="destructive">Ao vivo</Badge>}
                {match.status === 'POSTPONED' && <Badge variant="outline">Adiado</Badge>}
                {result && (
                  <span
                    className={cn('grid size-6 place-items-center rounded text-xs font-bold', RESULT[result].className)}
                    aria-label={result === 'W' ? 'Vitória' : result === 'L' ? 'Derrota' : 'Empate'}
                  >
                    {RESULT[result].label}
                  </span>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
