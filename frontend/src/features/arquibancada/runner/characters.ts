import type { Hitboxes } from './engine'

/**
 * Personagens da corrida, desenhados com formas do canvas — sem imagem, porque a CSP só
 * aceita imagem da própria origem e um sprite seria mais um arquivo para manter.
 *
 * Para criar um personagem novo: um `draw` que desenhe a partir do ponto dos pés, de frente
 * para a direita, nas três poses (em pé, abaixado, no ar), e caixas de colisão que caibam
 * no desenho. Caixa menor que o desenho é generosidade; maior é injustiça.
 */
export type Pose = {
  /** Centro horizontal do personagem. */
  x: number
  /** Linha dos pés no canvas (o canvas cresce para baixo). */
  feet: number
  ducking: boolean
  airborne: boolean
  /** Segundos de jogo, para a animação das pernas. */
  time: number
}

export type Character = {
  id: string
  name: string
  tagline: string
  hitboxes: Hitboxes
  /** Multiplica a força do pulo. */
  jumpBoost: number
  draw: (ctx: CanvasRenderingContext2D, pose: Pose) => void
}

function line(
  ctx: CanvasRenderingContext2D,
  x1: number,
  y1: number,
  x2: number,
  y2: number,
  width: number,
  color: string,
) {
  ctx.strokeStyle = color
  ctx.lineWidth = width
  ctx.lineCap = 'round'
  ctx.beginPath()
  ctx.moveTo(x1, y1)
  ctx.lineTo(x2, y2)
  ctx.stroke()
}

function ellipse(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  rx: number,
  ry: number,
  rotation: number,
  color: string,
) {
  ctx.fillStyle = color
  ctx.beginPath()
  ctx.ellipse(cx, cy, rx, ry, rotation, 0, Math.PI * 2)
  ctx.fill()
}

function polygon(ctx: CanvasRenderingContext2D, points: [number, number][], color: string) {
  ctx.fillStyle = color
  ctx.beginPath()
  points.forEach(([px, py], index) => (index === 0 ? ctx.moveTo(px, py) : ctx.lineTo(px, py)))
  ctx.closePath()
  ctx.fill()
}

function roundRect(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  w: number,
  h: number,
  r: number,
  color: string,
) {
  ctx.fillStyle = color
  ctx.beginPath()
  ctx.roundRect(x, y, w, h, r)
  ctx.fill()
}

// ----- Tamanduá-bandeira -----

const ANTEATER = {
  body: '#8b7b67',
  dark: '#3f362d',
  tail: '#5b4b3a',
  arm: '#e7e2da',
  stripe: '#1c1917',
  stripeEdge: '#f5f5f4',
}

/**
 * Braço da frente do tamanduá: claro, com a faixa preta perto do punho e as garras na
 * ponta — é o que deixa a posição de defesa reconhecível.
 */
function anteaterArm(ctx: CanvasRenderingContext2D, sx: number, sy: number, hx: number, hy: number) {
  line(ctx, sx, sy, hx, hy, 5, ANTEATER.arm)

  const bandStart = 0.68
  const bandEnd = 0.84
  line(
    ctx,
    sx + (hx - sx) * bandStart,
    sy + (hy - sy) * bandStart,
    sx + (hx - sx) * bandEnd,
    sy + (hy - sy) * bandEnd,
    5.5,
    ANTEATER.stripe,
  )

  const angle = Math.atan2(hy - sy, hx - sx)
  for (const spread of [-0.55, 0, 0.55]) {
    const claw = angle + spread + (hx < sx ? -0.5 : 0.5)
    line(ctx, hx, hy, hx + Math.cos(claw) * 6, hy + Math.sin(claw) * 6, 1.6, ANTEATER.dark)
  }
}

function anteaterHead(ctx: CanvasRenderingContext2D, hx: number, hy: number) {
  ellipse(ctx, hx - 3, hy - 5, 2, 3, -0.3, ANTEATER.body)
  ellipse(ctx, hx, hy, 6.5, 5, 0, ANTEATER.body)
  // Focinho comprido e afinando, levemente para baixo.
  polygon(
    ctx,
    [
      [hx + 3, hy - 3.5],
      [hx + 22, hy + 6],
      [hx + 22, hy + 7.5],
      [hx + 3, hy + 3.5],
    ],
    ANTEATER.body,
  )
  ellipse(ctx, hx + 22, hy + 6.8, 1.4, 1.4, 0, ANTEATER.stripe)
  ellipse(ctx, hx + 2.5, hy - 1.2, 1.2, 1.2, 0, ANTEATER.stripe)
}

function drawAnteater(ctx: CanvasRenderingContext2D, { x, feet, ducking, airborne, time }: Pose) {
  const swing = airborne ? 0 : Math.sin(time * 18) * 5

  if (ducking) {
    // De quatro, correndo baixo.
    ellipse(ctx, x - 27, feet - 14, 15, 8, -0.15, ANTEATER.tail)
    line(ctx, x - 12, feet - 10, x - 14 - swing, feet, 5, ANTEATER.dark)
    ellipse(ctx, x - 2, feet - 15, 22, 10, 0, ANTEATER.body)
    line(ctx, x + 9, feet - 24, x - 5, feet - 9, 8, ANTEATER.stripeEdge)
    line(ctx, x + 9, feet - 24, x - 5, feet - 9, 4, ANTEATER.stripe)
    line(ctx, x + 12, feet - 10, x + 14 + swing, feet, 5, ANTEATER.arm)
    line(ctx, x + 13 + swing * 0.7, feet - 4, x + 14 + swing, feet - 1, 5, ANTEATER.stripe)
    anteaterHead(ctx, x + 20, feet - 18)
    return
  }

  // Em pé, de braços abertos: a postura de defesa do tamanduá-bandeira.
  const bob = airborne ? -2 : Math.sin(time * 18) * 1.5

  ellipse(ctx, x - 17, feet - 20, 15, 9, -0.6, ANTEATER.tail)
  line(ctx, x - 22, feet - 26, x - 30, feet - 20, 2, '#6f604d')
  line(ctx, x - 20, feet - 18, x - 29, feet - 12, 2, '#6f604d')

  if (airborne) {
    line(ctx, x - 4, feet - 24, x - 8, feet - 4, 6, ANTEATER.dark)
    line(ctx, x + 3, feet - 24, x + 6, feet - 6, 6, ANTEATER.dark)
  } else {
    line(ctx, x - 4, feet - 24, x - 6 - swing, feet, 6, ANTEATER.dark)
    line(ctx, x + 3, feet - 24, x + 4 + swing, feet, 6, '#4a4036')
  }

  ellipse(ctx, x, feet - 36, 11.5, 19, 0, ANTEATER.body)
  line(ctx, x + 9, feet - 50, x - 8, feet - 30, 9, ANTEATER.stripeEdge)
  line(ctx, x + 9, feet - 50, x - 8, feet - 30, 5, ANTEATER.stripe)

  anteaterArm(ctx, x - 7, feet - 46, x - 28, feet - 54 + bob)
  anteaterArm(ctx, x + 7, feet - 46, x + 28, feet - 54 - bob)

  anteaterHead(ctx, x + 2, feet - 60)
}

// ----- Capivara -----

const CAPYBARA = { body: '#9a6b43', head: '#8a5d39', dark: '#4a3121', orange: '#f59e0b', leaf: '#16a34a' }

function drawCapybara(ctx: CanvasRenderingContext2D, { x, feet, ducking, airborne, time }: Pose) {
  const swing = airborne ? 0 : Math.sin(time * 16) * 3
  const lift = ducking ? 6 : 0

  for (const [legX, phase] of [
    [-14, 1],
    [-6, -1],
    [8, -1],
    [15, 1],
  ] as const) {
    line(ctx, x + legX, feet - 10, x + legX + swing * phase, feet, 5, CAPYBARA.dark)
  }

  roundRect(ctx, x - 23, feet - 34 + lift, 44, 26 - lift / 2, 12, CAPYBARA.body)
  roundRect(ctx, x + 9, feet - 42 + lift, 21, 18, 7, CAPYBARA.head)
  roundRect(ctx, x + 22, feet - 36 + lift, 9, 11, 4, CAPYBARA.dark)
  ellipse(ctx, x + 12, feet - 42 + lift, 2.5, 3, 0, CAPYBARA.dark)
  ellipse(ctx, x + 19, feet - 37 + lift, 1.3, 1.3, 0, '#1c1917')

  // A laranja na cabeça, como manda a tradição da capivara tranquila.
  if (!ducking) {
    ellipse(ctx, x + 17, feet - 47, 5, 4.5, 0, CAPYBARA.orange)
    ellipse(ctx, x + 19, feet - 52, 2.5, 1.2, -0.5, CAPYBARA.leaf)
  }
}

export const characters: Character[] = [
  {
    id: 'tamandua',
    name: 'Tamanduá',
    tagline: 'De braços abertos, em posição de defesa.',
    hitboxes: { standing: { w: 34, h: 64 }, ducking: { w: 56, h: 30 } },
    jumpBoost: 1,
    draw: drawAnteater,
  },
  {
    id: 'capivara',
    name: 'Capivara',
    tagline: 'Baixinha e tranquila: passa por baixo do quero-quero alto, mas pula menos.',
    hitboxes: { standing: { w: 44, h: 44 }, ducking: { w: 50, h: 30 } },
    jumpBoost: 0.92,
    draw: drawCapybara,
  },
]

export function characterById(id: string | null | undefined): Character {
  return characters.find((character) => character.id === id) ?? characters[0]!
}
