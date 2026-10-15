import { apiUrl, http } from './client'
import type {
  AdminClinicDetail,
  AdminMe,
  AdminNote,
  AuditItem,
  BulkStatusResult,
  ClinicListItem,
  ClinicMap,
  ClinicProfile,
  ClinicStatus,
  Dashboard,
  Message,
  PagedResult,
  Plan,
  ProfileUpdatePayload,
  RegistrationPayload,
  RegistrationResponse,
  RequestCodeResponse,
  TwoFactorRequired,
  TwoFactorSetup,
  TwoFactorStatus,
} from './types'

/** Builds a query string from defined, non-empty values. */
function query(params: object): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '' && value !== false) search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const publicApi = {
  plans: () => http.get<Plan[]>('/api/plans'),
  register: (payload: RegistrationPayload) => http.post<RegistrationResponse>('/api/registrations', payload),
}

export const clinicAuthApi = {
  requestCode: (email: string) => http.post<RequestCodeResponse>('/api/clinic-auth/request-code', { email }),
  verifyCode: (email: string, code: string) => http.post<ClinicProfile>('/api/clinic-auth/verify-code', { email, code }),
  logout: () => http.post<void>('/api/clinic-auth/logout'),
}

export const clinicApi = {
  me: () => http.get<ClinicProfile>('/api/clinic/me'),
  update: (payload: ProfileUpdatePayload) => http.put<ClinicProfile>('/api/clinic/me', payload),
  messages: () => http.get<Message[]>('/api/clinic/messages'),
  sendMessage: (body: string) => http.post<Message>('/api/clinic/messages', { body }),
  confirmationUrl: apiUrl('/api/clinic/me/confirmation.pdf'),
}

export const adminAuthApi = {
  /** Resolves to the administrator, or to {twoFactorRequired} when a code from the authenticator app is needed. */
  login: (username: string, password: string) => http.post<AdminMe | TwoFactorRequired>('/api/admin-auth/login', { username, password }),
  loginTwoFactor: (body: { code?: string; recoveryCode?: string }) => http.post<AdminMe>('/api/admin-auth/login/two-factor', body),
  logout: () => http.post<void>('/api/admin-auth/logout'),
  me: () => http.get<AdminMe>('/api/admin-auth/me'),
}

export const adminSecurityApi = {
  status: () => http.get<TwoFactorStatus>('/api/admin/security/two-factor'),
  setup: () => http.post<TwoFactorSetup>('/api/admin/security/two-factor/setup'),
  enable: (code: string) => http.post<{ recoveryCodes: string[] }>('/api/admin/security/two-factor/enable', { code }),
  disable: (code: string) => http.post<void>('/api/admin/security/two-factor/disable', { code }),
  renewRecoveryCodes: (code: string) => http.post<{ recoveryCodes: string[] }>('/api/admin/security/two-factor/recovery-codes', { code }),
}

export interface ClinicListParams {
  search?: string
  status?: string
  plan?: string
  unread?: boolean
  sort?: string
  direction?: string
  page?: number
  pageSize?: number
}

export interface AuditParams {
  from?: string
  to?: string
  type?: string
  actor?: string
  clinicId?: string
  search?: string
  page?: number
  pageSize?: number
}

export const adminApi = {
  dashboard: (days = 7) => http.get<Dashboard>(`/api/admin/dashboard${query({ days })}`),
  clinics: (params: ClinicListParams) => http.get<PagedResult<ClinicListItem>>(`/api/admin/clinics${query(params)}`),
  clinic: (id: string) => http.get<AdminClinicDetail>(`/api/admin/clinics/${encodeURIComponent(id)}`),
  changeStatus: (id: string, body: { status: ClinicStatus; message: string | null; internalNote: string | null; version: number }) =>
    http.post<AdminClinicDetail>(`/api/admin/clinics/${encodeURIComponent(id)}/status`, body),
  bulkStatus: (body: { clinicIds: string[]; status: ClinicStatus; message: string | null }) =>
    http.post<BulkStatusResult>('/api/admin/clinics/bulk-status', body),
  addNote: (id: string, text: string) => http.post<AdminNote>(`/api/admin/clinics/${encodeURIComponent(id)}/notes`, { text }),
  messages: (id: string) => http.get<Message[]>(`/api/admin/clinics/${encodeURIComponent(id)}/messages`),
  sendMessage: (id: string, body: string) => http.post<Message>(`/api/admin/clinics/${encodeURIComponent(id)}/messages`, { body }),
  confirmationUrl: (id: string) => apiUrl(`/api/admin/clinics/${encodeURIComponent(id)}/confirmation.pdf`),
  map: (params: { status?: string; plan?: string }) => http.get<ClinicMap>(`/api/admin/clinics/map${query(params)}`),
  audit: (params: AuditParams) => http.get<PagedResult<AuditItem>>(`/api/admin/audit${query(params)}`),
  auditExportUrl: (params: AuditParams) => apiUrl(`/api/admin/audit/export${query({ ...params, page: undefined, pageSize: undefined })}`),
}
