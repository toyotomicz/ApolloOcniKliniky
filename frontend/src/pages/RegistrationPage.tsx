import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, ArrowRight, CircleCheck, Download, MailCheck, RotateCw, Send, ShieldCheck } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type RefObject } from 'react'
import { FormProvider, useForm, useFormContext, type Path, type Resolver } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { ApiError } from '../api/client'
import { clinicApi, publicApi } from '../api/endpoints'
import type { ClinicProfile, Plan } from '../api/types'
import { useClinicSession } from '../auth/sessions'
import {
  AddressFields,
  ContactPersonFields,
  OperationalContactFields,
  PublicContactFields,
  ServiceConfigurationFields,
} from '../components/ClinicFormFields'
import { planRequirementText } from '../components/PlanCard'
import { Alert, DefinitionList, Field, LoadingBlock, Spinner } from '../components/ui'
import { formatPhone, formatPostalCode, onboardingLabels } from '../lib/format'
import { usePlans } from '../lib/usePlans'
import { emptyContact, formFieldPath, registrationSchema, toRegistrationPayload, type RegistrationFormValues } from '../lib/validation'

type StepKey = 'clinic' | 'verify' | 'contacts' | 'summary'
interface Step {
  key: StepKey
  title: string
  fields: Path<RegistrationFormValues>[]
}

const operationalFields: Path<RegistrationFormValues>[] = ['operationalContact.name', 'operationalContact.email', 'operationalContact.phone']
const serviceFields: Path<RegistrationFormValues>[] = [
  'serviceConfiguration.workplaceCount',
  'serviceConfiguration.onboardingPreference',
  'serviceConfiguration.technicalContactEmail',
  'serviceConfiguration.notes',
]

/**
 * The four steps described on the landing page ("Jak to funguje"). Level-specific fields are part of the
 * contacts step, so the number of steps never changes with the selected level.
 */
export function buildSteps(plan: Plan | undefined): Step[] {
  return [
    {
      key: 'clinic',
      title: 'Kontakt a organizace',
      fields: ['name', 'organizationId', 'email', 'contactFirstName', 'contactLastName', 'phone', 'street', 'city', 'postalCode', 'authorityConfirmed'],
    },
    { key: 'verify', title: 'Ověření e-mailu a úroveň', fields: ['planCode'] },
    {
      key: 'contacts',
      title: 'Veřejné kontakty',
      fields: [
        'publicContacts',
        'website',
        'patientInfo',
        ...(plan?.fieldGroups.includes('operationalContact') ? operationalFields : []),
        ...(plan?.fieldGroups.includes('serviceConfiguration') ? serviceFields : []),
      ],
    },
    { key: 'summary', title: 'Souhrn a odeslání', fields: ['privacyNoticeAccepted'] },
  ]
}

const normalizeEmail = (email: string) => email.trim().toLowerCase()

export function RegistrationPage() {
  const { plans, error, retry } = usePlans()
  const [searchParams] = useSearchParams()

  return (
    <div className="surface-bg">
      <div className="container page page-narrow">
        {error && (
          <Alert kind="error" title="Registraci nelze načíst">
            <p>{error}</p>
            <button type="button" className="btn btn-sm" onClick={() => void retry()}>
              Zkusit znovu
            </button>
          </Alert>
        )}
        {!plans && !error && <LoadingBlock />}
        {plans && <RegistrationWizard plans={plans} initialPlanCode={searchParams.get('plan')} />}
      </div>
    </div>
  )
}

/** E-mail verification state: kept outside the form so it survives moving between steps. */
interface VerificationState {
  /** Normalized address the last code was sent to. */
  codeEmail: string | null
  /** Time (ms) from which another code may be requested. */
  resendAt: number
  /** Registration token for `tokenEmail`, issued after the code was confirmed. */
  token: string | null
  tokenEmail: string | null
}

function RegistrationWizard({ plans, initialPlanCode }: { plans: Plan[]; initialPlanCode: string | null }) {
  const session = useClinicSession()
  const initialPlan = plans.find((p) => p.code === initialPlanCode)
  const [stepIndex, setStepIndex] = useState(0)
  const [registered, setRegistered] = useState<ClinicProfile | null>(null)
  const [stepError, setStepError] = useState<string | null>(null)
  const [registeredEmail, setRegisteredEmail] = useState<string | null>(null)
  const [verification, setVerification] = useState<VerificationState>({ codeEmail: null, resendAt: 0, token: null, tokenEmail: null })
  const [sendingCode, setSendingCode] = useState(false)
  const [codeNotice, setCodeNotice] = useState<string | null>(null)
  const [codeError, setCodeError] = useState<string | null>(null)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const firstRender = useRef(true)

  // The schema depends on the selected level, so it is resolved per validation run.
  const resolver: Resolver<RegistrationFormValues> = (values, context, options) => {
    const schema = registrationSchema(plans.find((p) => p.code === values.planCode))
    // Form values are plain strings/booleans; the schema output type (e.g. `privacyNoticeAccepted: true`) is stricter.
    const planResolver = zodResolver(schema) as unknown as Resolver<RegistrationFormValues>
    return planResolver(values, context, options)
  }

  const form = useForm<RegistrationFormValues>({
    resolver,
    mode: 'onTouched',
    // Values of steps that are not currently rendered are kept (default shouldUnregister: false).
    defaultValues: {
      ...emptyContact,
      planCode: initialPlan?.code ?? '',
      name: '',
      organizationId: '',
      email: '',
      authorityConfirmed: false,
      privacyNoticeAccepted: false,
    },
  })

  const planCode = form.watch('planCode')
  const plan = plans.find((p) => p.code === planCode)
  const steps = useMemo(() => buildSteps(plan), [plan])
  const step = steps[stepIndex]!
  const currentEmail = normalizeEmail(form.watch('email'))
  const verified = verification.token !== null && verification.tokenEmail === currentEmail

  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false
      return
    }
    headingRef.current?.focus()
  }, [stepIndex, registered])

  const goTo = (key: StepKey) => {
    setStepError(null)
    setStepIndex(steps.findIndex((s) => s.key === key))
  }

  /** Sends a registration code to the entered address. Returns false when the user has to fix something first. */
  const sendCode = async (): Promise<boolean> => {
    const { email, contactFirstName, contactLastName } = form.getValues()
    setSendingCode(true)
    setStepError(null)
    setCodeError(null)
    try {
      const response = await publicApi.sendRegistrationCode(email.trim(), `${contactFirstName} ${contactLastName}`.trim())
      setVerification((v) => ({ ...v, codeEmail: normalizeEmail(email), resendAt: Date.now() + response.resendAfterSeconds * 1000 }))
      setCodeNotice(null)
      return true
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setStepError('Ověřovací kód se nepodařilo odeslat.')
        return false
      }
      if (error.code === 'code_throttled') {
        // A code was sent a moment ago; the latest one is still valid.
        setVerification((v) => ({ ...v, codeEmail: normalizeEmail(email), resendAt: Date.now() + 60_000 }))
        setCodeNotice(error.message)
        return true
      }
      if (error.code === 'email_taken') {
        form.setError('email', { type: 'server', message: error.fieldErrors.email ?? error.message })
        setRegisteredEmail(email.trim())
        setStepIndex(0)
      }
      setStepError(error.message)
      return false
    } finally {
      setSendingCode(false)
    }
  }

  const next = async () => {
    setStepError(null)
    setRegisteredEmail(null)
    const valid = await form.trigger(step.fields, { shouldFocus: true })

    if (step.key === 'clinic') {
      if (!valid) return
      // A code is sent when the address is new; going back and forth does not send another one.
      const alreadyHandled = verified || (verification.codeEmail === currentEmail && Date.now() < verification.resendAt)
      if (!alreadyHandled && !(await sendCode())) return
    }
    if (step.key === 'verify' && !verified) {
      setCodeError('Nejprve ověřte e-mail kódem, který jsme vám poslali.')
      document.querySelector<HTMLInputElement>('[data-registration-code]')?.focus()
      return
    }
    if (valid) setStepIndex((i) => Math.min(i + 1, steps.length - 1))
  }

  const back = () => {
    setStepError(null)
    setStepIndex((i) => Math.max(i - 1, 0))
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setStepError(null)
    setRegisteredEmail(null)
    if (!verified) {
      goTo('verify')
      setCodeError('E-mail je potřeba ověřit kódem.')
      return
    }
    try {
      const profile = await publicApi.register(toRegistrationPayload(values, plan, verification.token))
      session.setData(profile)
      setRegistered(profile)
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setStepError('Registraci se nepodařilo odeslat.')
        return
      }
      setStepError(error.message)
      if (error.code === 'email_not_verified') {
        // The verification expired (or was used): verify the address again.
        setVerification({ codeEmail: null, resendAt: 0, token: null, tokenEmail: null })
        setStepIndex(0)
        return
      }
      if (error.code === 'email_taken') setRegisteredEmail(values.email.trim())
      const paths = Object.keys(error.fieldErrors).map((field) => {
        const path = formFieldPath(field)
        form.setError(path as Path<RegistrationFormValues>, { type: 'server', message: error.fieldErrors[field] })
        return path
      })
      // Jump back to the first step that contains a field rejected by the server.
      const target = steps.findIndex((s) => s.fields.some((f) => paths.some((p) => p === f || p.startsWith(`${f}.`))))
      if (target >= 0) setStepIndex(target)
    }
  })

  if (registered) return <RegistrationSuccess profile={registered} headingRef={headingRef} />

  return (
    <>
      <Link to="/#plany" className="back-link">
        <ArrowLeft size={16} aria-hidden="true" /> Zpět na úrovně registrace
      </Link>
      <div className="page-header">
        <div>
          <h1>Registrace oční kliniky</h1>
          <p>
            Krok {stepIndex + 1} z {steps.length}: {step.title}
          </p>
        </div>
        {plan && <span className="badge badge-info">Vybraná: {plan.name}</span>}
      </div>

      <ol className="wizard-steps" aria-label="Kroky registrace">
        {steps.map((s, i) => (
          <li key={s.key} data-state={i < stepIndex ? 'done' : i === stepIndex ? 'current' : 'todo'} aria-current={i === stepIndex ? 'step' : undefined}>
            <span>{s.title}</span>
          </li>
        ))}
      </ol>

      <FormProvider {...form}>
        <form
          className="card"
          noValidate
          onSubmit={(event) => {
            if (step.key === 'summary') {
              void onSubmit(event)
            } else {
              event.preventDefault()
              void next()
            }
          }}
        >
          <div className="card-body stack">
            <h2 ref={headingRef} tabIndex={-1} style={{ fontSize: '1.35rem', outline: 'none' }}>
              {step.title}
            </h2>
            {stepError && (
              <Alert kind="error" title={step.key === 'summary' ? 'Registraci se nepodařilo odeslat' : 'Nelze pokračovat'}>
                {stepError}
                {registeredEmail && (
                  <>
                    {' '}
                    <Link to="/prihlaseni" state={{ email: registeredEmail }}>
                      Přihlásit se kódem
                    </Link>
                  </>
                )}
              </Alert>
            )}

            {step.key === 'clinic' && <ClinicStep />}
            {step.key === 'verify' && (
              <>
                <EmailVerification
                  email={form.getValues('email').trim()}
                  verified={verified}
                  resendAt={verification.resendAt}
                  sending={sendingCode}
                  notice={codeNotice}
                  error={codeError}
                  onError={setCodeError}
                  onResend={async () => {
                    if (await sendCode()) setCodeNotice('Poslali jsme nový kód. Platí jen ten nejnovější.')
                  }}
                  onVerified={(token) => {
                    setCodeError(null)
                    setCodeNotice(null)
                    setVerification((v) => ({ ...v, token, tokenEmail: currentEmail }))
                  }}
                  onChangeEmail={() => goTo('clinic')}
                />
                <hr className="divider" />
                <PlanStep plans={plans} />
              </>
            )}
            {step.key === 'contacts' && (
              <div className="stack">
                <p className="muted" style={{ margin: 0 }}>
                  Telefony, e-maily, web a další informace, které budou sloužit pacientům k objednání.
                </p>
                <PublicContactFields />
                {plan && plan.fieldGroups.length > 0 && (
                  <>
                    <hr className="divider" />
                    <h3 className="section-title" style={{ marginBottom: 0 }}>
                      Údaje podle úrovně ({plan.name})
                    </h3>
                    {plan.fieldGroups.includes('operationalContact') && <OperationalContactFields />}
                    {plan.fieldGroups.includes('serviceConfiguration') && <ServiceConfigurationFields />}
                  </>
                )}
              </div>
            )}
            {step.key === 'summary' && <SummaryStep plan={plan} goTo={goTo} />}
          </div>
          <div className="dialog-footer" style={{ justifyContent: 'space-between' }}>
            <button type="button" className="btn" onClick={back} disabled={stepIndex === 0 || form.formState.isSubmitting}>
              <ArrowLeft size={16} aria-hidden="true" /> Zpět
            </button>
            {step.key === 'summary' ? (
              <button type="submit" className="btn btn-primary" disabled={form.formState.isSubmitting}>
                {form.formState.isSubmitting ? <Spinner /> : <Send size={16} aria-hidden="true" />}
                Odeslat registraci
              </button>
            ) : (
              <button type="submit" className="btn btn-dark" disabled={sendingCode}>
                {sendingCode && step.key === 'clinic' ? <Spinner /> : null}
                {step.key === 'clinic' && !verified ? 'Poslat ověřovací kód' : 'Pokračovat'} <ArrowRight size={16} aria-hidden="true" />
              </button>
            )}
          </div>
        </form>
      </FormProvider>
    </>
  )
}

function ClinicStep() {
  const {
    register,
    formState: { errors },
  } = useFormContext<RegistrationFormValues>()
  return (
    <div className="stack">
      <h3 className="section-title">Organizace</h3>
      <div className="form-grid">
        <Field label="Název kliniky" error={errors.name?.message} className="span-2" hint="Např. Oční klinika Plzeň">
          {(a) => <input {...a} {...register('name')} className="input" autoComplete="organization" />}
        </Field>
        <Field label="IČO" error={errors.organizationId?.message} optional hint="8 číslic">
          {(a) => <input {...a} {...register('organizationId')} className="input" inputMode="numeric" maxLength={10} />}
        </Field>
        <Field label="E-mail kontaktní osoby" error={errors.email?.message} hint="Slouží k přihlášení. Pošleme na něj ověřovací kód.">
          {(a) => <input {...a} {...register('email')} className="input" type="email" autoComplete="email" />}
        </Field>
      </div>
      <h3 className="section-title" style={{ marginTop: 32 }}>
        Kontaktní osoba
      </h3>
      <ContactPersonFields />
      <h3 className="section-title" style={{ marginTop: 32 }}>
        Adresa kliniky
      </h3>
      <AddressFields />
      <div className="alert alert-info" style={{ display: 'block', marginTop: 16 }}>
        <label className="checkbox">
          <input
            type="checkbox"
            {...register('authorityConfirmed')}
            aria-invalid={Boolean(errors.authorityConfirmed)}
            aria-describedby={errors.authorityConfirmed ? 'authority-error' : undefined}
          />
          <span>Potvrzuji, že jsem oprávněn(a) jednat jménem této organizace a registraci podávám za ni.</span>
        </label>
        {errors.authorityConfirmed && (
          <p id="authority-error" className="field-error" style={{ marginTop: 8, marginBottom: 0 }}>
            {errors.authorityConfirmed.message}
          </p>
        )}
      </div>
    </div>
  )
}

interface EmailVerificationProps {
  email: string
  verified: boolean
  resendAt: number
  sending: boolean
  notice: string | null
  error: string | null
  onError: (message: string | null) => void
  onResend: () => Promise<void>
  onVerified: (token: string) => void
  onChangeEmail: () => void
}

/** Code (PIN) entry inside the wizard form: it has its own button, so Enter verifies instead of submitting the step. */
function EmailVerification({ email, verified, resendAt, sending, notice, error, onError, onResend, onVerified, onChangeEmail }: EmailVerificationProps) {
  const [code, setCode] = useState('')
  const [checking, setChecking] = useState(false)
  const [now, setNow] = useState(() => Date.now())
  const cooldown = Math.max(0, Math.ceil((resendAt - now) / 1000))

  useEffect(() => {
    if (verified || resendAt <= Date.now()) return
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [resendAt, verified])

  const verify = async () => {
    const value = code.trim()
    if (!/^\d{6}$/.test(value)) {
      onError('Kód má 6 číslic.')
      return
    }
    setChecking(true)
    onError(null)
    try {
      const response = await publicApi.verifyRegistrationCode(email, value)
      onVerified(response.verificationToken)
    } catch (e) {
      onError(e instanceof ApiError ? e.message : 'Ověření se nezdařilo.')
    } finally {
      setChecking(false)
    }
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      void verify()
    }
  }

  if (verified) {
    return (
      <section aria-labelledby="verify-title" className="verify-done">
        <CircleCheck size={22} aria-hidden="true" />
        <div>
          <h3 id="verify-title" className="section-title" style={{ margin: 0 }}>
            E-mail ověřen
          </h3>
          <p className="muted small" style={{ margin: 0 }}>
            Adresa {email} patří vám. Zbývá zvolit úroveň registrace.
          </p>
        </div>
      </section>
    )
  }

  return (
    <section aria-labelledby="verify-title" className="stack">
      <div style={{ display: 'flex', gap: 16, alignItems: 'flex-start' }}>
        <span className="prereq-icon" aria-hidden="true">
          <MailCheck size={22} />
        </span>
        <div>
          <h3 id="verify-title" className="section-title" style={{ margin: 0 }}>
            Ověření e-mailu
          </h3>
          <p className="muted" style={{ margin: 0 }}>
            Na adresu <strong>{email}</strong> jsme poslali ověřovací kód (PIN). Zadejte jej níže.
          </p>
        </div>
      </div>
      {notice && <Alert kind="info">{notice}</Alert>}
      <Field label="Ověřovací kód (PIN)" error={error ?? undefined} hint="6 číslic, platí 10 minut.">
        {(a) => (
          <input
            {...a}
            data-registration-code=""
            value={code}
            onChange={(event) => setCode(event.target.value)}
            onKeyDown={onKeyDown}
            className="input input-code"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            placeholder="••••••"
          />
        )}
      </Field>
      <div className="inline-actions">
        <button type="button" className="btn btn-primary" onClick={() => void verify()} disabled={checking}>
          {checking ? <Spinner /> : <ShieldCheck size={18} aria-hidden="true" />}
          Ověřit kód
        </button>
        <button type="button" className="btn-link" onClick={() => void onResend()} disabled={cooldown > 0 || sending}>
          <RotateCw size={14} aria-hidden="true" style={{ verticalAlign: '-2px', marginRight: 4 }} />
          {sending ? 'Odesílám…' : cooldown > 0 ? `Poslat nový kód (${cooldown} s)` : 'Poslat nový kód'}
        </button>
        <button type="button" className="btn-link" onClick={onChangeEmail}>
          Změnit e-mail
        </button>
      </div>
    </section>
  )
}

function PlanStep({ plans }: { plans: Plan[] }) {
  const {
    register,
    formState: { errors },
  } = useFormContext<RegistrationFormValues>()
  return (
    <fieldset aria-describedby={errors.planCode ? 'plan-error' : undefined}>
      <legend style={{ marginBottom: 12 }}>
        <h3 className="section-title" style={{ marginBottom: 4 }}>
          Úroveň registrace
        </h3>
        <span className="muted small">
          Úroveň určuje, jaké další údaje budeme potřebovat. <Link to="/#plany">Porovnat úrovně</Link>
        </span>
      </legend>
      <div className="radio-cards">
        {plans.map((p) => (
          <label key={p.code} className="radio-card">
            <input type="radio" value={p.code} {...register('planCode')} />
            <span>
              <strong style={{ fontSize: '1.05rem' }}>{p.name}</strong>
              {p.badge && (
                <span className="badge badge-accent" style={{ marginLeft: 8 }}>
                  {p.badge}
                </span>
              )}
              <span className="radio-card-tagline">{p.tagline}</span>
              <span className="radio-card-price">{p.description}</span>
              <span className="small muted">{planRequirementText(p)}</span>
            </span>
          </label>
        ))}
      </div>
      {errors.planCode && (
        <p id="plan-error" className="field-error" style={{ marginTop: 8 }}>
          {errors.planCode.message}
        </p>
      )}
    </fieldset>
  )
}

function SummaryStep({ plan, goTo }: { plan: Plan | undefined; goTo: (key: StepKey) => void }) {
  const {
    register,
    getValues,
    formState: { errors },
  } = useFormContext<RegistrationFormValues>()
  const v = getValues()
  const editButton = (key: StepKey, label: string) => (
    <button type="button" className="btn-link small" onClick={() => goTo(key)} aria-label={`Upravit: ${label}`}>
      Upravit
    </button>
  )
  const filled = (rows: { value: string }[]) => rows.map((row) => row.value.trim()).filter(Boolean)
  const phones = filled(v.publicContacts.phones).map((phone) => formatPhone(phone.replace(/[\s-]/g, '')))
  const emails = filled(v.publicContacts.emails)

  return (
    <div className="stack">
      <section className="card card-body" style={{ boxShadow: 'none' }} aria-label="Kontakt a organizace">
        <div className="inline-actions" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
          <h3 className="section-title" style={{ margin: 0 }}>Kontakt a organizace</h3>
          {editButton('clinic', 'kontakt a organizace')}
        </div>
        <DefinitionList
          items={[
            ['Název', v.name],
            ['IČO', v.organizationId || '—'],
            ['Kontaktní osoba', `${v.contactFirstName} ${v.contactLastName}`],
            ['Telefon', formatPhone(v.phone.replace(/[\s-]/g, ''))],
            ['Adresa', `${v.street}, ${formatPostalCode(v.postalCode)} ${v.city}`],
            ['Oprávnění jednat', 'Potvrzeno'],
          ]}
        />
      </section>

      <section className="card card-body" style={{ boxShadow: 'none' }} aria-label="Ověření e-mailu a úroveň">
        <div className="inline-actions" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
          <h3 className="section-title" style={{ margin: 0 }}>Ověření e-mailu a úroveň</h3>
          {editButton('verify', 'úroveň')}
        </div>
        <DefinitionList
          items={[
            ['E-mail', `${v.email.trim()} (ověřen)`],
            ['Úroveň registrace', plan ? `${plan.name} – ${plan.tagline}` : '—'],
          ]}
        />
      </section>

      <section className="card card-body" style={{ boxShadow: 'none' }} aria-label="Veřejné kontakty">
        <div className="inline-actions" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
          <h3 className="section-title" style={{ margin: 0 }}>Veřejné kontakty</h3>
          {editButton('contacts', 'veřejné kontakty')}
        </div>
        <DefinitionList
          items={[
            ['Telefon', phones.length > 0 ? phones.join(', ') : '—'],
            ['E-mail', emails.length > 0 ? emails.join(', ') : '—'],
            ['Web', v.website.trim() || '—'],
            ['Další informace', v.patientInfo.trim() || '—'],
          ]}
        />
      </section>

      {plan && plan.fieldGroups.length > 0 && (
        <section className="card card-body" style={{ boxShadow: 'none' }} aria-label="Údaje podle úrovně">
          <div className="inline-actions" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
            <h3 className="section-title" style={{ margin: 0 }}>Údaje podle úrovně</h3>
            {editButton('contacts', 'údaje podle úrovně')}
          </div>
          <DefinitionList
            items={[
              ...(plan.fieldGroups.includes('operationalContact')
                ? ([
                    ['Provozní kontakt', v.operationalContact.name],
                    ['E-mail provozního kontaktu', v.operationalContact.email],
                    ['Telefon provozního kontaktu', v.operationalContact.phone || '—'],
                  ] as [string, string][])
                : []),
              ...(plan.fieldGroups.includes('serviceConfiguration')
                ? ([
                    ['Počet pracovišť', v.serviceConfiguration.workplaceCount],
                    [
                      'Úvodní nastavení',
                      v.serviceConfiguration.onboardingPreference ? onboardingLabels[v.serviceConfiguration.onboardingPreference] : '—',
                    ],
                    ['Technický kontakt', v.serviceConfiguration.technicalContactEmail || '—'],
                    ['Poznámka', v.serviceConfiguration.notes || '—'],
                  ] as [string, string][])
                : []),
            ]}
          />
        </section>
      )}

      <div className="alert alert-info" style={{ display: 'block' }}>
        <p style={{ marginBottom: 8 }}>
          Údaje zpracováváme pouze za účelem posouzení registrace a správy účtu kliniky. Zpracování je nezbytné pro vyřízení
          registrace, nejde o souhlas se zpracováním. Podrobnosti najdete v{' '}
          <Link to="/ochrana-osobnich-udaju" target="_blank" rel="noopener">
            zásadách zpracování osobních údajů
          </Link>
          .
        </p>
        <label className="checkbox">
          <input
            type="checkbox"
            {...register('privacyNoticeAccepted')}
            aria-invalid={Boolean(errors.privacyNoticeAccepted)}
            aria-describedby={errors.privacyNoticeAccepted ? 'privacy-error' : undefined}
          />
          <span>Potvrzuji, že jsem se seznámil(a) se zásadami zpracování osobních údajů.</span>
        </label>
        {errors.privacyNoticeAccepted && (
          <p id="privacy-error" className="field-error" style={{ marginTop: 8, marginBottom: 0 }}>
            {errors.privacyNoticeAccepted.message}
          </p>
        )}
      </div>
    </div>
  )
}

function RegistrationSuccess({ profile, headingRef }: { profile: ClinicProfile; headingRef: RefObject<HTMLHeadingElement | null> }) {
  const navigate = useNavigate()
  return (
    <div className="card">
      <div className="card-body stack">
        <div style={{ display: 'flex', gap: 16, alignItems: 'flex-start' }}>
          <span className="prereq-icon prereq-icon-success" aria-hidden="true">
            <CircleCheck size={22} />
          </span>
          <div>
            <h1 ref={headingRef} tabIndex={-1} style={{ fontSize: '1.6rem', outline: 'none' }}>
              Registrace byla odeslána
            </h1>
            <p className="muted" style={{ margin: 0 }}>
              Registrace kliniky {profile.name} ({profile.plan.name}) nyní čeká na posouzení administrátorem.
            </p>
          </div>
        </div>
        <Alert kind="success" title="Potvrzení je na cestě">
          Na adresu {profile.email} jsme poslali potvrzení s PDF přehledem vyplněných údajů. O výsledku posouzení vás budeme
          informovat e-mailem.
        </Alert>
        <div className="inline-actions">
          <button type="button" className="btn btn-primary" onClick={() => navigate('/portal', { replace: true })}>
            Přejít do portálu kliniky <ArrowRight size={16} aria-hidden="true" />
          </button>
          <a className="btn" href={clinicApi.confirmationUrl} download>
            <Download size={16} aria-hidden="true" /> Stáhnout PDF přehled
          </a>
        </div>
      </div>
    </div>
  )
}
