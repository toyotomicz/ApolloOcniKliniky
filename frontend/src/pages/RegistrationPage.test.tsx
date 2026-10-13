import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { callsTo, mockApi, renderRoute, testPlans } from '../test/utils'

async function fillClinicStep(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Název kliniky'), 'Ordinace Testovací')
  await user.type(screen.getByLabelText('E-mail pro přihlášení'), 'jana@example.test')
  await user.type(screen.getByLabelText('Jméno kontaktní osoby'), 'Jana')
  await user.type(screen.getByLabelText('Příjmení kontaktní osoby'), 'Nováková')
  await user.type(screen.getByLabelText('Telefon'), '777 123 456')
  await user.type(screen.getByLabelText('Ulice a číslo popisné'), 'Náměstí 1')
  await user.type(screen.getByLabelText('Obec'), 'Plzeň')
  await user.type(screen.getByLabelText('PSČ'), '301 00')
}

describe('Registration wizard', () => {
  it('validates each step, keeps data between steps and submits plan-specific fields', async () => {
    const user = userEvent.setup()
    const fetchMock = mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'POST /api/registrations': () => ({ status: 201, body: { email: 'jana@example.test', verificationEmailSent: true, codeLifetimeMinutes: 10 } }),
    })

    renderRoute('/registrace?plan=standard')

    // Plan preselected from the URL → starts on the clinic step; Standard adds a 4th step.
    expect(await screen.findByRole('heading', { name: 'Klinika a kontakt' })).toBeInTheDocument()
    expect(screen.getByText('Krok 2 z 4: Klinika a kontakt')).toBeInTheDocument()
    expect(screen.getByText('Vybraný plán: Standard')).toBeInTheDocument()

    // Empty step cannot be submitted.
    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))
    expect((await screen.findAllByText('Toto pole je povinné.')).length).toBeGreaterThan(3)

    await fillClinicStep(user)
    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))

    expect(await screen.findByRole('heading', { name: 'Údaje podle plánu' })).toBeInTheDocument()
    expect(screen.getByText('Provozní kontakt')).toBeInTheDocument()
    expect(screen.queryByText('Konfigurace služby')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))
    expect(await screen.findByText('Zadejte e-mailovou adresu.')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Jméno a příjmení'), 'Petr Svoboda')
    await user.type(screen.getByLabelText('E-mail'), 'provoz@example.test')
    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))

    // Summary shows data from earlier steps (state preserved).
    expect(await screen.findByRole('heading', { name: 'Souhrn a odeslání' })).toBeInTheDocument()
    expect(screen.getByText('Ordinace Testovací')).toBeInTheDocument()
    expect(screen.getByText('Petr Svoboda')).toBeInTheDocument()

    // Privacy confirmation is required.
    await user.click(screen.getByRole('button', { name: /Odeslat registraci/ }))
    expect(await screen.findByText(/je nutné potvrdit seznámení/)).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/api/registrations')).toHaveLength(0)

    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: /Odeslat registraci/ }))

    expect(await screen.findByRole('heading', { name: 'Ověřte e-mail' })).toBeInTheDocument()
    const [call] = callsTo(fetchMock, 'POST', '/api/registrations')
    const body = JSON.parse(String(call![1]!.body))
    expect(body).toMatchObject({
      planCode: 'standard',
      name: 'Ordinace Testovací',
      postalCode: '301 00',
      operationalContact: { name: 'Petr Svoboda', email: 'provoz@example.test', phone: null },
      serviceConfiguration: null,
      privacyNoticeAccepted: true,
    })
    expect((call![1]!.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('test-csrf')
  }, 15_000)

  it('Basic plan has no plan-specific step', async () => {
    mockApi({ 'GET /api/plans': () => ({ body: testPlans }) })
    renderRoute('/registrace?plan=basic')
    expect(await screen.findByText('Krok 2 z 3: Klinika a kontakt')).toBeInTheDocument()
    const steps = screen.getByRole('list', { name: 'Kroky registrace' })
    expect(within(steps).queryByText('Údaje podle plánu')).not.toBeInTheDocument()
  })

  it('maps server-side field errors back to the right step', async () => {
    const user = userEvent.setup()
    mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'POST /api/registrations': () => ({
        status: 409,
        body: {
          detail: 'Klinika s tímto e-mailem je již registrována. Přihlaste se jednorázovým kódem.',
          code: 'email_taken',
          errors: { email: ['Klinika s tímto e-mailem je již registrována.'] },
        },
      }),
    })

    renderRoute('/registrace?plan=basic')
    await screen.findByRole('heading', { name: 'Klinika a kontakt' })
    await fillClinicStep(user)
    await user.click(screen.getByRole('button', { name: /Pokračovat/ }))
    await user.click(await screen.findByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: /Odeslat registraci/ }))

    expect(await screen.findByRole('heading', { name: 'Klinika a kontakt' })).toBeInTheDocument()
    expect(screen.getByText('Klinika s tímto e-mailem je již registrována.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByLabelText('E-mail pro přihlášení')).toHaveAttribute('aria-invalid', 'true'))
    expect(screen.getByRole('link', { name: 'Přihlásit se kódem' })).toHaveAttribute('href', '/prihlaseni')
  })
})
