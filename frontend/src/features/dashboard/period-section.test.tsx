import { screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { PeriodSection } from '@/features/dashboard/period-section'
import * as ticketsApi from '@/features/tickets/api'
import type { PeriodIndicators } from '@/features/tickets/types'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/tickets/api')

function indicators(overrides: Partial<PeriodIndicators>): PeriodIndicators {
  return {
    opened: 10,
    resolved: 8,
    responseMet: 8,
    responseMissed: 2,
    responseCompliance: 80,
    resolutionMet: 6,
    resolutionMissed: 2,
    resolutionCompliance: 75,
    reopened: 1,
    reopenRate: 12.5,
    ...overrides,
  }
}

/** O tile cujo rótulo é este texto. */
async function tile(label: string) {
  return (await screen.findByText(label)).parentElement as HTMLElement
}

describe('desempenho no período', () => {
  it('diz se a variação melhorou ou piorou conforme o sentido do indicador', async () => {
    vi.mocked(ticketsApi.getDashboardPeriod).mockResolvedValue({
      from: '2026-09-01T03:00:00Z',
      before: '2026-10-01T03:00:00Z',
      current: indicators({ responseCompliance: 90, reopenRate: 20 }),
      previous: indicators({ responseCompliance: 80, reopenRate: 10 }),
      byTechnician: [],
    })

    renderWithProviders(<PeriodSection />, { role: 'Manager', route: '/dashboard' })

    // Cumprimento subiu: melhorou. Reabertura subiu: piorou.
    expect(within(await tile('Resposta no prazo')).getByText(/melhorou/)).toBeInTheDocument()
    expect(within(await tile('Reabertura')).getByText(/piorou/)).toBeInTheDocument()
    expect(within(await tile('Resposta no prazo')).getByText(/\+10 p\.p\./)).toBeInTheDocument()
  })

  it('sem prazo decidido mostra vazio, e não zero por cento', async () => {
    vi.mocked(ticketsApi.getDashboardPeriod).mockResolvedValue({
      from: '2026-09-01T03:00:00Z',
      before: '2026-10-01T03:00:00Z',
      current: indicators({ resolutionCompliance: null }),
      previous: indicators({ resolutionCompliance: null }),
      byTechnician: [],
    })

    renderWithProviders(<PeriodSection />, { role: 'Manager', route: '/dashboard' })

    const resolution = await tile('Resolução no prazo')
    expect(within(resolution).getByText('—')).toBeInTheDocument()
    expect(within(resolution).getByText('Sem base de comparação')).toBeInTheDocument()
    expect(within(resolution).queryByText('0%')).not.toBeInTheDocument()
  })

  it('pede o período em dias de São Paulo, com o fim no começo de amanhã', async () => {
    vi.mocked(ticketsApi.getDashboardPeriod).mockResolvedValue({
      from: '', before: '', current: indicators({}), previous: indicators({}), byTechnician: [],
    })

    renderWithProviders(<PeriodSection />, { role: 'Manager', route: '/dashboard?periodo=7' })

    await tile('Abertos')
    const [from, before] = vi.mocked(ticketsApi.getDashboardPeriod).mock.lastCall!
    expect(from).toMatch(/T00:00:00-03:00$/)
    expect(before).toMatch(/T00:00:00-03:00$/)
    expect((new Date(before).getTime() - new Date(from).getTime()) / 86_400_000).toBe(7)
  })
})
