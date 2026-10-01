import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as importApi from '@/features/import/api'
import type { ImportReport } from '@/features/import/api'
import { TicketImport } from '@/routes/ticket-import'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/import/api', async (original) => ({
  ...(await original<typeof importApi>()),
  importTickets: vi.fn(),
}))

function report(overrides: Partial<ImportReport> = {}): ImportReport {
  return {
    dryRun: true,
    rows: 3,
    valid: 2,
    created: 0,
    alreadyImported: 0,
    newRequesters: 1,
    recognizedColumns: ['title', 'email'],
    missingColumns: [],
    issues: [{ line: 4, message: 'Prioridade desconhecida: "Altíssima".' }],
    ...overrides,
  }
}

const file = new File(['titulo;solicitante_email\nVPN;ana@empresa.com\n'], 'chamados.csv', { type: 'text/csv' })

beforeEach(() => {
  vi.mocked(importApi.importTickets).mockReset()
})

async function chooseAndCheck(format?: string) {
  const user = userEvent.setup()

  renderWithProviders(<TicketImport />, { role: 'Manager', route: '/importacao' })

  if (format) {
    await user.selectOptions(screen.getByLabelText('Origem'), format)
  }

  await user.upload(screen.getByLabelText('Arquivo'), file)
  await user.click(screen.getByRole('button', { name: /conferir/i }))

  return user
}

describe('importação de chamados', () => {
  it('confere antes, mostra a linha com problema e só então importa', async () => {
    vi.mocked(importApi.importTickets)
      .mockResolvedValueOnce(report())
      .mockResolvedValueOnce(report({ dryRun: false, created: 2 }))

    const user = await chooseAndCheck('Freshdesk')

    expect(importApi.importTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ file, format: 'Freshdesk', dryRun: true }),
      expect.anything(),
    )
    expect(await screen.findByText(/linha 4/i)).toBeInTheDocument()
    expect(screen.getByText(/1 linha será pulada/i)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Importar 2 chamados' }))

    expect(importApi.importTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ format: 'Freshdesk', dryRun: false }),
      expect.anything(),
    )
    expect(await screen.findByText('2 chamados importados.')).toBeInTheDocument()
  })

  it('sem coluna obrigatória, diz qual falta e não oferece importar', async () => {
    vi.mocked(importApi.importTickets).mockResolvedValueOnce(
      report({ valid: 0, newRequesters: 0, missingColumns: ['solicitante_email'], issues: [] }),
    )

    await chooseAndCheck()

    expect(await screen.findByRole('alert')).toHaveTextContent('solicitante_email')
    expect(screen.queryByRole('button', { name: /^importar/i })).not.toBeInTheDocument()
  })

  it('trocar de arquivo descarta a conferência anterior', async () => {
    vi.mocked(importApi.importTickets).mockResolvedValueOnce(report())

    const user = await chooseAndCheck()
    expect(await screen.findByRole('region', { name: 'Conferência' })).toBeInTheDocument()

    await user.upload(screen.getByLabelText('Arquivo'), new File(['x'], 'outro.csv', { type: 'text/csv' }))

    expect(screen.queryByRole('region', { name: 'Conferência' })).not.toBeInTheDocument()
  })

  it('técnico não importa', () => {
    renderWithProviders(<TicketImport />, { role: 'Technician', route: '/importacao' })

    expect(screen.getByText(/apenas gestores/i)).toBeInTheDocument()
    expect(screen.queryByLabelText('Arquivo')).not.toBeInTheDocument()
  })
})
