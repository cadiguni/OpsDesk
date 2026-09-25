import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as notificationsApi from '@/features/notifications/api'

const keys = {
  all: ['notifications'] as const,
  unread: ['notifications', 'unread'] as const,
  list: (query: { unreadOnly?: boolean; page?: number }) => ['notifications', 'list', query] as const,
}

/**
 * Contador do sino. Consulta a cada minuto, e só com a aba visível: notificação é aviso,
 * não chat, e um minuto de atraso não muda o que a pessoa faz. Tempo real exigiria
 * conexão aberta com o servidor, que é infraestrutura para outro momento.
 */
export function useUnreadCount(enabled: boolean) {
  return useQuery({
    queryKey: keys.unread,
    queryFn: notificationsApi.countUnread,
    enabled,
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
  })
}

export function useNotifications(query: { unreadOnly?: boolean; page?: number }) {
  return useQuery({
    queryKey: keys.list(query),
    queryFn: () => notificationsApi.listNotifications(query),
    placeholderData: (previous) => previous,
  })
}

export function useMarkRead() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: notificationsApi.markRead,
    onSettled: () => void queryClient.invalidateQueries({ queryKey: keys.all }),
  })
}

export function useMarkAllRead() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: notificationsApi.markAllRead,
    onSettled: () => void queryClient.invalidateQueries({ queryKey: keys.all }),
  })
}
