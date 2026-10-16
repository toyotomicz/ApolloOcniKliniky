import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, ArrowRight, MailCheck, Send } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type RefObject } from 'react'
import { FormProvider, useForm, useFormContext, type Path, type Resolver } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { ApiError } from '../api/client'
import { publicApi } from '../api/endpoints'
import type { Plan, RegistrationResponse } from '../api/types'
import { useClinicSession } from '../auth/sessions'
import {
  AddressFields,
  ContactPersonFields,
  OperationalContactFields,
  ServiceConfigurationFields,
} from '../components/ClinicFormFields'
import { CodeVerification } from '../components/CodeVerification'
import { planRequirementText } from '../components/PlanCard'
import { useToast } from '../components/Toast'
import { Alert, DefinitionList, Field, LoadingBlock, Spinner } from '../components/ui'
import { formatPhone, formatPostalCode, onboardingLabels } from '../lib/format'
import { usePlans } from '../lib/usePlans'
import { emptyContact, registrationSchema, toRegistrationPayload, type RegistrationFormValues } from '../lib/validation'

type StepKey = 'plan' | 'clinic' | 'extra' | 'summary'
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

/** Steps depend on the selected plan: the "extra" step only exists for plans with additional field groups. */
export function buildSteps(plan: Plan | undefined): Step[] {
  const extraFields = [
    ...(plan?.fieldGroups.includes('operationalContact') ? operationalFields : []),
    ...(plan?.fieldGroups.includes('serviceConfiguration') ? serviceFields : []),
  ]
  return [
    { key: 'plan', title: 'Plán', fields: ['planCode'] },
    {
      key: 'clinic',
      title: 'Klinika a kontakt',
      fields: ['name', 'organizationId', 'email', 'contactFirstName', 'contactLastName', 'phone', 'street', 'city', 'postalCode'],
    },
    ...(extraFields.length > 0 ? [{ key: 'extra' as const, title: 'Údaje podle plánu', fields: extraFields }] : []),
    { key: 'summary', title: 'Souhrn a odeslání', fields: ['privacyNoticeAccepted'] },
  ]
}

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

function RegistrationWizard({ plans, initialPlanCode }: { plans: Plan[]; initialPlanCode: string | null }) {
  const initialPlan = plans.find((p) => p.code === initialPlanCode)
  const [stepIndex, setStepIndex] = useState(initialPlan ? 1 : 0)
  const [result, setResult] = useState<RegistrationResponse | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [registeredEmail, setRegisteredEmail] = useState<string | null>(null)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const firstRender = useRef(true)

  // The schema depends on the selected plan, so it is resolved per validation run.
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
      privacyNoticeAccepted: false,
    },
  })

  const planCode = form.watch('planCode')
  const plan = plans.find((p) => p.code === planCode)
  const steps = useMemo(() => buildSteps(plan), [plan])
  const step = steps[Math.min(stepIndex, steps.length - 1)]!

  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false
      return
    }
    headingRef.current?.focus()
  }, [stepIndex, result])

  const next = async () => {
    const valid = await form.trigger(step.fields, { shouldFocus: true })
    if (valid) setStepIndex((i) => Math.min(i + 1, steps.length - 1))
  }

  const back = () => setStepIndex((i) => Math.max(i - 1, 0))

  const onSubmit = form.handleSubmit(async (values) => {
    setSubmitError(null)
    setRegisteredEmail(null)
    try {
      setResult(await publicApi.register(toRegistrationPayload(values, plan)))
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setSubmitError('Registraci se nepodařilo odeslat.')
        return
      }
      setSubmitError(error.message)
      if (error.code === 'email_taken') setRegisteredEmail(values.email.trim())
      const fields = Object.keys(error.fieldErrors)
      for (const field of fields) {
        form.setError(field as Path<RegistrationFormValues>, { type: 'server', message: error.fieldErrors[field] })
      }
      // Jump back to the first step that contains a field rejected by the server.
      const target = steps.findIndex((s) => s.fields.some((f) => fields.includes(f)))
      if (target >= 0) setStepIndex(target)
    }
  })

  if (result) return <RegistrationSuccess result={result} headingRef={headingRef} />

  return (
    <>
      <Link to="/#plany" className="back-link">
        <ArrowLeft size={16} aria-hidden="true" /> Zpět na plány
      </Link>
      <div className="page-header">
        <div>
          <h1>Registrace oční kliniky</h1>
          <p>Krok {stepIndex + 1} z {steps.length}: {step.title}</p>
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
            {submitError && (
              <Alert kind="error" title="Registraci se nepodařilo odeslat">
                {submitError}
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

            {step.key === 'plan' && <PlanStep plans={plans} />}
            {step.key === 'clinic' && <ClinicStep />}
            {step.key === 'extra' && (
              <div className="stack">
                {plan?.fieldGroups.includes('operationalContact') && <OperationalContactFields />}
                {plan?.fieldGroups.includes('serviceConfiguration') && (
                  <>
                    <hr className="divider" />
                    <ServiceConfigurationFields />
                  </>
                )}
              </div>
            )}
            {step.key === 'summary' && <SummaryStep plan={plan} goTo={(key) => setStepIndex(steps.findIndex((s) => s.key === key))} />}
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
              <button type="submit" className="btn btn-dark">
                Pokračovat <ArrowRight size={16} aria-hidden="true" />
              </button>
            )}
          </div>
        </form>
      </FormProvider>
    </>
  )
}

function PlanStep({ plans }: { plans: Plan[] }) {
  const {
    register,
    formState: { errors },
  } = useFormContext<RegistrationFormValues>()
  return (
    <fieldset aria-describedby={errors.planCode ? 'plan-error' : undefined}>
      <legend className="muted" style={{ marginBottom: 12 }}>
        Plán určuje, jaké údaje budeme při registraci potřebovat. <Link to="/#plany">Porovnat plány</Link>
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
              <br />
              <span>{p.tagline}</span>
              <br />
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

function ClinicStep() {
  const {
    register,
    formState: { errors },
  } = useFormContext<RegistrationFormValues>()
  return (
    <div className="stack">
      <h3 className="section-title">Klinika</h3>
      <div className="form-grid">
        <Field label="Název kliniky" error={errors.name?.message} className="span-2" hint="Např. Oční klinika Plzeň">
          {(a) => <input {...a} {...register('name')} className="input" autoComplete="organization" />}
        </Field>
        <Field label="IČO" error={errors.organizationId?.message} optional hint="8 číslic">
          {(a) => <input {...a} {...register('organizationId')} className="input" inputMode="numeric" maxLength={10} />}
        </Field>
        <Field label="E-mail pro přihlášení" error={errors.email?.message} hint="Pošleme na něj ověřovací kód.">
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
    </div>
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

  return (
    <div className="stack">
      <section className="card card-body" style={{ boxShadow: 'none' }} aria-label="Plán">
        <div className="inline-actions" style={{ justifyContent: 'space-between' }}>
          <h3 className="section-title" style={{ margin: 0 }}>Plán</h3>
          {editButton('plan', 'plán')}
        </div>
        <p style={{ margin: '8px 0 0' }}>
          <strong>{plan?.name}</strong> – {plan?.tagline}
        </p>
      </section>

      <section className="card card-body" style={{ boxShadow: 'none' }} aria-label="Klinika a kontakt">
        <div className="inline-actions" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
          <h3 className="section-title" style={{ margin: 0 }}>Klinika a kontakt</h3>
          {editButton('clinic', 'klinika a kontakt')}
        </div>
        <DefinitionList
          items={[
            ['Název', v.name],
            ['IČO', v.organizationId || '—'],
            ['E-mail', v.email],
            ['Kontaktní osoba', `${v.contactFirstName} ${v.contactLastName}`],
            ['Telefon', formatPhone(v.phone.replace(/[\s-]/g, ''))],
            ['Adresa', `${v.street}, ${formatPostalCode(v.postalCode)} ${v.city}`],
          ]}
        />
      </section>

      {plan && plan.fieldGroups.length > 0 && (
        <section className="card card-body" style={{ boxShadow: 'none' }} aria-label="Údaje podle plánu">
          <div className="inline-actions" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
            <h3 className="section-title" style={{ margin: 0 }}>Údaje podle plánu</h3>
            {editButton('extra', 'údaje podle plánu')}
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

function RegistrationSuccess({ result, headingRef }: { result: RegistrationResponse; headingRef: RefObject<HTMLHeadingElement | null> }) {
  const session = useClinicSession()
  const navigate = useNavigate()
  const toast = useToast()

  return (
    <div className="card">
      <div className="card-body stack">
        <Alert kind="success" title="Registrace byla uložena">
          Vaše registrace nyní čeká na posouzení administrátorem. Nejprve prosím ověřte svůj e-mail.
        </Alert>
        <div style={{ display: 'flex', gap: 16, alignItems: 'flex-start' }}>
          <span className="prereq-icon" aria-hidden="true">
            <MailCheck size={22} />
          </span>
          <div>
            <h1 ref={headingRef} tabIndex={-1} style={{ fontSize: '1.6rem', outline: 'none' }}>
              Ověřte e-mail
            </h1>
            <p className="muted" style={{ margin: 0 }}>
              {result.verificationEmailSent
                ? `Na adresu ${result.email} jsme poslali jednorázový kód. Zadejte jej níže.`
                : `Registraci jsme uložili, ale e-mail s kódem se nyní nepodařilo odeslat na ${result.email}.`}
            </p>
          </div>
        </div>
        <CodeVerification
          email={result.email}
          initialDeliveryFailed={!result.verificationEmailSent}
          onVerified={(profile) => {
            session.setData(profile)
            toast.show('E-mail byl ověřen. Vítejte v portálu kliniky.')
            navigate('/portal', { replace: true })
          }}
        />
        <p className="small muted" style={{ marginBottom: 0 }}>
          Kód můžete zadat i později – stačí se přihlásit na stránce <Link to="/prihlaseni">Přihlášení kliniky</Link>.
        </p>
      </div>
    </div>
  )
}
