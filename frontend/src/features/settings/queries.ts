import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as settingsApi from '@/features/settings/api'

const keys = {
  email: ['settings', 'email'] as const,
  general: ['settings', 'general'] as const,
}

export function useGeneralSettings(enabled: boolean) {
  return useQuery({ queryKey: keys.general, queryFn: settingsApi.getGeneralSettings, enabled })
}

export function useUpdateGeneralSettings() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: settingsApi.updateGeneralSettings,
    onSuccess: (saved) => {
      queryClient.setQueryData(keys.general, saved)
      // O nome da organização e a política aparecem na tela de login e no menu.
      void queryClient.invalidateQueries({ queryKey: ['auth-options'] })
    },
  })
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
