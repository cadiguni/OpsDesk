import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as inboundApi from '@/features/inbound/api'
import { AttachmentRecovery } from '@/features/inbound/attachment-recovery'
import { renderWithProviders } from '@/test/render'

vi.mock('@/features/inbound/api')

const batch = (overrides: Partial<inboundApi.AttachmentRecoveryBatch>): inboundApi.AttachmentRecoveryBatch => ({
  examined: 0,
  recovered: 0,
  attachments: 0,
  notFound: 0,
  remaining: 0,
  cursor: null,
  error: null,
  ...overrides,
})

beforeEach(() => {
  vi.mocked(inboundApi.getPendingAttachmentRecovery).mockReset()
  vi.mocked(inboundApi.runAttachmentRecovery).mockReset()
})

describe('recuperação dos anexos de e-mails antigos', () => {
  it('não aparece quando não há nada a recuperar', async () => {
    vi.mocked(inboundApi.getPendingAttachmentRecovery).mockResolvedValue(0)

    renderWithProviders(<AttachmentRecovery credentialsReady />, { role: 'Manager' })

    await waitFor(() => expect(inboundApi.getPendingAttachmentRecovery).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: /trazer anexos/i })).not.toBeInTheDocument()
  })

  it('encadeia os lotes pelo cursor até acabar e soma o resultado', async () => {
    const user = userEvent.setup()
    vi.mocked(inboundApi.getPendingAttachmentRecovery).mockResolvedValue(30)
    vi.mocked(inboundApi.runAttachmentRecovery)
      .mockResolvedValueOnce(batch({ examined: 20, recovered: 5, attachments: 8, remaining: 10, cursor: 'c1' }))
      .mockResolvedValueOnce(batch({ examined: 10, recovered: 2, attachments: 3, notFound: 1 }))

    renderWithProviders(<AttachmentRecovery credentialsReady />, { role: 'Manager' })

    await user.click(await screen.findByRole('button', { name: /trazer anexos/i }))

    expect(await screen.findByText(/verificadas 30 de 30: 11 anexos trazidos em 7 mensagens, 1 não encontradas/i)).toBeInTheDocument()
    expect(vi.mocked(inboundApi.runAttachmentRecovery).mock.calls.map(([after]) => after)).toEqual([null, 'c1'])
  })

  it('para no erro do Microsoft Graph e mostra a mensagem', async () => {
    const user = userEvent.setup()
    vi.mocked(inboundApi.getPendingAttachmentRecovery).mockResolvedValue(30)
    vi.mocked(inboundApi.runAttachmentRecovery).mockResolvedValueOnce(
      batch({ examined: 3, remaining: 27, cursor: 'c1', error: 'Acesso negado à caixa.' }),
    )

    renderWithProviders(<AttachmentRecovery credentialsReady />, { role: 'Manager' })

    await user.click(await screen.findByRole('button', { name: /trazer anexos/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Acesso negado à caixa.')
    expect(inboundApi.runAttachmentRecovery).toHaveBeenCalledTimes(1)
  })
})
