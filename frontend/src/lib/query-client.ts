import { QueryClient } from '@tanstack/react-query'

/**
 * Cache de servidor. Não há biblioteca de estado global no projeto: o que o TanStack
 * Query guarda é cache, e o pouco de estado realmente local cabe em `useState` e na URL.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Chamado muda por ação humana, não a cada segundo. Meio minuto evita refetch
      // a cada navegação sem deixar a tela desatualizada de forma perceptível.
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})
