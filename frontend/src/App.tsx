import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { SessionProvider } from '@/features/auth/session'
import { queryClient } from '@/lib/query-client'
import { AppLayout } from '@/routes/app-layout'
import { Arquibancada } from '@/routes/arquibancada'
import { CategoryAdmin } from '@/routes/category-admin'
import { JobTitleAdmin } from '@/routes/job-title-admin'
import { ChangePassword } from '@/routes/change-password'
import { Dashboard } from '@/routes/dashboard'
import { EmailSettingsPage } from '@/routes/email-settings'
import { ForgotPassword } from '@/routes/forgot-password'
import { GeneralSettingsPage } from '@/routes/general-settings'
import { HolidayAdmin } from '@/routes/holiday-admin'
import { InboundEmails } from '@/routes/inbound-emails'
import { Login } from '@/routes/login'
import { NewTicket } from '@/routes/new-ticket'
import { NotFound } from '@/routes/not-found'
import { Notifications } from '@/routes/notifications'
import { CHANGE_PASSWORD_PATH, ProtectedRoute } from '@/routes/protected-route'
import { Register } from '@/routes/register'
import { ResetPassword } from '@/routes/reset-password'
import { TicketDetail } from '@/routes/ticket-detail'
import { TicketImport } from '@/routes/ticket-import'
import { TicketList } from '@/routes/ticket-list'
import { UserAdmin } from '@/routes/user-admin'

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
            <Route path="/esqueci-senha" element={<ForgotPassword />} />
            <Route path="/redefinir-senha" element={<ResetPassword />} />

            <Route element={<ProtectedRoute />}>
              {/* Fora do AppLayout de propósito: enquanto a senha for provisória não há
                  menu para onde ir, e oferecer um seria oferecer telas que a API recusa. */}
              <Route path={CHANGE_PASSWORD_PATH} element={<ChangePassword />} />

              <Route element={<AppLayout />}>
                {/* A lista é a tela inicial: serve a home do usuário e o painel do
                    técnico, e quem decide o conteúdo é o filtro de visibilidade. */}
                <Route path="/" element={<Navigate to="/chamados" replace />} />
                <Route path="/chamados" element={<TicketList />} />
                <Route path="/chamados/novo" element={<NewTicket />} />
                <Route path="/chamados/:id" element={<TicketDetail />} />
                <Route path="/dashboard" element={<Dashboard />} />
                <Route path="/usuarios" element={<UserAdmin />} />
                <Route path="/cargos" element={<JobTitleAdmin />} />
                <Route path="/categorias" element={<CategoryAdmin />} />
                <Route path="/feriados" element={<HolidayAdmin />} />
                <Route path="/importacao" element={<TicketImport />} />
                <Route path="/emails" element={<InboundEmails />} />
                <Route path="/configuracoes" element={<Navigate to="/configuracoes/geral" replace />} />
                <Route path="/configuracoes/geral" element={<GeneralSettingsPage />} />
                <Route path="/configuracoes/email" element={<EmailSettingsPage />} />
                <Route path="/notificacoes" element={<Notifications />} />
                {/* Sem link em menu nenhum, de propósito: ver routes/arquibancada.tsx. */}
                <Route path="/arquibancada" element={<Arquibancada />} />
                <Route path="*" element={<NotFound />} />
              </Route>
            </Route>
          </Routes>
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
