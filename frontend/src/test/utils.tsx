import { render } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { vi } from 'vitest'
import { AppProviders, routes } from '../App'
import { resetCsrfToken } from '../api/client'
import type { Plan } from '../api/types'
import { resetPlansCache } from '../lib/usePlans'

export interface MockResponse {
  status?: number
  body?: unknown
}

type Handler = (body: unknown, url: URL) => MockResponse | Promise<MockResponse>

/**
 * Replaces global fetch with a tiny router keyed by "METHOD /path".
 * Returns the mock so tests can inspect calls.
 */
export function mockApi(handlers: Record<string, Handler>) {
  resetCsrfToken()
  resetPlansCache()
  const all: Record<string, Handler> = {
    'GET /api/security/csrf': () => ({ body: { token: 'test-csrf' } }),
    ...handlers,
  }
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost')
    const method = (init?.method ?? 'GET').toUpperCase()
    const handler = all[`${method} ${url.pathname}`]
    if (!handler) {
      return new Response(JSON.stringify({ title: 'Not mocked' }), { status: 404 })
    }
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    const result = await handler(body, url)
    const status = result.status ?? 200
    return new Response(status === 204 ? null : JSON.stringify(result.body ?? {}), {
      status,
      headers: { 'Content-Type': 'application/json' },
    })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

export function callsTo(fetchMock: ReturnType<typeof mockApi>, method: string, path: string) {
  return fetchMock.mock.calls.filter(([input, init]) => {
    const url = new URL(String(input), 'http://localhost')
    return url.pathname === path && (init?.method ?? 'GET').toUpperCase() === method
  })
}

export function renderRoute(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const result = render(
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>,
  )
  return { ...result, router }
}

export const testPlans: Plan[] = [
  {
    code: 'basic',
    name: 'Basic',
    tagline: 'Základní registrace kliniky',
    description: 'Popis Basic',
    features: [{ text: 'Cena: Zcela zdarma', included: true }],
    fieldGroups: [],
    highlighted: false,
    badge: null,
  },
  {
    code: 'standard',
    name: 'Standard',
    tagline: 'Registrace s provozním kontaktem',
    description: 'Popis Standard',
    features: [
      { text: 'Cena: 50 Kč za požadavek', included: true },
      { text: 'Přímá rezervace v aplikaci: Není dostupná', included: false },
    ],
    fieldGroups: ['operationalContact'],
    highlighted: true,
    badge: 'Doporučený',
  },
  {
    code: 'premium',
    name: 'Premium',
    tagline: 'Registrace s konfigurací služby',
    description: 'Popis Premium',
    features: [{ text: 'Cena: 100 Kč za rezervaci', included: true }],
    fieldGroups: ['operationalContact', 'serviceConfiguration'],
    highlighted: false,
    badge: null,
  },
]

export const pendingProfile = {
  id: '0199aaaa-0000-7000-8000-000000000001',
  name: 'Ordinace Testovací',
  organizationId: '25596641',
  contactFirstName: 'Jana',
  contactLastName: 'Nováková',
  email: 'jana@example.test',
  phone: '+420777123456',
  street: 'Náměstí 1',
  city: 'Plzeň',
  postalCode: '301 00',
  plan: { code: 'basic', name: 'Basic', tagline: 'Základní registrace kliniky', features: ['Registrace kliniky'] },
  operationalContact: null,
  serviceConfiguration: null,
  status: 'pending',
  statusMessage: null,
  statusChangedAt: null,
  createdAt: '2026-10-13T08:00:00Z',
  updatedAt: '2026-10-13T08:00:00Z',
  emailVerifiedAt: '2026-10-13T08:05:00Z',
}
