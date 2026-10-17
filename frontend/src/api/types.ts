// DTOs mirroring the backend contracts (camelCase JSON, enums as camelCase strings).

export type ClinicStatus = 'pending' | 'approved' | 'rejected'
export type OnboardingPreference = 'online' | 'onSite'
export type FieldGroup = 'operationalContact' | 'serviceConfiguration'

export interface PlanFeature {
  text: string
  /** false = shown as "not part of this level" */
  included: boolean
}

export interface Plan {
  code: string
  name: string
  tagline: string
  description: string
  features: PlanFeature[]
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
  /** Booking contacts published for patients. */
  publicEmails: string[]
  publicPhones: string[]
  website: string | null
  patientInfo: string | null
  /** When the registering person confirmed the authority to act for the organization (null for older registrations). */
  authorityConfirmedAt: string | null
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
  publicEmails: string[]
  publicPhones: string[]
  website: string | null
  patientInfo: string | null
  authorityConfirmed: boolean
  privacyNoticeAccepted: boolean
  /** Issued after the e-mail was verified with the registration code. */
  emailVerificationToken: string | null
}

export type ProfileUpdatePayload = Omit<
  RegistrationPayload,
  'planCode' | 'name' | 'organizationId' | 'email' | 'privacyNoticeAccepted' | 'authorityConfirmed' | 'emailVerificationToken'
>

export interface RegistrationCodeResponse {
  email: string
  codeLifetimeMinutes: number
  resendAfterSeconds: number
}

export interface RegistrationCodeVerifyResponse {
  email: string
  verificationToken: string
  expiresAt: string
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
  twoFactorEnabled: boolean
}

/** Returned (HTTP 202) by the password step when the administrator has two-factor authentication on. */
export interface TwoFactorRequired {
  twoFactorRequired: true
  expiresInSeconds: number
}

export interface TwoFactorStatus {
  enabled: boolean
  enabledAt: string | null
  recoveryCodesRemaining: number
}

export interface TwoFactorSetup {
  secret: string
  otpAuthUri: string
  qrCodeDataUri: string
}

export interface DashboardMetrics {
  totalClinics: number
  newToday: number
  pendingReview: number
  approved: number
  rejected: number
  uniqueClinicSignInsToday: number
  emailUnverified: number
  unreadMessages: number
  approvalRatePercent: number | null
  averageDecisionHours: number | null
}

export type ActivityType =
  | 'clinicRegistered'
  | 'clinicEmailVerified'
  | 'clinicSignedIn'
  | 'clinicSignedOut'
  | 'clinicProfileUpdated'
  | 'clinicStatusChanged'
  | 'clinicMessageSent'
  | 'confirmationDownloaded'
  | 'adminNoteAdded'
  | 'adminSignedIn'
  | 'adminSignedOut'
  | 'adminSignInFailed'
  | 'adminMessageSent'
  | 'adminTwoFactorEnabled'
  | 'adminTwoFactorDisabled'
  | 'adminRecoveryCodesRenewed'
  | 'adminPasswordReset'
  | 'auditLogExported'
  | 'emailSent'
  | 'emailFailed'
  | 'demoDataSeeded'
  | 'demoDataRemoved'
  | 'clinicDeleted'

export interface ActivityItem {
  id: number
  type: ActivityType
  occurredAt: string
  clinicId: string | null
  clinicName: string | null
  administratorUsername: string | null
  detail: string | null
}

export interface DailyCount {
  date: string
  count: number
}

export interface Dashboard {
  metrics: DashboardMetrics
  funnel: { registered: number; emailVerified: number; approved: number }
  plans: { planCode: string; planName: string; count: number }[]
  seriesDays: number
  registrations: DailyCount[]
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
  unreadMessages: number
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
  location: { latitude: number; longitude: number } | null
  isDemo: boolean
}

export type MessageSender = 'clinic' | 'administrator'

export interface Message {
  id: string
  sender: MessageSender
  /** Administrator username (admin view) or "Administrátor" (clinic view); null for clinic messages. */
  authorName: string | null
  body: string
  createdAt: string
  /** When the recipient first read it, as it was before this request marked anything read. */
  readAt: string | null
}

export interface BulkDeleteResult {
  deleted: string[]
  notFound: string[]
}

export interface BulkStatusResult {
  updated: string[]
  skipped: string[]
  notFound: string[]
}

export interface MapClinic {
  id: string
  name: string
  city: string
  planCode: string
  status: ClinicStatus
  latitude: number
  longitude: number
}

export interface ClinicMap {
  items: MapClinic[]
  withoutLocation: number
}

export type AuditActor = 'clinic' | 'administrator' | 'system'

export interface AuditItem {
  id: number
  occurredAt: string
  type: ActivityType
  actor: AuditActor
  actorName: string | null
  clinicId: string | null
  clinicName: string | null
  detail: string | null
}
