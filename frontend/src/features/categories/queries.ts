import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as categoriesApi from '@/features/categories/api'

const keys = {
  lists: ['admin-categories', 'list'] as const,
  list: (query: categoriesApi.ManagedCategoryQuery) => ['admin-categories', 'list', query] as const,
}

/** `enabled` evita o 403 garantido quando quem abre a tela não é gestor. */
export function useManagedCategories(query: categoriesApi.ManagedCategoryQuery, enabled: boolean) {
  return useQuery({
    queryKey: keys.list(query),
    queryFn: () => categoriesApi.listManagedCategories(query),
    enabled,
    placeholderData: (previous) => previous,
  })
}

/**
 * Invalida a listagem, os seletores de categoria e os chamados: renomear muda o nome
 * exibido em chamado que já está em cache, e desativar tira a opção dos seletores.
 */
function useInvalidateCategories() {
  const queryClient = useQueryClient()

  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.lists })
    void queryClient.invalidateQueries({ queryKey: ['categories'] })
    void queryClient.invalidateQueries({ queryKey: ['tickets'] })
  }
}

export function useCreateCategory() {
  const invalidate = useInvalidateCategories()

  return useMutation({ mutationFn: categoriesApi.createCategory, onSuccess: invalidate })
}

export function useUpdateCategory() {
  const invalidate = useInvalidateCategories()

  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: categoriesApi.SaveCategoryInput }) =>
      categoriesApi.updateCategory(id, input),
    onSuccess: invalidate,
  })
}

export function useChangeCategoryActivation() {
  const invalidate = useInvalidateCategories()

  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      categoriesApi.changeCategoryActivation(id, isActive),
    onSettled: invalidate,
  })
}
