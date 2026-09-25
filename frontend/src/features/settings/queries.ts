import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as settingsApi from '@/features/settings/api'

const keys = {
  email: ['settings', 'email'] as const,
}

export function useEmailSettings(enabled: boolean) {
  return useQuery({ queryKey: keys.email, queryFn: settingsApi.getEmailSettings, enabled })
}

export function useUpdateEmailSettings() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: settingsApi.updateEmailSettings,
    onSuccess: (saved) => queryClient.setQueryData(keys.email, saved),
  })
}

export function useSendTestEmail() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: settingsApi.sendTestEmail,
    // Sucesso ou falha, o resultado vai para "último envio" na tela.
    onSettled: () => void queryClient.invalidateQueries({ queryKey: keys.email }),
  })
}
