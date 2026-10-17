import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { callsTo, mockApi, pendingProfile, renderRoute, testPlans } from '../test/utils'

async function fillClinicStep(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Název kliniky'), 'Ordinace Testovací')
  await user.type(screen.getByLabelText('E-mail kontaktní osoby'), 'jana@example.test')
  await user.type(screen.getByLabelText('Jméno kontaktní osoby'), 'Jana')
  await user.type(screen.getByLabelText('Příjmení kontaktní osoby'), 'Nováková')
  await user.type(screen.getByLabelText('Telefon'), '777 123 456')
  await user.type(screen.getByLabelText('Ulice a číslo popisné'), 'Náměstí 1')
  await user.type(screen.getByLabelText('Obec'), 'Plzeň')
  await user.type(screen.getByLabelText('PSČ'), '301 00')
  await user.click(screen.getByRole('checkbox', { name: /oprávněn\(a\) jednat/ }))
}

const codeSent = () => ({ status: 202, body: { email: 'jana@example.test', codeLifetimeMinutes: 10, resendAfterSeconds: 60 } })

describe('Registration wizard', () => {
  it('follows the four steps: contact, e-mail verification and level, public contacts, summary', async () => {
    const user = userEvent.setup()
    const fetchMock = mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'POST /api/registrations/email-code': codeSent,
      'POST /api/registrations/email-code/verify': () => ({
        body: { email: 'jana@example.test', verificationToken: 'token-1', expiresAt: '2026-10-16T12:00:00Z' },
      }),
      'POST /api/registrations': () => ({ status: 201, body: { ...pendingProfile, plan: { ...pendingProfile.plan, name: 'Standard' } } }),
    })

    renderRoute('/registrace?plan=standard')

    // Step 1: contact and organization (the level from the URL is preselected for step 2).
    expect(await screen.findByRole('heading', { name: 'Kontakt a organizace' })).toBeInTheDocument()
    expect(screen.getByText('Krok 1 z 4: Kontakt a organizace')).toBeInTheDocument()
    expect(screen.getByText('Vybraná: Standard')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Poslat ověřovací kód/ }))
    expect((await screen.findAllByText('Toto pole je povinné.')).length).toBeGreaterThan(3)
    expect(screen.getByText('Pro registraci je nutné potvrdit oprávnění jednat jménem organizace.')).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/api/registrations/email-code')).toHaveLength(0)

    await fillClinicStep(user)
    await user.click(screen.getByRole('button', { name: /Poslat ověřovací kód/ }))

    // Step 2: verify the e-mail with the code, then choose the level.
    expect(await screen.findByRole('heading', { name: 'Ověření e-mailu a úroveň' })).toBeInTheDocument()
    const [sendCall] = callsTo(fetchMock, 'POST', '/api/registrations/email-code')
    expect(JSON.parse(String(sendCall![1]!.body))).toEqual({ email: 'jana@example.test' })

    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))
    expect(await screen.findByText('Nejprve ověřte e-mail kódem, který jsme vám poslali.')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Ověřovací kód (PIN)'), '123456')
    await user.click(screen.getByRole('button', { name: /Ověřit kód/ }))
    expect(await screen.findByText('E-mail ověřen')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))

    // Step 3: public contacts and the level-specific operational contact.
    expect(await screen.findByRole('heading', { name: 'Veřejné kontakty' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))
    expect(await screen.findByText('Uveďte alespoň jeden telefon nebo e-mail, na kterém se pacienti mohou objednat.')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Telefon pro objednání'), '377 123 456')
    await user.type(screen.getByLabelText('Jméno a příjmení'), 'Petr Svoboda')
    await user.type(screen.getByLabelText('E-mail'), 'provoz@example.test')
    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))

    // Step 4: summary keeps the data of all steps.
    expect(await screen.findByRole('heading', { name: 'Souhrn a odeslání' })).toBeInTheDocument()
    expect(screen.getByText('Ordinace Testovací')).toBeInTheDocument()
    expect(screen.getByText('jana@example.test (ověřen)')).toBeInTheDocument()
    expect(screen.getByText('377 123 456')).toBeInTheDocument()
    expect(screen.getByText('Petr Svoboda')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Odeslat registraci/ }))
    expect(await screen.findByText(/je nutné potvrdit seznámení/)).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/api/registrations')).toHaveLength(0)

    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: /Odeslat registraci/ }))

    expect(await screen.findByRole('heading', { name: 'Registrace byla odeslána' })).toBeInTheDocument()
    expect(screen.getByText(/potvrzení s PDF přehledem/)).toBeInTheDocument()
    const [call] = callsTo(fetchMock, 'POST', '/api/registrations')
    expect(JSON.parse(String(call![1]!.body))).toMatchObject({
      planCode: 'standard',
      name: 'Ordinace Testovací',
      postalCode: '301 00',
      emailVerificationToken: 'token-1',
      authorityConfirmed: true,
      publicPhones: ['377 123 456'],
      publicEmails: [],
      website: null,
      operationalContact: { name: 'Petr Svoboda', email: 'provoz@example.test', phone: null },
      serviceConfiguration: null,
      privacyNoticeAccepted: true,
    })
    expect((call![1]!.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('test-csrf')
  }, 20_000)

  it('always shows the same four steps', async () => {
    mockApi({ 'GET /api/plans': () => ({ body: testPlans }) })
    renderRoute('/registrace?plan=basic')
    expect(await screen.findByText('Krok 1 z 4: Kontakt a organizace')).toBeInTheDocument()
    const steps = within(screen.getByRole('list', { name: 'Kroky registrace' })).getAllByRole('listitem')
    expect(steps.map((s) => s.textContent)).toEqual(['Kontakt a organizace', 'Ověření e-mailu a úroveň', 'Veřejné kontakty', 'Souhrn a odeslání'])
  })

  it('stops at the first step when the e-mail is already registered', async () => {
    const user = userEvent.setup()
    mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'POST /api/registrations/email-code': () => ({
        status: 409,
        body: {
          detail: 'Klinika s tímto e-mailem je již registrována. Přihlaste se jednorázovým kódem.',
          code: 'email_taken',
          errors: { email: ['Klinika s tímto e-mailem je již registrována. Přihlaste se jednorázovým kódem.'] },
        },
      }),
    })

    renderRoute('/registrace')
    await screen.findByRole('heading', { name: 'Kontakt a organizace' })
    await fillClinicStep(user)
    await user.click(screen.getByRole('button', { name: /Poslat ověřovací kód/ }))

    expect(await screen.findByRole('link', { name: 'Přihlásit se kódem' })).toHaveAttribute('href', '/prihlaseni')
    expect(screen.getByRole('heading', { name: 'Kontakt a organizace' })).toBeInTheDocument()
    await waitFor(() => expect(screen.getByLabelText('E-mail kontaktní osoby')).toHaveAttribute('aria-invalid', 'true'))
  })

  it('shows the server message for a wrong code', async () => {
    const user = userEvent.setup()
    mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'POST /api/registrations/email-code': codeSent,
      'POST /api/registrations/email-code/verify': () => ({
        status: 400,
        body: { detail: 'Kód je neplatný nebo vypršela jeho platnost.', code: 'invalid_code' },
      }),
    })

    renderRoute('/registrace')
    await screen.findByRole('heading', { name: 'Kontakt a organizace' })
    await fillClinicStep(user)
    await user.click(screen.getByRole('button', { name: /Poslat ověřovací kód/ }))
    await user.type(await screen.findByLabelText('Ověřovací kód (PIN)'), '000000')
    await user.click(screen.getByRole('button', { name: /Ověřit kód/ }))

    expect(await screen.findByText('Kód je neplatný nebo vypršela jeho platnost.')).toBeInTheDocument()
    expect(screen.queryByText('E-mail ověřen')).not.toBeInTheDocument()
  })
})
