import axios from 'axios'

/**
 * Cliente HTTP da API.
 *
 * `withCredentials` é obrigatório: o refresh token vive em cookie `httpOnly` e o
 * navegador só o envia entre origens diferentes com isso ligado. O token de acesso
 * continua indo no cabeçalho `Authorization`, nunca em `localStorage` — qualquer XSS
 * leria de lá.
 */
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? 'http://localhost:8080',
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
})

let accessToken: string | null = null

/**
 * Guarda o token de acesso em memória do módulo. Recarregar a página perde o token
 * de propósito: a sessão é reconstruída pelo refresh token do cookie.
 */
export function setAccessToken(token: string | null): void {
  accessToken = token
}

api.interceptors.request.use((config) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`
  }

  return config
})

/** Mensagem de erro legível a partir de uma resposta `ProblemDetails` da API. */
export function errorMessage(error: unknown, fallback = 'Não foi possível concluir a operação.'): string {
  if (!axios.isAxiosError(error)) {
    return fallback
  }

  const data = error.response?.data as { detail?: string; title?: string } | undefined

  return data?.detail ?? data?.title ?? error.message ?? fallback
}
