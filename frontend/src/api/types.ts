// DTOs mirroring the backend contracts (camelCase JSON, enums as camelCase strings).

export type ClinicStatus = 'pending' | 'approved' | 'rejected'
export type OnboardingPreference = 'online' | 'onSite'
export type FieldGroup = 'operationalContact' | 'serviceConfiguration'

export interface Plan {
  code: string
  name: string
  tagline: string
  description: string
  features: string[]
  fieldGroups: FieldGroup[]
  highlighted: boolean
  badge: string | null
}

export interface OperationalContact {
  name: string
  email: string
  phone: string | null
}

export interface ServiceConfiguration {
  workplaceCount: number
  onboardingPreference: OnboardingPreference
  technicalContactEmail: string | null
  notes: string | null
}

export interface ClinicProfile {
  id: string
  name: string
  organizationId: string | null
  contactFirstName: string
  contactLastName: string
  email: string
  phone: string
  street: string
  city: string
  postalCode: string
  plan: { code: string; name: string; tagline: string; features: string[] }
  operationalContact: OperationalContact | null
  serviceConfiguration: ServiceConfiguration | null
  status: ClinicStatus
  statusMessage: string | null
  statusChangedAt: string | null
  createdAt: string
  updatedAt: string
  emailVerifiedAt: string | null
}

export interface RegistrationPayload {
  planCode: string
  name: string
  organizationId: string | null
  contactFirstName: string
  contactLastName: string
  email: string
  phone: string
  street: string
  city: string
  postalCode: string
  operationalContact: { name: string; email: string; phone: string | null } | null
  serviceConfiguration: {
    workplaceCount: number
    onboardingPreference: OnboardingPreference
    technicalContactEmail: string | null
    notes: string | null
  } | null
  privacyNoticeAccepted: boolean
}

export type ProfileUpdatePayload = Omit<
  RegistrationPayload,
  'planCode' | 'name' | 'organizationId' | 'email' | 'privacyNoticeAccepted'
>

export interface RegistrationResponse {
  email: string
  verificationEmailSent: boolean
  codeLifetimeMinutes: number
}

export interface RequestCodeResponse {
  message: string
  codeLifetimeMinutes: number
  resendAfterSeconds: number
}

export interface AdminMe {
  id: string
  username: string
  lastLoginAt: string | null
}

export interface DashboardMetrics {
  totalClinics: number
  newToday: number
  pendingReview: number
  approved: number
  rejected: number
  uniqueClinicSignInsToday: number
  emailUnverified: number
}

export type ActivityType =
  | 'clinicRegistered'
  | 'clinicEmailVerified'
  | 'clinicSignedIn'
  | 'clinicProfileUpdated'
  | 'clinicStatusChanged'
  | 'adminNoteAdded'
  | 'adminSignedIn'

export interface ActivityItem {
  id: number
  type: ActivityType
  occurredAt: string
  clinicId: string | null
  clinicName: string | null
  administratorUsername: string | null
  detail: string | null
}

export interface Dashboard {
  metrics: DashboardMetrics
  registrationsLast7Days: { date: string; count: number }[]
  recentActivity: ActivityItem[]
  timeZone: string
  generatedAt: string
}

export interface ClinicListItem {
  id: string
  name: string
  email: string
  organizationId: string | null
  city: string
  planCode: string
  status: ClinicStatus
  emailVerified: boolean
  createdAt: string
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface StatusChange {
  id: string
  fromStatus: ClinicStatus
  toStatus: ClinicStatus
  changedAt: string
  reason: string | null
  administratorUsername: string
}

export interface AdminNote {
  id: string
  text: string
  createdAt: string
  administratorUsername: string
}

export interface AdminClinicDetail {
  profile: ClinicProfile
  version: number
  privacyNoticeAcceptedAt: string
  privacyNoticeVersion: string
  lastSignInAt: string | null
  allowedStatusChanges: ClinicStatus[]
  statusHistory: StatusChange[]
  notes: AdminNote[]
}
