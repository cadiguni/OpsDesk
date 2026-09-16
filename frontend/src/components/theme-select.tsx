import { useEffect, useState } from 'react'
import { SunMoon } from 'lucide-react'

import { Select } from '@/components/ui/select'

type Theme = 'light' | 'dark' | 'system'
const storageKey = 'opsdesk-theme'

function readTheme(): Theme {
  try {
    const stored = localStorage.getItem(storageKey)
    if (stored === 'light' || stored === 'dark') return stored
  } catch { /* Preferência do sistema quando o armazenamento está indisponível. */ }
  return 'system'
}

export function ThemeSelect() {
  const [theme, setTheme] = useState<Theme>(readTheme)

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    function apply() {
      const dark = theme === 'dark' || (theme === 'system' && media.matches)
      document.documentElement.classList.toggle('dark', dark)
      document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
    }
    function sync(event: StorageEvent) {
      if (event.key === storageKey || event.key === null) setTheme(readTheme())
    }
    apply()
    media.addEventListener('change', apply)
    window.addEventListener('storage', sync)
    return () => {
      media.removeEventListener('change', apply)
      window.removeEventListener('storage', sync)
    }
  }, [theme])

  return (
    <label className="flex items-center gap-2 text-sm">
      <SunMoon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <span className="sr-only">Tema de aparência</span>
      <Select value={theme} className="h-9 w-32" onChange={(event) => {
        const next = event.target.value as Theme
        setTheme(next)
        try { localStorage.setItem(storageKey, next) } catch { /* Ainda funciona nesta sessão. */ }
      }}>
        <option value="light">Claro</option>
        <option value="dark">Escuro</option>
        <option value="system">Sistema</option>
      </Select>
    </label>
  )
}
