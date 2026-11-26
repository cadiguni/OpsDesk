import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import type { UserRole } from '@/domain/enums'
import * as usersApi from '@/features/users/api'

const keys = {
  lists: ['admin-users', 'list'] as const,
  list: (query: usersApi.ManagedUserQuery) => ['admin-users', 'list', query] as const,
}

/** `enabled` evita o 403 garantido quando quem abre a tela não é administrador. */
export function useManagedUsers(query: usersApi.ManagedUserQuery, enabled: boolean) {
  return useQuery({
    queryKey: keys.list(query),
    queryFn: () => usersApi.listManagedUsers(query),
    enabled,
    placeholderData: (previous) => previous,
  })
}

/**
 * Invalida a listagem e também os seletores da equipe: promover ou desativar muda quem
 * aparece em "responsável" e em "abrir em nome de".
 */
function useInvalidateUsers() {
  const queryClient = useQueryClient()

  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.lists })
    void queryClient.invalidateQueries({ queryKey: ['staff'] })
    void queryClient.invalidateQueries({ queryKey: ['users'] })
  }
}

export function useCreateUser() {
  const invalidate = useInvalidateUsers()

  return useMutation({ mutationFn: usersApi.createUser, onSuccess: invalidate })
}

export function useResetUserPassword() {
  const invalidate = useInvalidateUsers()

  return useMutation({ mutationFn: usersApi.resetUserPassword, onSettled: invalidate })
}

/** Trilha de auditoria, só quando a linha é aberta. */
export function useUserHistory(userId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['admin-users', 'history', userId],
    queryFn: () => usersApi.getUserHistory(userId),
    enabled,
  })
}

export function useChangeRole() {
  const invalidate = useInvalidateUsers()

  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: UserRole }) =>
      usersApi.changeRole(userId, role),
    onSettled: invalidate,
  })
}

export function useChangeJobTitle() {
  const invalidate = useInvalidateUsers()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ userId, jobTitleId }: { userId: string; jobTitleId: string | null }) =>
      usersApi.changeJobTitle(userId, jobTitleId),
    onSettled: () => {
      invalidate()
      // A contagem de pessoas por cargo muda junto.
      void queryClient.invalidateQueries({ queryKey: ['admin-job-titles'] })
    },
  })
}

export function useChangeActivation() {
  const invalidate = useInvalidateUsers()

  return useMutation({
    mutationFn: ({ userId, isActive }: { userId: string; isActive: boolean }) =>
      usersApi.changeActivation(userId, isActive),
    onSettled: invalidate,
  })
}
