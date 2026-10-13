import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { mockApi, pendingProfile, renderRoute } from '../test/utils'

describe('Clinic passwordless login', () => {
  it('requests a code, rejects a wrong one and opens the portal with pending status', async () => {
    const user = userEvent.setup()
    let signedIn = false
    mockApi({
      'GET /api/clinic/me': () => (signedIn ? { body: pendingProfile } : { status: 401 }),
      'POST /api/clinic-auth/request-code': () => ({
        status: 202,
        body: { message: 'ok', codeLifetimeMinutes: 10, resendAfterSeconds: 60 },
      }),
      'POST /api/clinic-auth/verify-code': (body) => {
        const { code } = body as { code: string }
        if (code !== '123456') {
          return { status: 400, body: { detail: 'Kód je neplatný nebo vypršela jeho platnost.', code: 'invalid_code' } }
        }
        signedIn = true
        return { body: pendingProfile }
      },
    })

    renderRoute('/prihlaseni')

    await user.type(await screen.findByLabelText('E-mail'), 'jana@example.test')
    await user.click(screen.getByRole('button', { name: /Poslat přihlašovací kód/ }))

    const codeInput = await screen.findByLabelText('Jednorázový kód')
    expect(screen.getByRole('button', { name: /Poslat nový kód \(60 s\)/ })).toBeDisabled()

    await user.type(codeInput, '12')
    await user.click(screen.getByRole('button', { name: /Ověřit kód/ }))
    expect(await screen.findByText('Kód má 6 číslic.')).toBeInTheDocument()

    await user.clear(codeInput)
    await user.type(codeInput, '000000')
    await user.click(screen.getByRole('button', { name: /Ověřit kód/ }))
    expect(await screen.findByText('Kód je neplatný nebo vypršela jeho platnost.')).toBeInTheDocument()

    await user.clear(codeInput)
    await user.type(codeInput, '123456')
    await user.click(screen.getByRole('button', { name: /Ověřit kód/ }))

    expect(await screen.findByRole('heading', { name: 'Registrace čeká na posouzení' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Ordinace Testovací', level: 1 })).toBeInTheDocument()
  })

  it('redirects anonymous visitors from the portal to the login page', async () => {
    mockApi({ 'GET /api/clinic/me': () => ({ status: 401 }) })
    renderRoute('/portal')
    expect(await screen.findByRole('heading', { name: 'Přihlášení' })).toBeInTheDocument()
  })

  it('shows the rejection reason in the portal', async () => {
    mockApi({
      'GET /api/clinic/me': () => ({
        body: { ...pendingProfile, status: 'rejected', statusMessage: 'Chybí platné IČO.', statusChangedAt: '2026-10-13T10:00:00Z' },
      }),
    })
    renderRoute('/portal')
    expect(await screen.findByRole('heading', { name: 'Registrace byla zamítnuta' })).toBeInTheDocument()
    expect(screen.getByText('Důvod zamítnutí')).toBeInTheDocument()
    expect(screen.getByText('Chybí platné IČO.')).toBeInTheDocument()
  })
})
