/**
 * Motor da corrida: física, obstáculos e colisão, sem nada de desenho.
 *
 * Coordenadas no espaço lógico do canvas (800 × 220). A altura do jogador e dos obstáculos
 * é medida **a partir do chão, para cima**, que é como se pensa num pulo; quem desenha
 * converte para o eixo do canvas, que cresce para baixo.
 */

export const WORLD = { width: 800, height: 220, ground: 186 } as const

export const PHYSICS = {
  gravity: 2600,
  jumpVelocity: 820,
  /** Soltar o pulo cedo multiplica a gravidade na subida: toque curto, pulo baixo. */
  shortHopGravity: 2.4,
  fastFall: 2600,
  startSpeed: 330,
  maxSpeed: 780,
  acceleration: 9,
} as const

export type Size = { w: number; h: number }

export type Hitboxes = { standing: Size; ducking: Size }

export type ObstacleKind = 'cone' | 'cones' | 'ball' | 'bird'

export type Obstacle = {
  kind: ObstacleKind
  /** Borda esquerda. */
  x: number
  /** Altura da base acima do chão. */
  bottom: number
  w: number
  h: number
  /** Velocidade própria, somada à do cenário. A bola vem rolando na direção do jogador. */
  extraSpeed: number
}

export type Player = { x: number; y: number; vy: number; ducking: boolean }

export type Input = { jump: boolean; duck: boolean }

export type RunnerStatus = 'ready' | 'running' | 'over'

export type RunnerState = {
  status: RunnerStatus
  elapsed: number
  distance: number
  speed: number
  player: Player
  obstacles: Obstacle[]
  nextSpawn: number
  jumpWasHeld: boolean
  random: () => number
}

/** Folga da caixa de colisão: encostar de raspão não conta, e é isso que parece justo. */
const INSET = 5

export function createRunner(random: () => number = Math.random): RunnerState {
  return {
    status: 'ready',
    elapsed: 0,
    distance: 0,
    speed: PHYSICS.startSpeed,
    player: { x: 90, y: 0, vy: 0, ducking: false },
    obstacles: [],
    nextSpawn: 1.2,
    jumpWasHeld: false,
    random,
  }
}

export function score(state: RunnerState): number {
  return Math.floor(state.distance / 12)
}

/**
 * Avança o jogo `dt` segundos. Muda o estado no lugar (é chamado 60 vezes por segundo) e
 * o devolve, para os testes encadearem.
 */
export function step(
  state: RunnerState,
  dt: number,
  input: Input,
  hitboxes: Hitboxes,
  jumpBoost = 1,
): RunnerState {
  if (state.status !== 'running') {
    state.jumpWasHeld = input.jump
    return state
  }

  state.elapsed += dt
  state.speed = Math.min(PHYSICS.maxSpeed, PHYSICS.startSpeed + PHYSICS.acceleration * state.elapsed)
  state.distance += state.speed * dt

  const player = state.player
  const onGround = player.y <= 0

  // Pulo na borda do toque: segurar a tecla não faz pular de novo ao cair.
  if (input.jump && !state.jumpWasHeld && onGround) {
    player.vy = PHYSICS.jumpVelocity * jumpBoost
  }

  const rising = player.vy > 0
  const gravity = PHYSICS.gravity * (rising && !input.jump ? PHYSICS.shortHopGravity : 1)
  player.vy -= gravity * dt

  if (input.duck && !onGround) {
    player.vy -= PHYSICS.fastFall * dt
  }

  player.y += player.vy * dt

  if (player.y <= 0) {
    player.y = 0
    player.vy = 0
  }

  player.ducking = input.duck && player.y === 0
  state.jumpWasHeld = input.jump

  for (const obstacle of state.obstacles) {
    obstacle.x -= (state.speed + obstacle.extraSpeed) * dt
  }
  state.obstacles = state.obstacles.filter((obstacle) => obstacle.x + obstacle.w > -40)

  state.nextSpawn -= dt
  if (state.nextSpawn <= 0) {
    state.obstacles.push(spawnObstacle(state))
    state.nextSpawn = 0.75 + state.random() * 1.0
  }

  if (state.obstacles.some((obstacle) => collides(player, hitboxes, obstacle))) {
    state.status = 'over'
  }

  return state
}

/** Começa, ou recomeça depois de perder, mantendo a mesma fonte de sorteio. */
export function start(state: RunnerState): RunnerState {
  const fresh = createRunner(state.random)
  fresh.status = 'running'
  fresh.jumpWasHeld = true
  return fresh
}

export function spawnObstacle(state: RunnerState): Obstacle {
  const roll = state.random()
  const x = WORLD.width + 10
  // O quero-quero só aparece depois de um aquecimento, e vem em duas alturas: rente,
  // que pede pulo, e na altura da cabeça, que pede abaixar.
  const birdsAllowed = score(state) > 120

  if (birdsAllowed && roll < 0.25) {
    const low = state.random() < 0.4
    return { kind: 'bird', x, bottom: low ? 14 : 38, w: 34, h: 18, extraSpeed: 40 }
  }

  if (roll < 0.5) {
    return { kind: 'cone', x, bottom: 0, w: 20, h: 34, extraSpeed: 0 }
  }

  if (roll < 0.72) {
    return { kind: 'cones', x, bottom: 0, w: 42, h: 30, extraSpeed: 0 }
  }

  return { kind: 'ball', x, bottom: 0, w: 24, h: 24, extraSpeed: 70 }
}

export function playerBox(player: Player, hitboxes: Hitboxes) {
  const size = player.ducking ? hitboxes.ducking : hitboxes.standing

  return { left: player.x - size.w / 2, right: player.x + size.w / 2, bottom: player.y, top: player.y + size.h }
}

export function collides(player: Player, hitboxes: Hitboxes, obstacle: Obstacle): boolean {
  const box = playerBox(player, hitboxes)

  const horizontal =
    box.left + INSET < obstacle.x + obstacle.w - INSET && box.right - INSET > obstacle.x + INSET
  const vertical =
    box.bottom + INSET < obstacle.bottom + obstacle.h - INSET && box.top - INSET > obstacle.bottom + INSET

  return horizontal && vertical
}
