import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as jobTitlesApi from '@/features/job-titles/api'

const keys = {
  all: ['admin-job-titles'] as const,
  list: (query: jobTitlesApi.ManagedJobTitleQuery) => ['admin-job-titles', 'list', query] as const,
  options: ['admin-job-titles', 'options'] as const,
}

/** `enabled` evita o 403 garantido quando quem abre a tela não é administrador. */
export function useManagedJobTitles(query: jobTitlesApi.ManagedJobTitleQuery, enabled: boolean) {
  return useQuery({
    queryKey: keys.list(query),
    queryFn: () => jobTitlesApi.listManagedJobTitles(query),
    enabled,
    placeholderData: (previous) => previous,
  })
}

export function useJobTitleOptions(enabled: boolean) {
  return useQuery({ queryKey: keys.options, queryFn: jobTitlesApi.listJobTitleOptions, enabled })
}

/** Renomear muda o nome exibido na lista de usuários, que também é invalidada. */
function useInvalidateJobTitles() {
  const queryClient = useQueryClient()

  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.all })
    void queryClient.invalidateQueries({ queryKey: ['admin-users'] })
  }
}

export function useCreateJobTitle() {
  const invalidate = useInvalidateJobTitles()

  return useMutation({ mutationFn: jobTitlesApi.createJobTitle, onSuccess: invalidate })
}

export function useRenameJobTitle() {
  const invalidate = useInvalidateJobTitles()

  return useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) => jobTitlesApi.renameJobTitle(id, name),
    onSuccess: invalidate,
  })
}

export function useChangeJobTitleActivation() {
  const invalidate = useInvalidateJobTitles()

  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      jobTitlesApi.changeJobTitleActivation(id, isActive),
    onSettled: invalidate,
  })
}
