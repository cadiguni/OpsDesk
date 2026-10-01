import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// O jsdom não implementa matchMedia, e o seletor de tema consulta a preferência do sistema.
// Sem isto, toda tela que mostra o seletor quebra no teste por um motivo que não existe
// no navegador.
if (!window.matchMedia) {
  window.matchMedia = (query: string) =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList
}

afterEach(() => {
  cleanup()
})
