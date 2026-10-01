import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as inboundApi from '@/features/inbound/api'

const keys = {
  all: ['inbound'] as const,
  list: (query: object) => ['inbound', 'list', query] as const,
  detail: (id: string) => ['inbound', 'detail', id] as const,
  rules: ['inbound', 'rules'] as const,
  accounts: ['email-accounts'] as const,
}

export function useInboundList(query: { status?: inboundApi.InboundEmailStatus; search?: string; page?: number }) {
  return useQuery({
    queryKey: keys.list(query),
    queryFn: () => inboundApi.listInbound(query),
    placeholderData: (previous) => previous,
  })
}

export function useInboundDetail(id: string | null) {
  return useQuery({
    queryKey: keys.detail(id ?? ''),
    queryFn: () => inboundApi.getInbound(id!),
    enabled: id !== null,
  })
}

/**
 * Revisar, reprocessar ou excluir muda a lista, as regras e — quando a mensagem vira
 * chamado — a fila de chamados.
 */
function useInvalidateInbound() {
  const queryClient = useQueryClient()

  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.all })
    void queryClient.invalidateQueries({ queryKey: ['tickets'] })
  }
}

export function useReviewInbound() {
  const invalidate = useInvalidateInbound()

  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: inboundApi.SpamReviewAction }) => inboundApi.reviewInbound(id, action),
    onSettled: invalidate,
  })
}

export function useReprocessInbound() {
  const invalidate = useInvalidateInbound()

  return useMutation({ mutationFn: inboundApi.reprocessInbound, onSettled: invalidate })
}

export function useDeleteInbound() {
  const invalidate = useInvalidateInbound()

  return useMutation({ mutationFn: inboundApi.deleteInbound, onSettled: invalidate })
}

export function useSpamRules(enabled: boolean) {
  return useQuery({ queryKey: keys.rules, queryFn: inboundApi.listSpamRules, enabled })
}

export function useSetSpamRuleActive() {
  const invalidate = useInvalidateInbound()

  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => inboundApi.setSpamRuleActive(id, isActive),
    onSettled: invalidate,
  })
}

export function useEmailAccounts(enabled: boolean) {
  return useQuery({ queryKey: keys.accounts, queryFn: inboundApi.listEmailAccounts, enabled })
}

export function useSaveEmailAccount() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, input }: { id: string | null; input: inboundApi.SaveEmailAccountInput }) =>
      inboundApi.saveEmailAccount(id, input),
    onSettled: () => void queryClient.invalidateQueries({ queryKey: keys.accounts }),
  })
}
