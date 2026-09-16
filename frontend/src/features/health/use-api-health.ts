import { useQuery } from '@tanstack/react-query'

import { api } from '@/lib/api'

export type ApiHealth = {
  status: string
}

/**
 * Consulta o health check da API. Serve para a tela inicial provar que a SPA, a API e
 * o PostgreSQL estão conversando — o endpoint verifica a conexão com o banco.
 */
export function useApiHealth() {
  return useQuery({
    queryKey: ['api-health'],
    queryFn: async () => {
      const { data } = await api.get<ApiHealth | string>('/health')

      return typeof data === 'string' ? { status: data } : data
    },
    staleTime: 0,
    retry: false,
  })
}
