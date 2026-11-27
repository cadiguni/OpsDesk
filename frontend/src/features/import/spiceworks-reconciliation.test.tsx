import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as importApi from '@/features/import/api'
import type { SpiceworksReconciliationReport } from '@/features/import/api'
import { SpiceworksReconciliation } from '@/features/import/spiceworks-reconciliation'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/import/api', async (original) => ({
  ...(await original<typeof importApi>()),
  reconcileSpiceworks: vi.fn(),
}))

function report(overrides: Partial<SpiceworksReconciliationReport> = {}): SpiceworksReconciliationReport {
  return {
    dryRun: true,
    tickets: 2384,
    withoutMessageId: 58,
    matched: 2300,
    notFound: 26,
    datesFixed: 2290,
    closed: 2270,
    stillOpen: 28,
    openOnlyHere: 1,
    notes: [
      { spiceworksNumber: 2356, code: null, message: 'aberto no Spiceworks e sem chamado correspondente no OpsDesk.' },
      { spiceworksNumber: null, code: 'OPS-002435', message: 'em andamento no OpsDesk e ausente da exportação.' },
    ],
    ...overrides,
  }
}

const file = new File(['{"tickets":[]}'], 'ticket_export.json', { type: 'application/json' })

beforeEach(() => {
  vi.mocked(importApi.reconcileSpiceworks).mockReset()
})

async function check() {
  const user = userEvent.setup()

  renderWithProviders(<SpiceworksReconciliation />, { role: 'Manager', route: '/importacao' })

  await user.upload(screen.getByLabelText('Exportação do Spiceworks (JSON)'), file)
  await user.click(screen.getByRole('button', { name: 'Conferir conciliação' }))

  return user
}

describe('conciliação com o Spiceworks', () => {
  it('confere sem gravar, mostra o que fica para conferir à mão e só então aplica', async () => {
    vi.mocked(importApi.reconcileSpiceworks)
      .mockResolvedValueOnce(report())
      .mockResolvedValueOnce(report({ dryRun: false }))

    const user = await check()

    expect(vi.mocked(importApi.reconcileSpiceworks).mock.lastCall?.[0]).toEqual({ file, dryRun: true })
    expect(await screen.findByText('2.270')).toBeInTheDocument()
    expect(screen.getByText(/#2356/)).toBeInTheDocument()
    // O chamado que só existe aqui leva à busca por ele na lista.
    expect(screen.getByRole('link', { name: 'OPS-002435' })).toHaveAttribute('href', '/chamados?search=OPS-002435')
    expect(screen.getByText(/1 chamado de e-mail em andamento no OpsDesk não aparece/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Aplicar conciliação' }))

    expect(vi.mocked(importApi.reconcileSpiceworks).mock.lastCall?.[0]).toEqual({ file, dryRun: false })
    expect(await screen.findByRole('status')).toHaveTextContent('2.290 datas corrigidas e 2.270 chamados fechados')
  })

  it('não oferece aplicar quando já está tudo conciliado', async () => {
    vi.mocked(importApi.reconcileSpiceworks).mockResolvedValueOnce(report({ datesFixed: 0, closed: 0, notes: [] }))

    await check()

    expect(await screen.findByText('Nada a corrigir: os chamados já estão conciliados.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Aplicar conciliação' })).not.toBeInTheDocument()
  })
})
