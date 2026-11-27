import { useEffect, useRef, useState, type KeyboardEvent } from 'react'

import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

import { characterById, characters, type Character } from './characters'
import {
  createRunner,
  score,
  start,
  step,
  WORLD,
  type Input,
  type Obstacle,
  type RunnerState,
} from './engine'

const RECORD_KEY = 'arquibancada.recorde'
const CHARACTER_KEY = 'arquibancada.personagem'

function readStorage(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function writeStorage(key: string, value: string) {
  try {
    localStorage.setItem(key, value)
  } catch {
    // Navegação privada ou armazenamento bloqueado: o recorde só não sobrevive ao recarregar.
  }
}

const JUMP_KEYS = new Set([' ', 'Spacebar', 'ArrowUp', 'w', 'W'])
const DUCK_KEYS = new Set(['ArrowDown', 's', 'S'])

/**
 * Corrida infinita no gramado: pular cone e bola, abaixar do quero-quero.
 *
 * O teclado só vale com o jogo em foco — a página tem outros controles, e a barra de
 * espaço rolando a página no meio do pulo seria pior que não ter jogo.
 */
export function RunnerGame() {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const wrapperRef = useRef<HTMLDivElement>(null)
  const stateRef = useRef<RunnerState>(createRunner())
  const inputRef = useRef<Input>({ jump: false, duck: false })
  const [characterId, setCharacterId] = useState(() => characterById(readStorage(CHARACTER_KEY)).id)
  const [record, setRecord] = useState(() => Number(readStorage(RECORD_KEY)) || 0)
  const [lastScore, setLastScore] = useState<number | null>(null)
  const recordRef = useRef(record)
  const character = characterById(characterId)

  useEffect(() => {
    wrapperRef.current?.focus()
  }, [])

  useEffect(() => {
    const canvas = canvasRef.current
    const ctx = canvas?.getContext('2d')

    if (!canvas || !ctx) {
      return
    }

    const ratio = window.devicePixelRatio || 1
    canvas.width = WORLD.width * ratio
    canvas.height = WORLD.height * ratio
    ctx.scale(ratio, ratio)

    stateRef.current = createRunner()
    let frame = 0
    let previous = performance.now()

    const loop = (now: number) => {
      // Aba escondida ou máquina engasgada: no máximo 50 ms de jogo por quadro, para o
      // tamanduá não atravessar um cone inteiro sem colidir.
      const dt = Math.min(0.05, (now - previous) / 1000)
      previous = now

      const state = stateRef.current
      const wasRunning = state.status === 'running'
      step(state, dt, inputRef.current, character.hitboxes, character.jumpBoost)

      if (wasRunning && state.status === 'over') {
        const final = score(state)
        setLastScore(final)

        if (final > recordRef.current) {
          recordRef.current = final
          setRecord(final)
          writeStorage(RECORD_KEY, String(final))
        }
      }

      draw(ctx, state, character, recordRef.current, getComputedStyle(canvas).color)
      frame = requestAnimationFrame(loop)
    }

    frame = requestAnimationFrame(loop)

    return () => cancelAnimationFrame(frame)
  }, [character])

  function press() {
    inputRef.current.jump = true
    const state = stateRef.current

    if (state.status !== 'running') {
      setLastScore(null)
      stateRef.current = start(state)
    }
  }

  function release() {
    inputRef.current.jump = false
  }

  function onKeyDown(event: KeyboardEvent) {
    if (JUMP_KEYS.has(event.key)) {
      event.preventDefault()
      if (!event.repeat) press()
    } else if (DUCK_KEYS.has(event.key)) {
      event.preventDefault()
      inputRef.current.duck = true
    }
  }

  function onKeyUp(event: KeyboardEvent) {
    if (JUMP_KEYS.has(event.key)) release()
    if (DUCK_KEYS.has(event.key)) inputRef.current.duck = false
  }

  function choose(next: Character) {
    setCharacterId(next.id)
    writeStorage(CHARACTER_KEY, next.id)
    setLastScore(null)
    wrapperRef.current?.focus()
  }

  return (
    <div className="space-y-4">
      <div role="radiogroup" aria-label="Personagem" className="flex flex-wrap gap-2">
        {characters.map((option) => (
          <button
            key={option.id}
            type="button"
            role="radio"
            aria-checked={option.id === character.id}
            onClick={() => choose(option)}
            className={cn(
              'rounded-lg border px-3 py-2 text-left text-sm transition-colors',
              option.id === character.id ? 'border-primary bg-primary/10' : 'hover:bg-muted',
            )}
          >
            <span className="block font-semibold">{option.name}</span>
            <span className="text-muted-foreground block text-xs">{option.tagline}</span>
          </button>
        ))}
      </div>

      <div
        ref={wrapperRef}
        tabIndex={0}
        aria-label="Corrida. Espaço ou seta para cima pula, seta para baixo abaixa."
        onKeyDown={onKeyDown}
        onKeyUp={onKeyUp}
        onBlur={() => {
          inputRef.current = { jump: false, duck: false }
        }}
        className="focus-visible:ring-ring/50 rounded-xl border bg-card outline-none focus-visible:ring-[3px]"
      >
        <canvas
          ref={canvasRef}
          className="text-foreground block h-auto w-full touch-none select-none"
          style={{ aspectRatio: `${WORLD.width} / ${WORLD.height}` }}
          onPointerDown={(event) => {
            event.currentTarget.setPointerCapture(event.pointerId)
            wrapperRef.current?.focus()
            press()
          }}
          onPointerUp={release}
          onPointerCancel={release}
        />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 text-sm">
        <p className="text-muted-foreground">
          Espaço ou ↑ pula (segure para ir mais alto) · ↓ abaixa · no celular, toque para pular.
        </p>
        <Button
          variant="outline"
          size="sm"
          className="touch-none select-none sm:hidden"
          onPointerDown={() => (inputRef.current.duck = true)}
          onPointerUp={() => (inputRef.current.duck = false)}
          onPointerCancel={() => (inputRef.current.duck = false)}
        >
          Abaixar
        </Button>
      </div>

      <p aria-live="polite" className="sr-only">
        {lastScore !== null ? `Fim de jogo. ${lastScore} pontos. Recorde ${record}.` : ''}
      </p>
    </div>
  )
}

// ----- Desenho -----

const GRASS = '#3f8f3a'
const GRASS_STRIPE = '#377f33'

function draw(
  ctx: CanvasRenderingContext2D,
  state: RunnerState,
  character: Character,
  record: number,
  ink: string,
) {
  const { width, height, ground } = WORLD
  ctx.clearRect(0, 0, width, height)

  // Nuvens ao longe, em paralaxe lenta.
  ctx.globalAlpha = 0.1
  for (let index = 0; index < 4; index++) {
    const cx = (((index * 260 - state.distance * 0.15) % (width + 200)) + width + 200) % (width + 200) - 100
    ctx.fillStyle = ink
    ctx.beginPath()
    ctx.ellipse(cx, 40 + (index % 2) * 26, 34, 10, 0, 0, Math.PI * 2)
    ctx.ellipse(cx + 22, 34 + (index % 2) * 26, 20, 9, 0, 0, Math.PI * 2)
    ctx.fill()
  }
  ctx.globalAlpha = 1

  // Gramado com as faixas do corte, correndo com o cenário.
  ctx.fillStyle = GRASS
  ctx.fillRect(0, ground, width, height - ground)
  ctx.fillStyle = GRASS_STRIPE
  const offset = state.distance % 80
  for (let x = -offset; x < width; x += 80) {
    ctx.fillRect(x, ground, 40, height - ground)
  }
  ctx.fillStyle = '#f5f5f4'
  ctx.fillRect(0, ground + 3, width, 2)

  for (const obstacle of state.obstacles) {
    drawObstacle(ctx, obstacle, state.elapsed, state.distance)
  }

  const player = state.player
  character.draw(ctx, {
    x: player.x,
    feet: ground - player.y,
    ducking: player.ducking,
    airborne: player.y > 0,
    time: state.elapsed,
  })

  ctx.fillStyle = ink
  ctx.font = '600 14px ui-monospace, SFMono-Regular, Menlo, monospace'
  ctx.textAlign = 'right'
  ctx.fillText(
    `REC ${String(record).padStart(5, '0')}   ${String(score(state)).padStart(5, '0')}`,
    width - 16,
    26,
  )

  if (state.status !== 'running') {
    ctx.textAlign = 'center'
    ctx.font = '700 18px system-ui, sans-serif'
    ctx.fillText(state.status === 'over' ? 'Fim de jogo!' : character.name, width / 2, 80)
    ctx.font = '400 14px system-ui, sans-serif'
    ctx.fillText(
      state.status === 'over' ? 'Espaço ou toque para tentar de novo' : 'Espaço ou toque para começar',
      width / 2,
      104,
    )
  }
}

function drawObstacle(ctx: CanvasRenderingContext2D, obstacle: Obstacle, time: number, distance: number) {
  const { ground } = WORLD
  const base = ground - obstacle.bottom

  if (obstacle.kind === 'cone' || obstacle.kind === 'cones') {
    const count = obstacle.kind === 'cones' ? 2 : 1
    const coneWidth = obstacle.w / count

    for (let index = 0; index < count; index++) {
      const left = obstacle.x + index * coneWidth
      ctx.fillStyle = '#ea580c'
      ctx.fillRect(left - 1, base - 4, coneWidth + 2, 4)
      ctx.fillStyle = '#f97316'
      ctx.beginPath()
      ctx.moveTo(left + 2, base - 4)
      ctx.lineTo(left + coneWidth / 2, base - obstacle.h)
      ctx.lineTo(left + coneWidth - 2, base - 4)
      ctx.closePath()
      ctx.fill()
      ctx.fillStyle = '#fafaf9'
      const bandY = base - obstacle.h * 0.55
      ctx.fillRect(left + coneWidth * 0.3, bandY, coneWidth * 0.4, 4)
    }
    return
  }

  if (obstacle.kind === 'ball') {
    const r = obstacle.w / 2
    const cx = obstacle.x + r
    const cy = base - r
    ctx.fillStyle = '#fafaf9'
    ctx.beginPath()
    ctx.arc(cx, cy, r, 0, Math.PI * 2)
    ctx.fill()
    ctx.strokeStyle = '#1c1917'
    ctx.lineWidth = 1.5
    ctx.stroke()

    // Gomos girando ao contrário do avanço, que é o sentido de quem rola na nossa direção.
    const spin = -(distance / r)
    ctx.fillStyle = '#1c1917'
    for (let index = 0; index < 3; index++) {
      const angle = spin + (index * Math.PI * 2) / 3
      ctx.beginPath()
      ctx.arc(cx + Math.cos(angle) * r * 0.55, cy + Math.sin(angle) * r * 0.55, r * 0.22, 0, Math.PI * 2)
      ctx.fill()
    }
    return
  }

  // Quero-quero: cinza, peito preto, olho vermelho, batendo asa.
  const cx = obstacle.x + obstacle.w / 2
  const cy = base - obstacle.h / 2
  const flap = Math.sin(time * 14) * 9

  ctx.fillStyle = '#78716c'
  ctx.beginPath()
  ctx.moveTo(cx - 4, cy - 2)
  ctx.lineTo(cx + 6, cy - 2)
  ctx.lineTo(cx + 2, cy - 4 - flap)
  ctx.closePath()
  ctx.fill()

  ctx.fillStyle = '#a8a29e'
  ctx.beginPath()
  ctx.ellipse(cx, cy, 13, 6, 0, 0, Math.PI * 2)
  ctx.fill()
  ctx.fillStyle = '#1c1917'
  ctx.beginPath()
  ctx.ellipse(cx - 6, cy + 1, 5, 4.5, 0, 0, Math.PI * 2)
  ctx.fill()
  ctx.fillStyle = '#fafaf9'
  ctx.beginPath()
  ctx.ellipse(cx + 2, cy + 3, 7, 2.5, 0, 0, Math.PI * 2)
  ctx.fill()
  ctx.fillStyle = '#a8a29e'
  ctx.beginPath()
  ctx.arc(cx - 13, cy - 3, 4.5, 0, Math.PI * 2)
  ctx.fill()
  ctx.strokeStyle = '#1c1917'
  ctx.lineWidth = 1.5
  ctx.beginPath()
  ctx.moveTo(cx - 13, cy - 7)
  ctx.lineTo(cx - 8, cy - 11)
  ctx.stroke()
  ctx.fillStyle = '#dc2626'
  ctx.beginPath()
  ctx.arc(cx - 14.5, cy - 3.5, 1.3, 0, Math.PI * 2)
  ctx.fill()
  ctx.fillStyle = '#f87171'
  ctx.beginPath()
  ctx.moveTo(cx - 17, cy - 3)
  ctx.lineTo(cx - 23, cy - 1.5)
  ctx.lineTo(cx - 17, cy - 1)
  ctx.closePath()
  ctx.fill()
}
