import { Outlet } from 'react-router-dom'

export function AppLayout() {
  return (
    <div className="min-h-dvh bg-background">
      <header className="border-b">
        <div className="mx-auto flex h-14 max-w-5xl items-center gap-3 px-4">
          <span className="grid size-7 place-items-center rounded-md bg-primary text-sm font-bold text-primary-foreground">
            O
          </span>
          <div className="leading-tight">
            <p className="text-sm font-semibold">OpsDesk</p>
            <p className="text-muted-foreground text-xs">Chamados de TI</p>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-5xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
