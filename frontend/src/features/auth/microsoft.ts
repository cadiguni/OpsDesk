import { api } from '@/lib/api'

/**
 * Endereço que inicia o login com a conta Microsoft. É navegação, não chamada do axios: o
 * navegador vai ao Entra ID e volta pela API, que abre a sessão no cookie de refresh e o
 * devolve a `returnTo`. A base resolve contra a página porque, no contêiner, `VITE_API_URL`
 * é `/` — concatenar daria `//api/...`, que o navegador lê como outro host.
 */
export function microsoftSignInUrl(returnTo: string): string {
  const url = new URL('/api/auth/microsoft/start', apiOrigin())
  url.searchParams.set('returnTo', returnTo)

  return url.toString()
}

/** O endereço de retorno a registrar no aplicativo do Entra ID, como a API o calcula. */
export function microsoftCallbackUrl(): string {
  return new URL('/api/auth/microsoft/callback', apiOrigin()).toString()
}

function apiOrigin(): string {
  return new URL(api.defaults.baseURL ?? '/', window.location.origin).origin
}

/** O motivo que a API manda em `/entrar?microsoft=`, na língua de quem está na tela. */
export function microsoftSignInError(reason: string | null): string | null {
  switch (reason) {
    case null:
      return null
    case 'cancelado':
      return 'O login com a Microsoft foi cancelado.'
    case 'expirou':
      return 'O login com a Microsoft demorou demais ou foi aberto em outra aba. Tente de novo.'
    case 'indisponivel':
      return 'O login com a Microsoft não está disponível. Entre com e-mail e senha.'
    case 'desativada':
      return 'Sua conta está desativada. Fale com a equipe de TI.'
    case 'conflito':
      return 'Este e-mail já está ligado a outra conta Microsoft. Fale com a equipe de TI.'
    default:
      return 'Não foi possível entrar com a Microsoft. Tente de novo ou fale com a equipe de TI.'
  }
}
