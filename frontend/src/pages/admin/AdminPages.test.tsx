import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { callsTo, mockApi, pendingProfile, renderRoute, testPlans } from '../../test/utils'

const admin = { id: 'a1', username: 'admin', lastLoginAt: null, twoFactorEnabled: false }

const dashboard = (seriesDays: number) => ({
  metrics: {
    totalClinics: 12,
    newToday: 2,
    pendingReview: 5,
    approved: 6,
    rejected: 1,
    uniqueClinicSignInsToday: 3,
    emailUnverified: 0,
    unreadMessages: 2,
    approvalRatePercent: 86,
    averageDecisionHours: 30.5,
  },
  funnel: { registered: 12, emailVerified: 10, approved: 6 },
  plans: [
    { planCode: 'basic', planName: 'Basic', count: 5 },
    { planCode: 'standard', planName: 'Standard', count: 4 },
    { planCode: 'premium', planName: 'Premium', count: 3 },
  ],
  seriesDays,
  registrations: Array.from({ length: seriesDays }, (_, i) => ({ date: `2026-09-${String(14 + i).padStart(2, '0')}`, count: i % 3 })),
  recentActivity: [],
  timeZone: 'Europe/Prague',
  generatedAt: '2026-10-13T12:00:00Z',
})

describe('Admin dashboard', () => {
  it('renders metrics, funnel, plan breakdown and switches the chart period', async () => {
    const user = userEvent.setup()
    const fetchMock = mockApi({
      'GET /api/admin-auth/me': () => ({ body: admin }),
      'GET /api/admin/dashboard': (_, url) => ({ body: dashboard(url.searchParams.get('days') === '30' ? 30 : 7) }),
    })

    renderRoute('/admin')

    expect(await screen.findByRole('heading', { name: 'Přehled' })).toBeInTheDocument()
    const pending = await screen.findByRole('link', { name: /Čeká na posouzení/ })
    expect(within(pending).getByText('5')).toBeInTheDocument()
    expect(pending).toHaveAttribute('href', '/admin/kliniky?status=pending')
    expect(screen.getByRole('link', { name: /Nepřečtené zprávy/ })).toHaveAttribute('href', '/admin/kliniky?unread=1')
    expect(screen.getByText('86 %')).toBeInTheDocument()
    expect(screen.getByText('30,5 h')).toBeInTheDocument()
    expect(within(screen.getByRole('list', { name: 'Počty klinik v jednotlivých krocích' })).getByText('83 %')).toBeInTheDocument()
    expect(screen.getByRole('list', { name: 'Počty klinik podle úrovně registrace' })).toHaveTextContent('Premium')
    expect(screen.getAllByTestId('chart-bar')).toHaveLength(7)
    expect(screen.getByText('Zatím žádná aktivita')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '30 dní' }))
    expect(await screen.findByRole('heading', { name: 'Registrace za posledních 30 dní' })).toBeInTheDocument()
    expect(screen.getAllByTestId('chart-bar')).toHaveLength(30)
    expect(callsTo(fetchMock, 'GET', '/api/admin/dashboard').some(([input]) => String(input).includes('days=30'))).toBe(true)
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
      location: null,
      isDemo: false,
    }
    const fetchMock = mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'GET /api/admin-auth/me': () => ({ body: admin }),
      [`GET /api/admin/clinics/${pendingProfile.id}`]: () => ({ body: detail }),
      [`GET /api/admin/clinics/${pendingProfile.id}/messages`]: () => ({ body: [] }),
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

describe('Admin bulk actions', () => {
  it('changes the status of the selected clinics after confirmation', async () => {
    const user = userEvent.setup()
    const item = (id: string, name: string) => ({
      id,
      name,
      email: `${id}@example.test`,
      organizationId: null,
      city: 'Plzeň',
      planCode: 'basic',
      status: 'pending',
      emailVerified: true,
      createdAt: '2026-10-13T08:00:00Z',
      unreadMessages: id === 'c2' ? 2 : 0,
    })
    const fetchMock = mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'GET /api/admin-auth/me': () => ({ body: admin }),
      'GET /api/admin/clinics': () => ({
        body: {
          items: [item('c1', 'Oční klinika Lumen'), item('c2', 'Oční centrum Iris'), item('c3', 'Oftalmologie Vizus')],
          page: 1,
          pageSize: 20,
          totalCount: 3,
          totalPages: 1,
        },
      }),
      'POST /api/admin/clinics/bulk-status': () => ({ body: { updated: ['c1', 'c2'], skipped: [], notFound: [] } }),
    })

    renderRoute('/admin/kliniky')

    await user.click(await screen.findByRole('checkbox', { name: 'Vybrat Oční klinika Lumen' }))
    await user.click(screen.getByRole('checkbox', { name: 'Vybrat Oční centrum Iris' }))
    expect(screen.getByText(/2 nové/)).toBeInTheDocument()
    const bar = screen.getByRole('region', { name: 'Hromadné akce' })
    expect(bar).toHaveTextContent('Vybráno 2 kliniky')

    await user.click(within(bar).getByRole('button', { name: /Zamítnout/ }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Zamítnout' }))
    expect(await within(dialog).findByText(/Uveďte důvod zamítnutí/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Zrušit' }))

    await user.click(within(bar).getByRole('button', { name: /Schválit/ }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Schválit' }))

    expect(await screen.findByText('Změněno 2 kliniky.')).toBeInTheDocument()
    const [call] = callsTo(fetchMock, 'POST', '/api/admin/clinics/bulk-status')
    expect(JSON.parse(String(call![1]!.body))).toEqual({ clinicIds: ['c1', 'c2'], status: 'approved', message: null })
  })
})

describe('Admin two-factor login', () => {
  it('asks for the authenticator code after a correct password', async () => {
    const user = userEvent.setup()
    let signedIn = false
    const fetchMock = mockApi({
      'GET /api/admin-auth/me': () => (signedIn ? { body: { ...admin, twoFactorEnabled: true } } : { status: 401 }),
      'POST /api/admin-auth/login': () => ({ status: 202, body: { twoFactorRequired: true, expiresInSeconds: 300 } }),
      'POST /api/admin-auth/login/two-factor': (body) => {
        if ((body as { code?: string }).code !== '123456') return { status: 401, body: { detail: 'Ověřovací kód není platný.' } }
        signedIn = true
        return { body: { ...admin, twoFactorEnabled: true } }
      },
      'GET /api/admin/dashboard': () => ({ body: dashboard(7) }),
    })

    renderRoute('/admin/prihlaseni')
    await user.type(await screen.findByLabelText('Uživatelské jméno'), 'admin')
    await user.type(screen.getByLabelText('Heslo'), 'Spravne-Heslo-123')
    await user.click(screen.getByRole('button', { name: /Přihlásit se/ }))

    expect(await screen.findByRole('heading', { name: /Dvoufázové ověření/ })).toBeInTheDocument()
    await user.type(screen.getByLabelText('Kód z aplikace'), '000000')
    await user.click(screen.getByRole('button', { name: /Ověřit a přihlásit/ }))
    expect(await screen.findByText('Ověřovací kód není platný.')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Kód z aplikace'), '123456')
    await user.click(screen.getByRole('button', { name: /Ověřit a přihlásit/ }))
    expect(await screen.findByRole('heading', { name: 'Přehled' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/api/admin-auth/login/two-factor')).toHaveLength(2)
  })
})
