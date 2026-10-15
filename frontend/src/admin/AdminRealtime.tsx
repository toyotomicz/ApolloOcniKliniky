import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { apiUrl } from '../api/client'
import { useToast } from '../components/Toast'
import {
  RealtimeContext,
  useRealtimeStatus,
  type ClinicsChangedEvent,
  type MessageReceivedEvent,
  type RealtimeApi,
  type RealtimeEvent,
  type RealtimeStatus,
  type RegistrationCreatedEvent,
} from './realtime'

/*
 * SignalR connection for administrators (WebSockets, falling back to long polling). The hub uses the admin
 * session cookie; events only tell the UI that something happened – data is always (re)loaded via the REST API.
 */

// Unit tests render the admin pages without a server; they do not open a connection.
const ENABLED = import.meta.env.MODE !== 'test'

export function AdminRealtimeProvider({ children }: { children: ReactNode }) {
  const toast = useToast()
  const navigate = useNavigate()
  const [status, setStatus] = useState<RealtimeStatus>(ENABLED ? 'connecting' : 'offline')
  const listeners = useRef(new Set<(event: RealtimeEvent) => void>())
  // Keep the latest callbacks without reconnecting when they change.
  const notify = useRef({ toast, navigate })
  useEffect(() => {
    notify.current = { toast, navigate }
  }, [toast, navigate])

  useEffect(() => {
    if (!ENABLED) return
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(apiUrl('/hubs/admin'), { withCredentials: true })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.None)
      .build()

    const emit = (event: RealtimeEvent) => listeners.current.forEach((listener) => listener(event))

    connection.on('registrationCreated', (e: RegistrationCreatedEvent) => {
      emit({ type: 'registrationCreated', ...e })
      notify.current.toast.show(`Nová registrace: ${e.clinicName} (${e.city})`, 'info', {
        label: 'Otevřít',
        onClick: () => notify.current.navigate(`/admin/kliniky/${e.clinicId}`),
      })
    })
    connection.on('messageReceived', (e: MessageReceivedEvent) => {
      emit({ type: 'messageReceived', ...e })
      notify.current.toast.show(`Nová zpráva od kliniky ${e.clinicName}`, 'info', {
        label: 'Přečíst',
        onClick: () => notify.current.navigate(`/admin/kliniky/${e.clinicId}#zpravy`),
      })
    })
    connection.on('clinicsChanged', (e: ClinicsChangedEvent) => emit({ type: 'clinicsChanged', ...e }))

    connection.onreconnecting(() => setStatus('connecting'))
    connection.onreconnected(() => setStatus('live'))
    connection.onclose(() => setStatus('offline'))

    let cancelled = false
    let retryTimer: number | undefined
    const start = async (attempt: number) => {
      try {
        await connection.start()
        if (!cancelled) setStatus('live')
      } catch {
        if (cancelled) return
        setStatus('offline')
        // The initial connection is not covered by automatic reconnect: retry with back-off.
        retryTimer = window.setTimeout(() => void start(attempt + 1), Math.min(30000, 2000 * 2 ** attempt))
      }
    }
    void start(0)

    return () => {
      cancelled = true
      window.clearTimeout(retryTimer)
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [])

  const api = useMemo<RealtimeApi>(
    () => ({
      status,
      subscribe: (listener) => {
        listeners.current.add(listener)
        return () => listeners.current.delete(listener)
      },
    }),
    [status],
  )

  return <RealtimeContext.Provider value={api}>{children}</RealtimeContext.Provider>
}

export function RealtimeIndicator() {
  const status = useRealtimeStatus()
  const label = { live: 'Živě', connecting: 'Připojuji…', offline: 'Offline' }[status]
  const title = {
    live: 'Nové registrace a zprávy se zobrazují okamžitě.',
    connecting: 'Navazuji spojení pro okamžité novinky.',
    offline: 'Okamžité novinky nejsou dostupné; data obnovíte tlačítkem Obnovit.',
  }[status]
  return (
    <span className="realtime-indicator" data-status={status} title={title}>
      <span className="realtime-dot" aria-hidden="true" />
      {label}
    </span>
  )
}
