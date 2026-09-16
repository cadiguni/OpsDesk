import type { TicketPriority, TicketSource, TicketStatus, UserRole } from '@/domain/enums'

export type TicketListItem = {
  id: string
  code: string
  title: string
  status: TicketStatus
  priority: TicketPriority
  categoryName: string
  requesterName: string
  assignedTechnicianName: string | null
  createdAt: string
  slaResponseDueAt: string
  slaResolutionDueAt: string
  firstRespondedAt: string | null
  resolvedAt: string | null
}

export type TicketDetail = {
  id: string
  code: string
  title: string
  description: string
  status: TicketStatus
  priority: TicketPriority
  source: TicketSource
  categoryId: string
  categoryName: string
  requesterId: string
  requesterName: string
  assignedTechnicianId: string | null
  assignedTechnicianName: string | null
  createdAt: string
  updatedAt: string
  slaResponseDueAt: string
  slaResolutionDueAt: string
  firstRespondedAt: string | null
  resolvedAt: string | null
  closedAt: string | null
  slaPaused: boolean
  slaPausedBusinessMinutes: number
  allowedNextStatuses: TicketStatus[]
}

export type CategoryOption = {
  id: string
  name: string
  description: string | null
}

export type PagedResult<T> = {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
}

export type TicketSort =
  | 'CreatedAtDescending'
  | 'CreatedAtAscending'
  | 'ResolutionDueAtAscending'
  | 'PriorityDescending'

export type TicketQuery = {
  status?: TicketStatus[]
  priority?: TicketPriority[]
  categoryId?: string
  unassigned?: boolean
  overdue?: boolean
  search?: string
  sort?: TicketSort
  page?: number
  pageSize?: number
}

export type TicketComment = {
  id: string
  authorId: string
  authorName: string
  authorRole: UserRole
  content: string
  isInternal: boolean
  createdAt: string
}

export type TicketHistoryAction =
  | 'Created'
  | 'StatusChanged'
  | 'PriorityChanged'
  | 'CategoryChanged'
  | 'TechnicianAssigned'
  | 'TechnicianUnassigned'
  | 'CommentAdded'
  | 'InternalCommentAdded'
  | 'Resolved'
  | 'Closed'
  | 'Cancelled'

export type TicketHistoryEntry = {
  id: string
  action: TicketHistoryAction
  previousValue: string | null
  newValue: string | null
  changedById: string | null
  changedByName: string | null
  createdAt: string
}

export type StaffOption = {
  id: string
  name: string
  role: UserRole
}

export type CountByLabel = {
  label: string
  count: number
}

export type DashboardSummary = {
  total: number
  open: number
  inProgress: number
  waitingOnRequester: number
  resolved: number
  overdueResponse: number
  overdueResolution: number
  byStatus: CountByLabel[]
  byPriority: CountByLabel[]
  byCategory: CountByLabel[]
  byTechnician: CountByLabel[]
}
