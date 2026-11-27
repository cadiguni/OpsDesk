import { describe, expect, it } from 'vitest'

import { characterById } from './characters'
import { collides, createRunner, PHYSICS, start, step, type Obstacle } from './engine'

const anteater = characterById('tamandua').hitboxes
const idle = { jump: false, duck: false }

function running() {
  // Sorteio fixo bem alto: nenhum obstáculo nasce durante o teste sem a gente pedir.
  const state = start(createRunner(() => 0.99))
  state.nextSpawn = 999
  return state
}

function advance(state: ReturnType<typeof running>, seconds: number, input = idle) {
  for (let t = 0; t < seconds; t += 1 / 60) step(state, 1 / 60, input, anteater)
  return state
}

const obstacleAt = (x: number, overrides: Partial<Obstacle> = {}): Obstacle => ({
  kind: 'cone',
  x,
  bottom: 0,
  w: 20,
  h: 34,
  extraSpeed: 0,
  ...overrides,
})

describe('corrida', () => {
  it('pula na borda do toque, cai de volta e não pula de novo com a tecla presa', () => {
    const state = running()
    state.jumpWasHeld = false

    step(state, 1 / 60, { jump: true, duck: false }, anteater)
    expect(state.player.vy).toBeGreaterThan(0)

    advance(state, 1.5, { jump: true, duck: false })
    expect(state.player.y).toBe(0)
    expect(state.player.vy).toBe(0)
  })

  it('toque curto pula mais baixo que segurar', () => {
    const peak = (held: boolean) => {
      const state = running()
      state.jumpWasHeld = false
      step(state, 1 / 60, { jump: true, duck: false }, anteater)

      let highest = 0
      for (let frame = 0; frame < 90; frame++) {
        step(state, 1 / 60, { jump: held, duck: false }, anteater)
        highest = Math.max(highest, state.player.y)
      }
      return highest
    }

    expect(peak(false)).toBeLessThan(peak(true) * 0.7)
  })

  it('encostar no cone acaba o jogo, e pular por cima não', () => {
    const hit = running()
    hit.obstacles.push(obstacleAt(hit.player.x - 10))
    step(hit, 1 / 60, idle, anteater)
    expect(hit.status).toBe('over')

    const player = { x: 90, y: 60, vy: 0, ducking: false }
    expect(collides(player, anteater, obstacleAt(80))).toBe(false)
  })

  it('o quero-quero alto pede abaixar; o rente, pular', () => {
    const high = obstacleAt(80, { kind: 'bird', bottom: 38, w: 34, h: 18 })
    const low = obstacleAt(80, { kind: 'bird', bottom: 14, w: 34, h: 18 })
    const standing = { x: 90, y: 0, vy: 0, ducking: false }
    const ducking = { ...standing, ducking: true }

    expect(collides(standing, anteater, high)).toBe(true)
    expect(collides(ducking, anteater, high)).toBe(false)
    expect(collides(ducking, anteater, low)).toBe(true)
    expect(collides({ ...standing, y: 40 }, anteater, low)).toBe(false)
  })

  it('acelera com o tempo até o teto', () => {
    const state = advance(running(), 3)
    expect(state.speed).toBeGreaterThan(PHYSICS.startSpeed)

    advance(state, 120)
    expect(state.speed).toBe(PHYSICS.maxSpeed)
  })

  it('parado, não anda nem pontua', () => {
    const state = createRunner()
    step(state, 1, idle, anteater)

    expect(state.distance).toBe(0)
    expect(state.status).toBe('ready')
  })
})
