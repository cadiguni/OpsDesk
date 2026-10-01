import { expect, type Browser, type Page } from '@playwright/test'

/** Senha dos usuários do seed de desenvolvimento. */
export const DevPassword = 'OpsDesk@123'

export const users = {
  requester: 'usuario@opsdesk.local',
  technician: 'tecnico@opsdesk.local',
  manager: 'gestor@opsdesk.local',
} as const

/** Entra pela tela de login, como uma pessoa faria. */
export async function signIn(page: Page, email: string) {
  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(DevPassword)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL(/\/chamados/)
}

/** Sessão separada, com cookies próprios: duas pessoas, dois navegadores. */
export async function newSession(browser: Browser, email: string) {
  const context = await browser.newContext()
  const page = await context.newPage()
  await signIn(page, email)

  return { context, page }
}

/** Abre um chamado pela tela, e devolve o endereço dele. */
export async function openTicket(page: Page, title: string, category: string) {
  await page.getByRole('link', { name: 'Abrir chamado' }).first().click()
  await page.getByLabel('Título').fill(title)
  await page.getByLabel('Descrição').fill('Descrição escrita pelo teste de ponta a ponta, com detalhe suficiente.')
  await page.getByLabel('Categoria').selectOption({ label: category })
  await page.getByRole('button', { name: 'Abrir chamado' }).click()
  await expect(page.getByRole('heading', { name: title })).toBeVisible()

  return page.url()
}

export function uniqueTitle(prefix: string) {
  return `${prefix} ${Date.now().toString(36)}`
}
