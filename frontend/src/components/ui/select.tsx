import type { ComponentProps } from 'react'

import { cn } from '@/lib/utils'

/**
 * Select nativo.
 *
 * Nativo de propósito nesta etapa: funciona com teclado, com leitor de tela e no celular
 * sem nenhum código nosso. Se a tela precisar de busca dentro do seletor ou de opção com
 * várias linhas, aí vale a versão do shadcn/ui com Radix.
 */
export function Select({ className, ...props }: ComponentProps<'select'>) {
  return (
    <select
      className={cn(
        'border-input bg-background flex h-9 w-full rounded-md border px-3 py-1 text-sm shadow-xs',
        'focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] focus-visible:outline-none',
        'disabled:cursor-not-allowed disabled:opacity-50',
        'aria-invalid:border-destructive aria-invalid:ring-destructive/20',
        className,
      )}
      {...props}
    />
  )
}
