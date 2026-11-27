import { api } from '@/lib/api'

export type FootballTeam = { id: number; name: string; shortName: string; tla: string }

export type StandingRow = {
  position: number
  team: FootballTeam
  played: number
  won: number
  draw: number
  lost: number
  goalsFor: number
  goalsAgainst: number
  goalDifference: number
  points: number
  /** Últimos resultados, do mais antigo ao mais recente: "W,D,L,W,W". Pode faltar. */
  form: string | null
}

export type Standings = {
  competition: string
  season: string | null
  currentMatchday: number | null
  table: StandingRow[]
}

export type FootballMatch = {
  id: number
  utcDate: string
  status: string
  matchday: number | null
  home: FootballTeam
  away: FootballTeam
  homeGoals: number | null
  awayGoals: number | null
}

export type TeamMatches = { team: FootballTeam; recent: FootballMatch[]; upcoming: FootballMatch[] }

export async function getStandings(): Promise<Standings> {
  const { data } = await api.get<Standings>('/api/arquibancada/standings')

  return data
}

export async function getTeamMatches(teamId: number): Promise<TeamMatches> {
  const { data } = await api.get<TeamMatches>(`/api/arquibancada/teams/${teamId}/matches`)

  return data
}
