import type { ActivityItem, ClinicStatus, OnboardingPreference } from '../api/types'

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
  const target = detail?.split('→')[1]?.toLowerCase() as ClinicStatus | undefined
  return target && target in statusLabels ? statusLabels[target].toLowerCase() : 'změněn'
}

/** Human-readable Czech description of an activity event (clinic name is rendered separately). */
export function describeActivity(item: ActivityItem): string {
  switch (item.type) {
    case 'clinicRegistered':
      return `Nová registrace${item.detail ? ` (plán ${item.detail.charAt(0).toUpperCase()}${item.detail.slice(1)})` : ''}`
    case 'clinicEmailVerified':
      return 'Ověřený e-mail'
    case 'clinicSignedIn':
      return 'Přihlášení kliniky'
    case 'clinicProfileUpdated':
      return 'Klinika upravila své údaje'
    case 'clinicStatusChanged':
      return `Stav: ${statusFromDetail(item.detail)}${item.administratorUsername ? ` (${item.administratorUsername})` : ''}`
    case 'adminNoteAdded':
      return `Interní poznámka${item.administratorUsername ? ` (${item.administratorUsername})` : ''}`
    case 'adminSignedIn':
      return 'Přihlášení administrátora'
  }
}
