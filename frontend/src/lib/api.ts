import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'

import type { UserRole } from '@/domain/enums'

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

const REFRESH_PATH = '/api/auth/refresh'

let accessToken: string | null = null
let onSessionLost: (() => void) | null = null

/**
 * Guarda o token de acesso em memória do módulo. Recarregar a página perde o token
 * de propósito: a sessão é reconstruída pelo refresh token do cookie.
 */
export function setAccessToken(token: string | null): void {
  accessToken = token
}

export function getAccessToken(): string | null {
  return accessToken
}

/** Chamado quando a renovação falha e não há mais sessão para recuperar. */
export function setSessionLostHandler(handler: (() => void) | null): void {
  onSessionLost = handler
}

api.interceptors.request.use((config) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`
  }

  return config
})

export type RefreshedSession = {
  accessToken: string
  expiresAt: string
  user: {
    id: string
    name: string
    email: string
    role: UserRole
    mustChangePassword: boolean
  }
}

/**
 * Uma renovação por vez, em todo o aplicativo.
 *
 * Isto não é otimização, é correção. O refresh token é de uso único e rotaciona a cada
 * uso, então duas renovações concorrentes com o mesmo cookie fazem a segunda apresentar um
 * token já rotacionado — e o servidor, fora da janela de tolerância, trata isso como
 * vazamento e derruba todas as sessões do usuário.
 *
 * As duas formas de isso acontecer sozinho:
 *
 * - o `StrictMode` do React executa o efeito duas vezes em desenvolvimento, então a
 *   restauração de sessão dispara duas vezes por carregamento de página;
 * - uma tela que faz várias consultas em paralelo com o token expirado recebe vários 401
 *   ao mesmo tempo.
 *
 * Ambas passam por aqui, e por isso só a primeira chamada vai à rede: as outras aguardam
 * a mesma promessa. O sintoma que isso evita é logout aparentemente aleatório.
 */
let inFlightRefresh: Promise<RefreshedSession | null> | null = null

export function refreshSession(): Promise<RefreshedSession | null> {
  inFlightRefresh ??= (async () => {
    try {
      const { data } = await api.post<RefreshedSession>(REFRESH_PATH, null, {
        // Evita recursão: o 401 da própria renovação não dispara outra renovação.
        skipAuthRefresh: true,
      })

      setAccessToken(data.accessToken)

      return data
    } catch {
      setAccessToken(null)

      return null
    } finally {
      // Liberado apenas depois de a promessa resolver, para que quem chegar durante a
      // renovação reaproveite o resultado em vez de abrir uma segunda.
      inFlightRefresh = null
    }
  })()

  return inFlightRefresh
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const request = error.config as InternalAxiosRequestConfig | undefined

    const shouldRefresh =
      error.response?.status === 401 &&
      request !== undefined &&
      !request.skipAuthRefresh &&
      !request.isAuthRetry

    if (!shouldRefresh) {
      return Promise.reject(error)
    }

    const refreshed = await refreshSession()

    if (!refreshed) {
      onSessionLost?.()

      return Promise.reject(error)
    }

    // Uma única repetição, marcada para não entrar em laço se o 401 persistir.
    request.isAuthRetry = true
    request.headers.Authorization = `Bearer ${refreshed.accessToken}`

    return api.request(request)
  },
)

declare module 'axios' {
  export interface AxiosRequestConfig {
    /** Não tentar renovar o token quando esta requisição receber 401. */
    skipAuthRefresh?: boolean

    /** Marca interna: esta requisição já é a repetição após uma renovação. */
    isAuthRetry?: boolean
  }
}

/** Mensagem de erro legível a partir de uma resposta `ProblemDetails` da API. */
export function errorMessage(
  error: unknown,
  fallback = 'Não foi possível concluir a operação.',
): string {
  if (!axios.isAxiosError(error)) {
    return fallback
  }

  if (error.response?.status === 429) {
    return 'Muitas tentativas. Aguarde um minuto e tente novamente.'
  }

  const data = error.response?.data as ProblemDetails | undefined

  // Erro de validação do backend: juntamos as mensagens dos campos, que são mais
  // específicas que o título genérico.
  if (data?.errors) {
    const messages = Object.values(data.errors).flat()

    if (messages.length > 0) {
      return messages.join(' ')
    }
  }

  return data?.detail ?? data?.title ?? error.message ?? fallback
}

type ProblemDetails = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}
