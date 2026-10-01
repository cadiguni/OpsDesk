import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import * as holidaysApi from '@/features/holidays/api'

const keys = {
  all: ['holidays'] as const,
  year: (year: number) => ['holidays', year] as const,
}

export function useHolidays(year: number, enabled: boolean) {
  return useQuery({ queryKey: keys.year(year), queryFn: () => holidaysApi.listHolidays(year), enabled })
}

function useInvalidate() {
  const queryClient = useQueryClient()

  return () => void queryClient.invalidateQueries({ queryKey: keys.all })
}

export function useAddHoliday() {
  const invalidate = useInvalidate()

  return useMutation({ mutationFn: holidaysApi.addHoliday, onSuccess: invalidate })
}

export function useChangeHolidayActivation() {
  const invalidate = useInvalidate()

  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      holidaysApi.changeHolidayActivation(id, isActive),
    onSettled: invalidate,
  })
}

export function useDeleteHoliday() {
  const invalidate = useInvalidate()

  return useMutation({ mutationFn: holidaysApi.deleteHoliday, onSettled: invalidate })
}
