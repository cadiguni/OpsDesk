import type { ComponentProps } from 'react'

import { cn } from '@/lib/utils'

export function Input({ className, type, ...props }: ComponentProps<'input'>) {
  return (
    <input
      type={type}
      className={cn(
        'border-input bg-background flex h-9 w-full rounded-md border px-3 py-1 text-sm shadow-xs transition-colors',
        'placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] focus-visible:outline-none',
        'disabled:cursor-not-allowed disabled:opacity-50',
        // O estado de erro vem do aria-invalid, não de uma classe própria: assim o
        // leitor de tela e a borda vermelha nunca saem de sincronia.
        'aria-invalid:border-destructive aria-invalid:ring-destructive/20',
        className,
      )}
      {...props}
    />
  )
}
