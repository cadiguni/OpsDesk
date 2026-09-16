import { Link } from 'react-router-dom'

import { buttonVariants } from '@/components/ui/button'

export function NotFound() {
  return (
    <section className="space-y-4">
      <h1 className="text-xl font-semibold">Página não encontrada</h1>
      <p className="text-muted-foreground text-sm">
        O endereço acessado não corresponde a nenhuma tela do sistema.
      </p>
      {/* Link com a aparência de botão, e não um Link dentro de um button:
          âncora aninhada em botão é HTML inválido e quebra a navegação por teclado. */}
      <Link to="/" className={buttonVariants()}>
        Voltar ao início
      </Link>
    </section>
  )
}
