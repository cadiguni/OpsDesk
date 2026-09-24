import { describe, expect, it } from 'vitest'

import { nextDay, startOfDayInSaoPaulo } from '@/lib/format'

describe('startOfDayInSaoPaulo', () => {
  it('começa o dia à meia-noite de São Paulo, não de Greenwich', () => {
    expect(startOfDayInSaoPaulo('2026-09-10')).toBe('2026-09-10T00:00:00-03:00')
  })

  it('é o mesmo instante que 03:00 UTC', () => {
    expect(new Date(startOfDayInSaoPaulo('2026-09-10')).toISOString()).toBe(
      '2026-09-10T03:00:00.000Z',
    )
  })
})

describe('nextDay', () => {
  it.each([
    ['2026-09-10', '2026-09-11'],
    ['2026-09-30', '2026-10-01'],
    ['2026-12-31', '2027-01-01'],
    ['2028-02-28', '2028-02-29'],
  ])('%s → %s', (day, expected) => {
    expect(nextDay(day)).toBe(expected)
  })
})
