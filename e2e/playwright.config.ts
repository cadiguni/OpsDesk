import { defineConfig, devices } from '@playwright/test'

/**
 * Testes de ponta a ponta contra o sistema de verdade: SPA compilado, nginx, API e banco,
 * como sobem no perfil `web` do compose de desenvolvimento.
 *
 *   docker compose --profile web up -d --build
 *   cd e2e && npm ci && npx playwright install chromium && npm test
 *
 * Usam os usuários do seed de desenvolvimento (CLAUDE.md, "Usuários de desenvolvimento"),
 * e cada teste cria os próprios chamados com título único: rodam contra um banco que já
 * tem dados, e não dependem de ordem.
 */
export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.OPSDESK_URL ?? 'http://localhost:3000',
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
