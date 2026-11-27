import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AxiosError, AxiosHeaders } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as arquibancadaApi from '@/features/arquibancada/api'
import type { FootballTeam, StandingRow } from '@/features/arquibancada/api'
import { Arquibancada } from '@/routes/arquibancada'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/arquibancada/api', () => ({
  getStandings: vi.fn(),
  getTeamMatches: vi.fn(),
}))

const gremio: FootballTeam = { id: 1767, name: 'Grêmio FBPA', shortName: 'Grêmio', tla: 'GRE' }
const flamengo: FootballTeam = { id: 1783, name: 'CR Flamengo', shortName: 'Flamengo', tla: 'FLA' }

function row(position: number, team: FootballTeam, points: number): StandingRow {
  return {
    position,
    team,
    played: 36,
    won: 20,
    draw: 6,
    lost: 10,
    goalsFor: 50,
    goalsAgainst: 30,
    goalDifference: 20,
    points,
    form: 'W,D,L',
  }
}

beforeEach(() => {
  localStorage.clear()
  vi.mocked(arquibancadaApi.getStandings).mockResolvedValue({
    competition: 'Campeonato Brasileiro Série A',
    season: '2026',
    currentMatchday: 36,
    table: [row(1, flamengo, 70), row(2, gremio, 68)],
  })
  vi.mocked(arquibancadaApi.getTeamMatches).mockImplementation(async (teamId) => ({
    team: teamId === gremio.id ? gremio : flamengo,
    recent: [
      {
        id: 1,
        utcDate: '2026-11-20T22:00:00Z',
        status: 'FINISHED',
        matchday: 35,
        home: gremio,
        away: flamengo,
        homeGoals: 2,
        awayGoals: 1,
      },
    ],
    upcoming: [],
  }))
})

describe('arquibancada', () => {
  it('mostra a tabela e abre nos jogos do Grêmio quando nada foi escolhido', async () => {
    renderWithProviders(<Arquibancada />, { role: 'Requester', route: '/arquibancada' })

    expect(await screen.findByText('Campeonato Brasileiro Série A 2026')).toBeInTheDocument()
    await waitFor(() => expect(arquibancadaApi.getTeamMatches).toHaveBeenCalledWith(gremio.id))

    const recent = await screen.findByRole('heading', { name: 'Últimos jogos' })
    expect(within(recent.parentElement!).getByLabelText('Vitória')).toBeInTheDocument()
  })

  it('trocar de time busca os jogos dele e lembra da escolha', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Arquibancada />, { role: 'Requester', route: '/arquibancada' })

    await user.selectOptions(await screen.findByLabelText('Time'), 'Flamengo')

    await waitFor(() => expect(arquibancadaApi.getTeamMatches).toHaveBeenCalledWith(flamengo.id))
    expect(localStorage.getItem('arquibancada.time')).toBe(String(flamengo.id))
  })

  it('sem chave configurada, explica em vez de quebrar', async () => {
    vi.mocked(arquibancadaApi.getStandings).mockRejectedValue(
      new AxiosError('503', 'ERR_BAD_RESPONSE', undefined, undefined, {
        status: 503,
        statusText: 'Service Unavailable',
        headers: {},
        config: { headers: new AxiosHeaders() },
        data: { title: 'Arquibancada sem dados configurados', detail: 'Defina Football__ApiKey.' },
      }),
    )

    renderWithProviders(<Arquibancada />, { role: 'Requester', route: '/arquibancada' })

    expect(await screen.findByRole('alert')).toHaveTextContent('Defina Football__ApiKey.')
  })
})
