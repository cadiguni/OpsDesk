/**
 * Enums do domínio e seus rótulos.
 *
 * Os valores são exatamente as strings que a API persiste e devolve — o backend grava
 * enum como texto justamente para o banco continuar legível, e o frontend não traduz
 * nada na ida. Os rótulos em português existem só para a interface, conforme a convenção
 * de idioma do projeto: código em inglês, o que o usuário lê em português.
 */

export const ticketStatuses = [
  'Open',
  'Triage',
  'InProgress',
  'WaitingOnRequester',
  'Resolved',
  'Closed',
  'Cancelled',
] as const

export type TicketStatus = (typeof ticketStatuses)[number]

export const ticketStatusLabels: Record<TicketStatus, string> = {
  Open: 'Aberto',
  Triage: 'Em triagem',
  InProgress: 'Em atendimento',
  WaitingOnRequester: 'Aguardando usuário',
  Resolved: 'Resolvido',
  Closed: 'Fechado',
  Cancelled: 'Cancelado',
}

export const ticketPriorities = ['Low', 'Medium', 'High', 'Critical'] as const

export type TicketPriority = (typeof ticketPriorities)[number]

export const ticketPriorityLabels: Record<TicketPriority, string> = {
  Low: 'Baixa',
  Medium: 'Média',
  High: 'Alta',
  Critical: 'Crítica',
}

export const userRoles = ['Requester', 'Technician', 'Manager'] as const

export type UserRole = (typeof userRoles)[number]

export const userRoleLabels: Record<UserRole, string> = {
  Requester: 'Usuário',
  Technician: 'Técnico',
  Manager: 'Gestor',
}

export const ticketSources = [
  'Portal',
  'Email',
  'Csv',
  'Freshdesk',
  'Freshservice',
  'Glpi',
  'ExternalApi',
] as const

export type TicketSource = (typeof ticketSources)[number]

export const ticketSourceLabels: Record<TicketSource, string> = {
  Portal: 'Portal',
  Email: 'E-mail',
  Csv: 'CSV',
  Freshdesk: 'Freshdesk',
  Freshservice: 'Freshservice',
  Glpi: 'GLPI',
  ExternalApi: 'API externa',
}

/** Status que encerram o chamado: saem das filas de atendimento. */
export const closedOutStatuses: readonly TicketStatus[] = ['Resolved', 'Closed', 'Cancelled']

/** Técnico e gestor formam a equipe: veem comentário interno e o painel técnico. */
export function isStaff(role: UserRole): boolean {
  return role === 'Technician' || role === 'Manager'
}
