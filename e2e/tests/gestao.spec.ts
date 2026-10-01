import { expect, test } from '@playwright/test'

import { signIn, users } from './session'

test('gestor vê o dashboard com os indicadores do período', async ({ page }) => {
  await signIn(page, users.manager)

  await page.getByRole('link', { name: 'Dashboard' }).click()
  await expect(page.getByRole('heading', { name: 'Desempenho no período' })).toBeVisible()
  await expect(page.getByText('Resposta no prazo')).toBeVisible()

  await page.getByRole('button', { name: '7 dias' }).click()
  await expect(page).toHaveURL(/periodo=7/)

  // Registro visual para revisão humana: o teste não compara pixels.
  //
  // Janela alta e captura sem `fullPage`, de propósito. A captura de página inteira
  // redimensiona a janela, os gráficos se redesenham e reiniciam a animação de entrada, e
  // a foto sai com as barras em zero — parecendo defeito do gráfico sem ser.
  await page.setViewportSize({ width: 1280, height: 1400 })
  await page.waitForTimeout(2000)
  await page.screenshot({ path: 'test-results/dashboard.png' })
})

test('técnico não vê o menu de administração', async ({ page }) => {
  await signIn(page, users.technician)

  await expect(page.getByRole('link', { name: 'Chamados' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Usuários' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Dashboard' })).toHaveCount(0)
})
