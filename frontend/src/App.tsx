import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Route, Routes } from 'react-router-dom'

import { SessionProvider } from '@/features/auth/session'
import { queryClient } from '@/lib/query-client'
import { AppLayout } from '@/routes/app-layout'
import { Home } from '@/routes/home'
import { Login } from '@/routes/login'
import { NotFound } from '@/routes/not-found'
import { ProtectedRoute } from '@/routes/protected-route'
import { Register } from '@/routes/register'

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {/* O SessionProvider fica dentro do BrowserRouter porque as telas de login
            navegam, e dentro do QueryClientProvider porque limpa o cache ao sair. */}
        <SessionProvider>
          <Routes>
            <Route path="/entrar" element={<Login />} />
            <Route path="/cadastro" element={<Register />} />

            <Route element={<ProtectedRoute />}>
              <Route element={<AppLayout />}>
                <Route path="/" element={<Home />} />
                <Route path="*" element={<NotFound />} />
              </Route>
            </Route>
          </Routes>
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
