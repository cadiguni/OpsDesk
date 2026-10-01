// Aplica o tema antes do primeiro desenho da página, para não piscar claro num sistema
// escuro. Fica num arquivo, e não inline no index.html, porque a política de segurança de
// conteúdo da produção (deploy/Caddyfile) só permite script servido pela própria origem.
(() => {
  let theme = 'system'
  try {
    theme = localStorage.getItem('opsdesk-theme') || 'system'
  } catch {
    // Armazenamento bloqueado: segue o sistema.
  }
  const dark = theme === 'dark' || (theme !== 'light' && matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.classList.toggle('dark', dark)
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
})()
