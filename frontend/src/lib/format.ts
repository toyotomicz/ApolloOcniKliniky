import type { ActivityItem, ActivityType, AuditActor, ClinicStatus, OnboardingPreference } from '../api/types'

// Dates are stored in UTC and displayed in the clinic administration's local time zone.
const TIME_ZONE = 'Europe/Prague'

const dateTimeFormat = new Intl.DateTimeFormat('cs-CZ', {
  day: 'numeric',
  month: 'numeric',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: TIME_ZONE,
})
const dateFormat = new Intl.DateTimeFormat('cs-CZ', { day: 'numeric', month: 'numeric', year: 'numeric', timeZone: TIME_ZONE })

export const formatDateTime = (iso: string | null | undefined): string => (iso ? dateTimeFormat.format(new Date(iso)) : '—')
export const formatDate = (iso: string | null | undefined): string => (iso ? dateFormat.format(new Date(iso)) : '—')

/** Formats a calendar date ("2026-10-13") as a short Czech label, e.g. "po 13. 10.". */
export function formatShortDay(isoDate: string): { weekday: string; day: string } {
  const [y, m, d] = isoDate.split('-').map(Number) as [number, number, number]
  const date = new Date(Date.UTC(y, m - 1, d, 12))
  return {
    weekday: new Intl.DateTimeFormat('cs-CZ', { weekday: 'short', timeZone: 'UTC' }).format(date),
    day: `${d}. ${m}.`,
  }
}

export function formatRelative(iso: string, now: Date = new Date()): string {
  const diffSeconds = Math.round((new Date(iso).getTime() - now.getTime()) / 1000)
  const rtf = new Intl.RelativeTimeFormat('cs', { numeric: 'auto' })
  const abs = Math.abs(diffSeconds)
  if (abs < 60) return rtf.format(diffSeconds, 'second')
  if (abs < 3600) return rtf.format(Math.round(diffSeconds / 60), 'minute')
  if (abs < 86400) return rtf.format(Math.round(diffSeconds / 3600), 'hour')
  return formatDateTime(iso)
}

export const statusLabels: Record<ClinicStatus, string> = {
  pending: 'Čeká na posouzení',
  approved: 'Schváleno',
  rejected: 'Zamítnuto',
}

export const statusActionLabels: Record<ClinicStatus, string> = {
  approved: 'Schválit registraci',
  rejected: 'Zamítnout registraci',
  pending: 'Vrátit k posouzení',
}

export const onboardingLabels: Record<OnboardingPreference, string> = {
  online: 'Online',
  onSite: 'Na místě',
}

export function formatPhone(phone: string | null | undefined): string {
  if (!phone) return '—'
  const match = /^(\+420)?(\d{3})(\d{3})(\d{3})$/.exec(phone)
  if (!match) return phone
  return [match[1], match[2], match[3], match[4]].filter(Boolean).join(' ')
}

/** "30100" → "301 00" (same normalisation as the backend). */
export function formatPostalCode(value: string): string {
  const compact = value.replace(/[\s-]/g, '')
  return /^\d{5}$/.test(compact) ? `${compact.slice(0, 3)} ${compact.slice(3)}` : value
}

export function pluralize(count: number, one: string, few: string, many: string): string {
  if (count === 1) return one
  if (count >= 2 && count <= 4) return few
  return many
}

const statusFromDetail = (detail: string | null): string => {
  // "Pending→Approved" or "Pending→Approved;bulk"
  const target = detail?.split(';')[0]?.split('→')[1]?.toLowerCase() as ClinicStatus | undefined
  return target && target in statusLabels ? statusLabels[target].toLowerCase() : 'změněn'
}

/** Display names of the registration levels by their internal code (codes never change). */
export const levelNames: Record<string, string> = { basic: 'Úroveň 1', standard: 'Úroveň 2', premium: 'Úroveň 3' }
export const levelName = (code: string | null | undefined): string => (code ? (levelNames[code] ?? code) : '')

const by = (item: { administratorUsername?: string | null }) => (item.administratorUsername ? ` (${item.administratorUsername})` : '')

/** Czech names of audit events (mirrors AuditEndpoints.Labels on the server). */
export const activityLabels: Record<ActivityType, string> = {
  clinicRegistered: 'Registrace kliniky',
  clinicEmailVerified: 'Ověření e-mailu',
  clinicSignedIn: 'Přihlášení kliniky',
  clinicSignedOut: 'Odhlášení kliniky',
  clinicProfileUpdated: 'Úprava údajů kliniky',
  clinicStatusChanged: 'Změna stavu registrace',
  clinicMessageSent: 'Zpráva od kliniky',
  confirmationDownloaded: 'Stažení potvrzení (PDF)',
  adminNoteAdded: 'Interní poznámka',
  adminSignedIn: 'Přihlášení administrátora',
  adminSignedOut: 'Odhlášení administrátora',
  adminSignInFailed: 'Neúspěšné přihlášení administrátora',
  adminMessageSent: 'Zpráva od administrátora',
  adminTwoFactorEnabled: 'Zapnutí dvoufázového ověření',
  adminTwoFactorDisabled: 'Vypnutí dvoufázového ověření',
  adminRecoveryCodesRenewed: 'Nové záložní kódy',
  adminPasswordReset: 'Reset hesla administrátora',
  auditLogExported: 'Export audit logu',
  emailSent: 'Odeslaný e-mail',
  emailFailed: 'Neodeslaný e-mail',
  demoDataSeeded: 'Nahrání demo dat',
  demoDataRemoved: 'Odstranění demo dat',
  clinicDeleted: 'Smazání kliniky',
}

export const auditActorLabels: Record<AuditActor, string> = {
  clinic: 'Klinika',
  administrator: 'Administrátor',
  system: 'Systém',
}

const emailKindLabels: Record<string, string> = {
  StatusChanged: 'změna stavu',
  NewMessage: 'nová zpráva',
}

/** Human-readable detail of an audit event (raw details are machine-friendly, e.g. "Pending→Approved;bulk"). */
export function describeAuditDetail(type: ActivityType, detail: string | null): string {
  if (!detail) return ''
  switch (type) {
    case 'clinicStatusChanged': {
      const [change, flag] = detail.split(';')
      const [from, to] = (change ?? '').split('→').map((s) => statusLabels[s?.toLowerCase() as ClinicStatus] ?? s)
      return `${from} → ${to}${flag === 'bulk' ? ' (hromadně)' : ''}`
    }
    case 'clinicRegistered':
      return levelName(detail)
    case 'emailSent':
    case 'emailFailed':
      return emailKindLabels[detail.split(';')[0] ?? ''] ?? detail
    case 'adminSignedIn':
      return detail === '2fa' ? 'heslo + kód z aplikace' : detail === 'recovery-code' ? 'heslo + záložní kód' : 'heslo'
    case 'adminSignInFailed':
      return (
        {
          'unknown-user': 'neznámý uživatel',
          password: 'chybné heslo',
          'password;locked': 'chybné heslo, účet zamčen',
          '2fa': 'chybný kód z aplikace',
          '2fa;locked': 'chybný kód, účet zamčen',
          'recovery-code': 'chybný záložní kód',
        } as Record<string, string>
      )[detail] ?? detail
    case 'clinicProfileUpdated':
      return detail === 'address' ? 'včetně adresy' : detail
    default:
      return detail.replace('rows=', 'řádků: ').replace('clinics=', 'klinik: ').replace('maintenance', 'údržbový příkaz')
  }
}

/** Human-readable Czech description of an activity event (clinic name is rendered separately). */
export function describeActivity(item: ActivityItem): string {
  switch (item.type) {
    case 'clinicRegistered':
      return `Nová registrace${item.detail ? ` (${levelName(item.detail)})` : ''}`
    case 'clinicEmailVerified':
      return 'Ověřený e-mail'
    case 'clinicSignedIn':
      return 'Přihlášení kliniky'
    case 'clinicProfileUpdated':
      return 'Klinika upravila své údaje'
    case 'clinicStatusChanged':
      return `Stav: ${statusFromDetail(item.detail)}${item.detail?.endsWith(';bulk') ? ', hromadně' : ''}${by(item)}`
    case 'clinicMessageSent':
      return 'Nová zpráva od kliniky'
    case 'adminMessageSent':
      return `Odpověď administrátora${by(item)}`
    case 'adminNoteAdded':
      return `Interní poznámka${by(item)}`
    default:
      return activityLabels[item.type]
  }
}

/** "3 h", "2,5 dne" – duration given in hours. */
export function formatHours(hours: number | null | undefined): string {
  if (hours === null || hours === undefined) return '—'
  if (hours < 1) return `${Math.max(1, Math.round(hours * 60))} min`
  if (hours < 48) return `${new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 1 }).format(hours)} h`
  return `${new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 1 }).format(hours / 24)} dne`
}
