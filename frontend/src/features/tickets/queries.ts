import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as ticketsApi from '@/features/tickets/api'
import type { TicketQuery } from '@/features/tickets/types'

const keys = {
  all: ['tickets'] as const,
  list: (query: TicketQuery) => ['tickets', 'list', query] as const,
  detail: (id: string) => ['tickets', 'detail', id] as const,
  categories: ['categories'] as const,
}

export function useTickets(query: TicketQuery) {
  return useQuery({
    queryKey: keys.list(query),
    queryFn: () => ticketsApi.listTickets(query),

    // Mantém a página anterior visível enquanto a próxima carrega, em vez de piscar
    // vazio a cada troca de filtro ou de página.
    placeholderData: (previous) => previous,
  })
}

export function useTicket(id: string) {
  return useQuery({
    queryKey: keys.detail(id),
    queryFn: () => ticketsApi.getTicket(id),
  })
}

export function useCategories() {
  return useQuery({
    queryKey: keys.categories,
    queryFn: ticketsApi.listCategories,

    // Categorias mudam raramente e são dado de referência.
    staleTime: 10 * 60 * 1000,
  })
}

export function useCreateTicket() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ticketsApi.createTicket,
    onSuccess: () => {
      // A listagem tem contagem e paginação vindas do servidor; recalcular no cliente
      // daria divergência. Invalidar e deixar o servidor responder é mais simples e
      // sempre correto.
      void queryClient.invalidateQueries({ queryKey: keys.all })
    },
  })
}
