import { useState, type FormEvent, type ReactNode } from 'react'
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { pageSizeOptions, pageWindow } from '@/lib/pagination'

type PaginationProps = {
  page: number
  totalPages: number
  totalCount: number
  onPageChange: (page: number) => void
  /** Rótulo do total, no singular e no plural: `['chamado', 'chamados']`. */
  noun: [string, string]
  /** Só as listas que deixam escolher o tamanho da página passam estes dois. */
  pageSize?: number
  onPageSizeChange?: (pageSize: number) => void
}

/**
 * Navegação entre páginas de uma listagem paginada no servidor.
 *
 * Mostra a primeira, a última e as vizinhas da atual, com reticências no meio, e um campo
 * para ir direto a uma página: com dezenas de páginas, só "próxima" obriga a clicar dezenas
 * de vezes.
 */
export function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
  noun,
  pageSize,
  onPageSizeChange,
}: PaginationProps) {
  const [target, setTarget] = useState('')

  if (totalCount === 0) {
    return null
  }

  const last = Math.max(totalPages, 1)

  function go(next: number) {
    const clamped = Math.min(Math.max(Math.trunc(next), 1), last)

    if (clamped !== page) {
      onPageChange(clamped)
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    const value = Number(target)

    if (Number.isFinite(value) && target.trim() !== '') {
      go(value)
    }

    setTarget('')
  }

  return (
    <nav aria-label="Paginação" className="flex flex-wrap items-center justify-between gap-3 text-sm">
      <div className="text-muted-foreground flex flex-wrap items-center gap-3">
        <p>
          {totalCount.toLocaleString('pt-BR')} {totalCount === 1 ? noun[0] : noun[1]}
          {totalPages > 1 && ` · página ${page} de ${totalPages}`}
        </p>

        {pageSize !== undefined && onPageSizeChange && totalCount > pageSizeOptions[0] && (
          <label className="flex items-center gap-2">
            <Select
              aria-label="Itens por página"
              className="h-8 w-auto"
              value={String(pageSize)}
              onChange={(event) => onPageSizeChange(Number(event.target.value))}
            >
              {pageSizeOptions.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </Select>
            por página
          </label>
        )}
      </div>

      {totalPages > 1 && (
        <div className="flex flex-wrap items-center gap-1">
          <IconButton label="Primeira página" disabled={page <= 1} onClick={() => go(1)}>
            <ChevronsLeft />
          </IconButton>
          <IconButton label="Página anterior" disabled={page <= 1} onClick={() => go(page - 1)}>
            <ChevronLeft />
          </IconButton>

          {pageWindow(page, totalPages).map((item, index) =>
            item === 'gap' ? (
              <span key={`gap-${index}`} className="text-muted-foreground px-1" aria-hidden>
                …
              </span>
            ) : (
              <Button
                key={item}
                variant={item === page ? 'default' : 'outline'}
                size="sm"
                className="min-w-8 px-2"
                aria-label={`Página ${item}`}
                aria-current={item === page ? 'page' : undefined}
                onClick={() => go(item)}
              >
                {item}
              </Button>
            ),
          )}

          <IconButton label="Próxima página" disabled={page >= totalPages} onClick={() => go(page + 1)}>
            <ChevronRight />
          </IconButton>
          <IconButton label="Última página" disabled={page >= totalPages} onClick={() => go(totalPages)}>
            <ChevronsRight />
          </IconButton>

          {totalPages > 7 && (
            <form onSubmit={submit} className="ml-2 flex items-center gap-1">
              <Input
                type="number"
                inputMode="numeric"
                aria-label="Ir para a página"
                placeholder="Pág."
                className="h-8 w-16"
                value={target}
                onChange={(event) => setTarget(event.target.value)}
              />
              <Button type="submit" variant="outline" size="sm">
                Ir
              </Button>
            </form>
          )}
        </div>
      )}
    </nav>
  )
}

function IconButton({
  label,
  disabled,
  onClick,
  children,
}: {
  label: string
  disabled: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <Button
      variant="outline"
      size="sm"
      className="size-8 p-0"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
    >
      {children}
    </Button>
  )
}
