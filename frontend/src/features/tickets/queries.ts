import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as ticketsApi from '@/features/tickets/api'
import type { TicketQuery } from '@/features/tickets/types'
import type { TicketStatus } from '@/domain/enums'

const keys = {
  all: ['tickets'] as const,
  // `lists` é o prefixo de todas as listagens. Invalidar com `list({})` não
  // funcionaria: a correspondência é por prefixo, e o objeto de filtros vazio não é
  // prefixo de um objeto com filtros preenchidos.
  lists: ['tickets', 'list'] as const,
  list: (query: TicketQuery) => ['tickets', 'list', query] as const,
  detail: (id: string) => ['tickets', 'detail', id] as const,
  categories: ['categories'] as const,
  comments: (id: string) => ['tickets', 'comments', id] as const,
  history: (id: string) => ['tickets', 'history', id] as const,
  staff: ['staff'] as const,
  dashboard: ['dashboard'] as const,
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

export function useComments(ticketId: string) {
  return useQuery({
    queryKey: keys.comments(ticketId),
    queryFn: () => ticketsApi.listComments(ticketId),
  })
}

export function useHistory(ticketId: string) {
  return useQuery({
    queryKey: keys.history(ticketId),
    queryFn: () => ticketsApi.listHistory(ticketId),
  })
}

export function useStaff(enabled: boolean) {
  return useQuery({
    queryKey: keys.staff,
    queryFn: ticketsApi.listStaff,

    // Só técnico e gestor podem chamar /api/staff. Sem isto, a tela do solicitante
    // dispararia uma requisição que volta 403 e sujaria o console sem motivo.
    enabled,
    staleTime: 10 * 60 * 1000,
  })
}

export function useDashboard() {
  return useQuery({
    queryKey: keys.dashboard,
    queryFn: ticketsApi.getDashboard,
  })
}

export function useAddComment(ticketId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: { content: string; isInternal: boolean }) =>
      ticketsApi.addComment(ticketId, input),
    onSuccess: () => {
      // O comentário público da equipe pode ter encerrado o SLA de resposta e gerou
      // histórico, então o detalhe e o histórico também saem do cache.
      void queryClient.invalidateQueries({ queryKey: keys.comments(ticketId) })
      void queryClient.invalidateQueries({ queryKey: keys.history(ticketId) })
      void queryClient.invalidateQueries({ queryKey: keys.detail(ticketId) })
    },
  })
}

export function useChangeStatus(ticketId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (status: TicketStatus) => ticketsApi.changeStatus(ticketId, status),
    onSuccess: (ticket) => {
      // A resposta já é o chamado atualizado: aproveitamos em vez de refazer a consulta.
      queryClient.setQueryData(keys.detail(ticketId), ticket)
      void queryClient.invalidateQueries({ queryKey: keys.history(ticketId) })
      void queryClient.invalidateQueries({ queryKey: keys.lists })
      void queryClient.invalidateQueries({ queryKey: keys.dashboard })
    },
  })
}

export function useAssign(ticketId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (technicianId: string | null) => ticketsApi.assign(ticketId, technicianId),
    onSuccess: (ticket) => {
      queryClient.setQueryData(keys.detail(ticketId), ticket)
      void queryClient.invalidateQueries({ queryKey: keys.history(ticketId) })
      void queryClient.invalidateQueries({ queryKey: keys.lists })
      void queryClient.invalidateQueries({ queryKey: keys.dashboard })
    },
  })
}
