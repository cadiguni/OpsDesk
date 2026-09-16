import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { SessionProvider } from '@/features/auth/session'
import { queryClient } from '@/lib/query-client'
import { AppLayout } from '@/routes/app-layout'
import { Login } from '@/routes/login'
import { NewTicket } from '@/routes/new-ticket'
import { NotFound } from '@/routes/not-found'
import { ProtectedRoute } from '@/routes/protected-route'
import { Register } from '@/routes/register'
import { TicketDetail } from '@/routes/ticket-detail'
import { TicketList } from '@/routes/ticket-list'

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
                {/* A lista é a tela inicial: serve a home do usuário e o painel do
                    técnico, e quem decide o conteúdo é o filtro de visibilidade. */}
                <Route path="/" element={<Navigate to="/chamados" replace />} />
                <Route path="/chamados" element={<TicketList />} />
                <Route path="/chamados/novo" element={<NewTicket />} />
                <Route path="/chamados/:id" element={<TicketDetail />} />
                <Route path="*" element={<NotFound />} />
              </Route>
            </Route>
          </Routes>
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
