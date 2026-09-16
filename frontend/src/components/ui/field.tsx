import type { ReactNode } from 'react'
import { useId } from 'react'

import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

/**
 * Rótulo, controle e mensagem de erro amarrados.
 *
 * O `id` é gerado aqui e distribuído para os três, junto com `aria-invalid` e
 * `aria-describedby`. Fazer isso à mão em cada formulário é onde a acessibilidade se
 * perde: basta um campo em que o `htmlFor` não bate com o `id` para o rótulo deixar de
 * funcionar, e nada na tela indica o problema.
 */
export function Field({
  label,
  error,
  hint,
  children,
  className,
}: {
  label: string
  error?: string
  hint?: string
  children: (props: {
    id: string
    'aria-invalid': boolean
    'aria-describedby': string | undefined
  }) => ReactNode
  className?: string
}) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`

  const describedBy = [error ? errorId : null, hint ? hintId : null]
    .filter(Boolean)
    .join(' ')

  return (
    <div className={cn('grid gap-2', className)}>
      <Label htmlFor={id}>{label}</Label>

      {children({
        id,
        'aria-invalid': Boolean(error),
        'aria-describedby': describedBy || undefined,
      })}

      {hint && !error && (
        <p id={hintId} className="text-muted-foreground text-xs">
          {hint}
        </p>
      )}

      {error && (
        <p id={errorId} role="alert" className="text-destructive text-xs">
          {error}
        </p>
      )}
    </div>
  )
}
