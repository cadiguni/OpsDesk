import { describe, expect, it } from 'vitest'

import { pageWindow } from '@/lib/pagination'

describe('pageWindow', () => {
  it('mostra todas quando são poucas', () => {
    expect(pageWindow(1, 1)).toEqual([1])
    expect(pageWindow(2, 4)).toEqual([1, 2, 3, 4])
  })

  it('mostra a primeira, a última e as vizinhas da atual, com reticências no meio', () => {
    expect(pageWindow(50, 98)).toEqual([1, 'gap', 49, 50, 51, 'gap', 98])
  })

  it('não troca uma página só por reticências', () => {
    expect(pageWindow(4, 98)).toEqual([1, 2, 3, 4, 5, 'gap', 98])
    expect(pageWindow(1, 98)).toEqual([1, 2, 'gap', 98])
    expect(pageWindow(98, 98)).toEqual([1, 'gap', 97, 98])
  })
})
