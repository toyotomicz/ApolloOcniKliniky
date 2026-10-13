import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { callsTo, mockApi, pendingProfile, renderRoute, testPlans } from '../../test/utils'

const admin = { id: 'a1', username: 'admin', lastLoginAt: null }

const days = ['2026-10-07', '2026-10-08', '2026-10-09', '2026-10-10', '2026-10-11', '2026-10-12', '2026-10-13']

describe('Admin dashboard', () => {
  it('renders persisted metrics, a 7-day chart and an empty activity state', async () => {
    mockApi({
      'GET /api/admin-auth/me': () => ({ body: admin }),
      'GET /api/admin/dashboard': () => ({
        body: {
          metrics: { totalClinics: 12, newToday: 2, pendingReview: 5, approved: 6, rejected: 1, uniqueClinicSignInsToday: 3, emailUnverified: 0 },
          registrationsLast7Days: days.map((date, i) => ({ date, count: i === 6 ? 2 : i % 2 })),
          recentActivity: [],
          timeZone: 'Europe/Prague',
          generatedAt: '2026-10-13T12:00:00Z',
        },
      }),
    })

    renderRoute('/admin')

    expect(await screen.findByRole('heading', { name: 'Přehled' })).toBeInTheDocument()
    const pending = await screen.findByRole('link', { name: /Čeká na posouzení/ })
    expect(within(pending).getByText('5')).toBeInTheDocument()
    expect(pending).toHaveAttribute('href', '/admin/kliniky?status=pending')
    expect(screen.getAllByTestId('chart-bar')).toHaveLength(7)
    expect(screen.getByText('Zatím žádná aktivita')).toBeInTheDocument()
  })

  it('shows an error state when statistics cannot be loaded', async () => {
    mockApi({
      'GET /api/admin-auth/me': () => ({ body: admin }),
      'GET /api/admin/dashboard': () => ({ status: 500, body: { detail: 'Omlouváme se, požadavek se nepodařilo zpracovat.' } }),
    })
    renderRoute('/admin')
    expect(await screen.findByText('Přehled se nepodařilo načíst')).toBeInTheDocument()
  })

  it('sends anonymous users to the administrator login', async () => {
    mockApi({ 'GET /api/admin-auth/me': () => ({ status: 401 }) })
    renderRoute('/admin/kliniky')
    expect(await screen.findByRole('heading', { name: 'Přihlášení administrátora' })).toBeInTheDocument()
  })
})

describe('Admin clinic detail', () => {
  it('requires a reason before rejecting and sends the version for concurrency control', async () => {
    const user = userEvent.setup()
    const detail = {
      profile: pendingProfile,
      version: 4711,
      privacyNoticeAcceptedAt: '2026-10-13T08:00:00Z',
      privacyNoticeVersion: '2026-10-demo',
      lastSignInAt: null,
      allowedStatusChanges: ['approved', 'rejected'],
      statusHistory: [],
      notes: [],
    }
    const fetchMock = mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'GET /api/admin-auth/me': () => ({ body: admin }),
      [`GET /api/admin/clinics/${pendingProfile.id}`]: () => ({ body: detail }),
      [`POST /api/admin/clinics/${pendingProfile.id}/status`]: (body) => ({
        body: {
          ...detail,
          profile: { ...pendingProfile, status: 'rejected', statusMessage: (body as { message: string }).message },
          allowedStatusChanges: ['approved', 'pending'],
          statusHistory: [
            { id: 's1', fromStatus: 'pending', toStatus: 'rejected', changedAt: '2026-10-13T12:00:00Z', reason: 'Chybí IČO.', administratorUsername: 'admin' },
          ],
        },
      }),
    })

    renderRoute(`/admin/kliniky/${pendingProfile.id}`)

    await user.click(await screen.findByRole('button', { name: /Zamítnout registraci/ }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Zamítnout registraci' }))
    expect(await within(dialog).findByText(/Uveďte důvod zamítnutí/)).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', `/api/admin/clinics/${pendingProfile.id}/status`)).toHaveLength(0)

    await user.type(within(dialog).getByLabelText(/Důvod zamítnutí/), 'Chybí IČO.')
    await user.type(within(dialog).getByLabelText(/Interní poznámka/), 'Ověřit telefonicky')
    await user.click(within(dialog).getByRole('button', { name: 'Zamítnout registraci' }))

    expect(await screen.findByText('Stav kliniky změněn na „Zamítnuto“.')).toBeInTheDocument()
    const [call] = callsTo(fetchMock, 'POST', `/api/admin/clinics/${pendingProfile.id}/status`)
    expect(JSON.parse(String(call![1]!.body))).toEqual({
      status: 'rejected',
      message: 'Chybí IČO.',
      internalNote: 'Ověřit telefonicky',
      version: 4711,
    })
    expect(screen.getByRole('button', { name: /Vrátit k posouzení/ })).toBeInTheDocument()
  })
})
