import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import type { UserRole } from '@/domain/enums'
import * as usersApi from '@/features/users/api'

const keys = {
  lists: ['admin-users', 'list'] as const,
  list: (query: usersApi.ManagedUserQuery) => ['admin-users', 'list', query] as const,
}

/** `enabled` evita o 403 garantido quando quem abre a tela não é gestor. */
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

export function useChangeRole() {
  const invalidate = useInvalidateUsers()

  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: UserRole }) =>
      usersApi.changeRole(userId, role),
    onSettled: invalidate,
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
