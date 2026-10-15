/**
 * Thin fetch wrapper.
 * - Cookies (HttpOnly session cookies) are sent with `credentials: 'include'`; no tokens are stored in localStorage.
 * - Unsafe requests carry the antiforgery token in X-CSRF-TOKEN (kept in memory only).
 * - Errors are normalised into ApiError with field errors from RFC 7807 validation problems.
 */

// Empty by default: the SPA talks to the API on the same origin (/api), proxied by Nginx or Vite.
const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

/** Absolute URL for links that the browser opens itself (file downloads, the realtime hub). */
export const apiUrl = (path: string): string => `${API_BASE}${path}`

export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined
  readonly fieldErrors: Record<string, string>

  constructor(status: number, message: string, code?: string, fieldErrors: Record<string, string> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }
}

let csrfToken: string | null = null
let csrfPromise: Promise<string> | null = null

async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken
  csrfPromise ??= fetch(`${API_BASE}/api/security/csrf`, { credentials: 'include' })
    .then(async (res) => {
      if (!res.ok) throw new ApiError(res.status, 'Nepodařilo se navázat zabezpečené spojení se serverem.')
      const body = (await res.json()) as { token: string }
      csrfToken = body.token
      return body.token
    })
    .finally(() => {
      csrfPromise = null
    })
  return csrfPromise
}

/** Forget the cached token (e.g. after login/logout or when the server rejects it). */
export function resetCsrfToken(): void {
  csrfToken = null
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

interface ProblemBody {
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
}

async function toApiError(res: Response): Promise<ApiError> {
  let body: ProblemBody = {}
  try {
    body = (await res.json()) as ProblemBody
  } catch {
    // Non-JSON error (e.g. proxy error page).
  }
  const fieldErrors: Record<string, string> = {}
  for (const [key, messages] of Object.entries(body.errors ?? {})) {
    if (messages[0]) fieldErrors[key] = messages[0]
  }
  const fallback =
    res.status === 429
      ? 'Příliš mnoho pokusů. Zkuste to prosím později.'
      : res.status >= 500
        ? 'Server je dočasně nedostupný. Zkuste to prosím znovu.'
        : 'Požadavek se nepodařilo zpracovat.'
  return new ApiError(res.status, body.detail ?? body.title ?? fallback, body.code, fieldErrors)
}

export async function apiRequest<T>(method: Method, path: string, body?: unknown, retried = false): Promise<T> {
  const unsafe = method !== 'GET'
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (unsafe) headers['X-CSRF-TOKEN'] = await getCsrfToken()

  let res: Response
  try {
    res = await fetch(`${API_BASE}${path}`, {
      method,
      headers,
      credentials: 'include',
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, 'Nepodařilo se spojit se serverem. Zkontrolujte připojení a zkuste to znovu.')
  }

  if (!res.ok) {
    const error = await toApiError(res)
    if (unsafe && !retried && error.status === 403 && error.code === 'csrf_invalid') {
      resetCsrfToken()
      return apiRequest<T>(method, path, body, true)
    }
    throw error
  }

  if (res.status === 204 || res.headers.get('Content-Length') === '0') return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const http = {
  get: <T>(path: string) => apiRequest<T>('GET', path),
  post: <T>(path: string, body?: unknown) => apiRequest<T>('POST', path, body ?? {}),
  put: <T>(path: string, body?: unknown) => apiRequest<T>('PUT', path, body ?? {}),
}
