import { http } from './client'
import type {
  AdminClinicDetail,
  AdminMe,
  AdminNote,
  ClinicListItem,
  ClinicProfile,
  ClinicStatus,
  Dashboard,
  PagedResult,
  Plan,
  ProfileUpdatePayload,
  RegistrationPayload,
  RegistrationResponse,
  RequestCodeResponse,
} from './types'

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
}

export const adminAuthApi = {
  login: (username: string, password: string) => http.post<AdminMe>('/api/admin-auth/login', { username, password }),
  logout: () => http.post<void>('/api/admin-auth/logout'),
  me: () => http.get<AdminMe>('/api/admin-auth/me'),
}

export interface ClinicListParams {
  search?: string
  status?: string
  plan?: string
  sort?: string
  direction?: string
  page?: number
  pageSize?: number
}

export const adminApi = {
  dashboard: () => http.get<Dashboard>('/api/admin/dashboard'),
  clinics: (params: ClinicListParams) => {
    const query = new URLSearchParams()
    for (const [key, value] of Object.entries(params)) {
      if (value !== undefined && value !== '') query.set(key, String(value))
    }
    return http.get<PagedResult<ClinicListItem>>(`/api/admin/clinics?${query.toString()}`)
  },
  clinic: (id: string) => http.get<AdminClinicDetail>(`/api/admin/clinics/${encodeURIComponent(id)}`),
  changeStatus: (id: string, body: { status: ClinicStatus; message: string | null; internalNote: string | null; version: number }) =>
    http.post<AdminClinicDetail>(`/api/admin/clinics/${encodeURIComponent(id)}/status`, body),
  addNote: (id: string, text: string) => http.post<AdminNote>(`/api/admin/clinics/${encodeURIComponent(id)}/notes`, { text }),
}
