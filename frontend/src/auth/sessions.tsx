import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { ApiError, resetCsrfToken } from '../api/client'
import { adminAuthApi, clinicApi, clinicAuthApi } from '../api/endpoints'
import type { AdminMe, ClinicProfile } from '../api/types'
import { LoadingBlock } from '../components/ui'

/*
 * The browser cannot read the HttpOnly session cookies, so the session state is discovered by
 * calling the corresponding /me endpoint. Clinic and administrator sessions are independent.
 */

type Status = 'unknown' | 'loading' | 'authenticated' | 'anonymous'

export interface Session<T> {
  status: Status
  data: T | null
  ensureLoaded: () => void
  /** Reloads the session data; resolves to false if it could not be loaded. */
  refresh: () => Promise<boolean>
  setData: (data: T) => void
  /** Call when an API request returned 401 – the guard then redirects to the login page. */
  markSignedOut: () => void
  logout: () => Promise<void>
}

function createSession<T>(load: () => Promise<T>, logoutRequest: () => Promise<void>) {
  const Context = createContext<Session<T> | null>(null)

  function Provider({ children }: { children: ReactNode }) {
    const [status, setStatus] = useState<Status>('unknown')
    const [data, setDataState] = useState<T | null>(null)

    const refresh = useCallback(async () => {
      setStatus((s) => (s === 'authenticated' ? s : 'loading'))
      try {
        const result = await load()
        setDataState(result)
        setStatus('authenticated')
        return true
      } catch (error) {
        if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
          setDataState(null)
          setStatus('anonymous')
        } else {
          // Network/server problem: keep an existing session; an unknown one is treated as signed out.
          setStatus((s) => (s === 'authenticated' ? s : 'anonymous'))
        }
        return false
      }
    }, [])

    const started = useRef(false)
    const ensureLoaded = useCallback(() => {
      if (started.current) return
      started.current = true
      void refresh()
    }, [refresh])

    const setData = useCallback((value: T) => {
      started.current = true
      resetCsrfToken()
      setDataState(value)
      setStatus('authenticated')
    }, [])

    const markSignedOut = useCallback(() => {
      setDataState(null)
      setStatus('anonymous')
    }, [])

    const logout = useCallback(async () => {
      try {
        await logoutRequest()
      } finally {
        resetCsrfToken()
        setDataState(null)
        setStatus('anonymous')
      }
    }, [])

    const value = useMemo(
      () => ({ status, data, ensureLoaded, refresh, setData, markSignedOut, logout }),
      [status, data, ensureLoaded, refresh, setData, markSignedOut, logout],
    )
    return <Context.Provider value={value}>{children}</Context.Provider>
  }

  function useSession(): Session<T> {
    const context = useContext(Context)
    if (!context) throw new Error('Session provider is missing')
    return context
  }

  /** Route guard: renders nested routes only for an authenticated session. */
  function RequireSession({ loginPath }: { loginPath: string }) {
    const session = useSession()
    const location = useLocation()
    const { status, ensureLoaded } = session

    useEffect(() => {
      ensureLoaded()
    }, [ensureLoaded])

    if (status === 'authenticated') return <Outlet />
    if (status === 'anonymous') return <Navigate to={loginPath} replace state={{ from: location.pathname }} />
    return <LoadingBlock label="Ověřuji přihlášení…" />
  }

  return { Provider, useSession, RequireSession }
}

const clinicSession = createSession<ClinicProfile>(clinicApi.me, clinicAuthApi.logout)
const adminSession = createSession<AdminMe>(adminAuthApi.me, adminAuthApi.logout)

export const ClinicSessionProvider = clinicSession.Provider
export const useClinicSession = clinicSession.useSession
export const RequireClinic = () => <clinicSession.RequireSession loginPath="/prihlaseni" />

export const AdminSessionProvider = adminSession.Provider
export const useAdminSession = adminSession.useSession
export const RequireAdmin = () => <adminSession.RequireSession loginPath="/admin/prihlaseni" />
