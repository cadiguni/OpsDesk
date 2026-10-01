import { expect, test } from '@playwright/test'

import { newSession, openTicket, uniqueTitle, users } from './session'

/**
 * O caminho principal do produto, ponta a ponta: o solicitante abre, a equipe responde —
 * em público e em nota interna —, e o solicitante vê a resposta, o aviso no sino, e nunca
 * a nota interna.
 */
test('solicitante abre, técnico responde, solicitante vê a resposta e não vê a nota interna', async ({ browser }) => {
  const title = uniqueTitle('VPN não conecta')

  const requester = await newSession(browser, users.requester)
  const ticketUrl = await openTicket(requester.page, title, 'VPN')

  const technician = await newSession(browser, users.technician)
  await technician.page.goto(ticketUrl)
  await expect(technician.page.getByRole('heading', { name: title })).toBeVisible()

  await technician.page.getByRole('button', { name: /nota interna/i }).click()
  await technician.page.getByLabel('Novo comentário').fill('Mesmo erro do servidor de certificados de ontem.')
  await technician.page.getByRole('button', { name: 'Enviar', exact: true }).click()
  await expect(technician.page.getByText('Mesmo erro do servidor de certificados de ontem.')).toBeVisible()

  await technician.page.getByRole('button', { name: /resposta pública/i }).click()
  await technician.page.getByLabel('Novo comentário').fill('Renovei o certificado. Pode testar de novo?')
  await technician.page.getByRole('button', { name: 'Enviar', exact: true }).click()
  await expect(technician.page.getByText('Renovei o certificado. Pode testar de novo?')).toBeVisible()

  await requester.page.reload()
  await expect(requester.page.getByText('Renovei o certificado. Pode testar de novo?')).toBeVisible()
  await expect(requester.page.getByText('Mesmo erro do servidor de certificados de ontem.')).toHaveCount(0)
  await expect(requester.page.getByRole('link', { name: /Notificações, \d+ não lida/ })).toBeVisible()

  await requester.context.close()
  await technician.context.close()
})

test('solicitante não abre chamado de outra pessoa pelo endereço', async ({ browser }) => {
  const title = uniqueTitle('Impressora')

  const technician = await newSession(browser, users.technician)
  const ticketUrl = await openTicket(technician.page, title, 'Impressora')

  const requester = await newSession(browser, users.requester)
  await requester.page.goto(ticketUrl)
  await expect(requester.page.getByRole('heading', { name: title })).toHaveCount(0)

  await requester.context.close()
  await technician.context.close()
})
