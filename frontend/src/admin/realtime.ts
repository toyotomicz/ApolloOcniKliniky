import { createContext, useContext, useEffect, useRef } from 'react'
import { pluralize } from '../lib/format'

/*
 * Live notifications for administrators: event types, the context and the hooks that pages use.
 * The SignalR connection itself lives in AdminRealtimeProvider (AdminRealtime.tsx).
 */

export interface RegistrationCreatedEvent {
  clinicId: string
  clinicName: string
  planCode: string
  city: string
  createdAt: string
}

export interface MessageReceivedEvent {
  clinicId: string
  clinicName: string
  createdAt: string
}

export interface ClinicsChangedEvent {
  clinicIds: string[]
  reason: string
}

export type RealtimeEvent =
  | ({ type: 'registrationCreated' } & RegistrationCreatedEvent)
  | ({ type: 'messageReceived' } & MessageReceivedEvent)
  | ({ type: 'clinicsChanged' } & ClinicsChangedEvent)

export type RealtimeStatus = 'connecting' | 'live' | 'offline'

export interface RealtimeApi {
  status: RealtimeStatus
  subscribe: (listener: (event: RealtimeEvent) => void) => () => void
}

export const RealtimeContext = createContext<RealtimeApi>({ status: 'offline', subscribe: () => () => undefined })

export function useRealtimeStatus(): RealtimeStatus {
  return useContext(RealtimeContext).status
}

/** Calls the handler for every realtime event while the component is mounted. */
export function useRealtimeEvents(handler: (event: RealtimeEvent) => void): void {
  const { subscribe } = useContext(RealtimeContext)
  const latest = useRef(handler)
  useEffect(() => {
    latest.current = handler
  })
  useEffect(() => subscribe((event) => latest.current(event)), [subscribe])
}

/** Short summary of a bulk result, e.g. "Změněno 3 kliniky, 1 přeskočena (stav již měla)". */
export function bulkSummary(updated: number, skipped: number): string {
  const changed = `Změněno ${updated} ${pluralize(updated, 'klinika', 'kliniky', 'klinik')}`
  return skipped > 0
    ? `${changed}, ${skipped} ${pluralize(skipped, 'přeskočena', 'přeskočeny', 'přeskočeno')} (stav již ${pluralize(skipped, 'měla', 'měly', 'mělo')})`
    : changed
}
