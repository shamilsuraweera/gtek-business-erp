import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api/client'
import { useAuth } from './auth'

/** GET a company-scoped resource. The company id is part of the key so switching company refetches. */
export function useApiQuery<T>(path: string, options: { enabled?: boolean } = {}) {
  const { company } = useAuth()
  return useQuery({
    queryKey: [company?.id ?? 'none', path],
    queryFn: () => api<T>(path),
    enabled: !!company && (options.enabled ?? true),
  })
}

/** Mutations invalidate everything: the data set is small and cross-module effects (stock, ledger, AR) are wide. */
export function useApiMutation<TBody = unknown, TResult = unknown>(
  build: (body: TBody) => { path: string; method?: string; body?: unknown },
  onSuccess?: (result: TResult) => void,
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: TBody) => {
      const { path, method = 'POST', body } = build(input)
      return api<TResult>(path, { method, body })
    },
    onSuccess: async (result) => {
      await queryClient.invalidateQueries()
      onSuccess?.(result)
    },
  })
}
