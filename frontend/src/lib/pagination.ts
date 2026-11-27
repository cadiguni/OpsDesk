/** Tamanhos oferecidos. O servidor limita a 100 (`PageRequest.MaxPageSize`). */
export const pageSizeOptions = [25, 50, 100] as const

/**
 * Páginas a exibir: a primeira, a última e a atual com uma vizinha de cada lado. Um buraco
 * de uma página só é mostrado como o número, e não como reticências, que ocupariam o
 * mesmo espaço sem dizer nada.
 */
export function pageWindow(page: number, totalPages: number): (number | 'gap')[] {
  const shown = new Set([1, totalPages, page - 1, page, page + 1])
  const pages = [...shown].filter((item) => item >= 1 && item <= totalPages).sort((a, b) => a - b)

  const result: (number | 'gap')[] = []

  for (const item of pages) {
    const previous = result.at(-1)

    if (typeof previous === 'number' && item - previous === 2) {
      result.push(item - 1)
    } else if (typeof previous === 'number' && item - previous > 2) {
      result.push('gap')
    }

    result.push(item)
  }

  return result
}
