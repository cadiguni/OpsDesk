import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'

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

export const REFRESH_PATH = '/api/auth/refresh'

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

/**
 * Uma renovação por vez.
 *
 * Sem isto, uma tela que dispara quatro consultas em paralelo com o token expirado
 * abriria quatro renovações simultâneas. Como o refresh token é de uso único e rotaciona,
 * a primeira invalidaria as outras três — e o backend interpretaria as tentativas
 * seguintes como reuso de token, derrubando todas as sessões do usuário. O bug apareceria
 * como logout aleatório ao abrir uma tela pesada.
 */
let inFlightRefresh: Promise<string | null> | null = null

async function refreshAccessToken(): Promise<string | null> {
  inFlightRefresh ??= (async () => {
    try {
      const { data } = await api.post<{ accessToken: string }>(REFRESH_PATH, null, {
        // Evita recursão: a própria renovação não passa pelo tratamento de 401.
        skipAuthRefresh: true,
      } as InternalAxiosRequestConfig)

      setAccessToken(data.accessToken)

      return data.accessToken
    } catch {
      setAccessToken(null)

      return null
    } finally {
      inFlightRefresh = null
    }
  })()

  return inFlightRefresh
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const request = error.config as (InternalAxiosRequestConfig & AuthRetryFlags) | undefined

    const shouldRefresh =
      error.response?.status === 401 &&
      request !== undefined &&
      !request.skipAuthRefresh &&
      !request.isAuthRetry

    if (!shouldRefresh) {
      return Promise.reject(error)
    }

    const token = await refreshAccessToken()

    if (!token) {
      onSessionLost?.()

      return Promise.reject(error)
    }

    // Uma única repetição, marcada para não entrar em laço se o 401 persistir.
    request.isAuthRetry = true
    request.headers.Authorization = `Bearer ${token}`

    return api.request(request)
  },
)

type AuthRetryFlags = {
  skipAuthRefresh?: boolean
  isAuthRetry?: boolean
}

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
