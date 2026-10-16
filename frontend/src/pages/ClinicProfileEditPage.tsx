import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Save } from 'lucide-react'
import { useState } from 'react'
import { FormProvider, useForm, type Path, type Resolver } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { ApiError } from '../api/client'
import { clinicApi } from '../api/endpoints'
import type { ClinicProfile, Plan } from '../api/types'
import { useClinicSession } from '../auth/sessions'
import {
  AddressFields,
  ContactPersonFields,
  OperationalContactFields,
  PublicContactFields,
  ServiceConfigurationFields,
} from '../components/ClinicFormFields'
import { useToast } from '../components/Toast'
import { Alert, DefinitionList, LoadingBlock, Spinner } from '../components/ui'
import { formatPhone } from '../lib/format'
import { usePlans } from '../lib/usePlans'
import { formFieldPath, profileSchema, toProfilePayload, type ContactFormValues } from '../lib/validation'

const rows = (values: string[], format: (value: string) => string = (v) => v) =>
  values.length > 0 ? values.map((value) => ({ value: format(value) })) : [{ value: '' }]

function toFormValues(clinic: ClinicProfile): ContactFormValues {
  return {
    contactFirstName: clinic.contactFirstName,
    contactLastName: clinic.contactLastName,
    phone: formatPhone(clinic.phone),
    street: clinic.street,
    city: clinic.city,
    postalCode: clinic.postalCode,
    publicContacts: { emails: rows(clinic.publicEmails), phones: rows(clinic.publicPhones, formatPhone) },
    website: clinic.website ?? '',
    patientInfo: clinic.patientInfo ?? '',
    operationalContact: {
      name: clinic.operationalContact?.name ?? '',
      email: clinic.operationalContact?.email ?? '',
      phone: clinic.operationalContact?.phone ? formatPhone(clinic.operationalContact.phone) : '',
    },
    serviceConfiguration: {
      workplaceCount: clinic.serviceConfiguration ? String(clinic.serviceConfiguration.workplaceCount) : '',
      onboardingPreference: clinic.serviceConfiguration?.onboardingPreference ?? '',
      technicalContactEmail: clinic.serviceConfiguration?.technicalContactEmail ?? '',
      notes: clinic.serviceConfiguration?.notes ?? '',
    },
  }
}

export function ClinicProfileEditPage() {
  const session = useClinicSession()
  const { plans, error } = usePlans()
  const clinic = session.data
  if (!clinic) return null

  return (
    <div className="container page page-narrow">
      <Link to="/portal" className="back-link">
        <ArrowLeft size={16} aria-hidden="true" /> Zpět na přehled
      </Link>
      <div className="page-header">
        <div>
          <h1>Úprava údajů</h1>
          <p>Změny se uloží okamžitě a uvidí je i administrátor.</p>
        </div>
      </div>
      {error && <Alert kind="error">{error}</Alert>}
      {!plans && !error && <LoadingBlock />}
      {plans && <ProfileForm clinic={clinic} plan={plans.find((p) => p.code === clinic.plan.code)} />}
    </div>
  )
}

function ProfileForm({ clinic, plan }: { clinic: ClinicProfile; plan: Plan | undefined }) {
  const session = useClinicSession()
  const navigate = useNavigate()
  const toast = useToast()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const form = useForm<ContactFormValues>({
    resolver: zodResolver(profileSchema(plan)) as Resolver<ContactFormValues>,
    mode: 'onTouched',
    defaultValues: toFormValues(clinic),
  })

  const onSubmit = form.handleSubmit(async (values) => {
    setSubmitError(null)
    try {
      const updated = await clinicApi.update(toProfilePayload(values, plan))
      session.setData(updated)
      toast.show('Údaje byly uloženy.')
      navigate('/portal')
    } catch (e) {
      if (e instanceof ApiError) {
        if (e.status === 401) {
          session.markSignedOut()
          return
        }
        setSubmitError(e.message)
        for (const [field, message] of Object.entries(e.fieldErrors)) {
          form.setError(formFieldPath(field) as Path<ContactFormValues>, { type: 'server', message })
        }
      } else {
        setSubmitError('Údaje se nepodařilo uložit.')
      }
    }
  })

  return (
    <FormProvider {...form}>
      <form onSubmit={onSubmit} noValidate className="card">
        <div className="card-body stack">
          {submitError && <Alert kind="error" title="Údaje se nepodařilo uložit">{submitError}</Alert>}

          <section aria-labelledby="readonly-title">
            <h2 id="readonly-title" className="section-title">
              Údaje, které mění pouze administrátor
            </h2>
            <DefinitionList
              items={[
                ['Název kliniky', clinic.name],
                ['IČO', clinic.organizationId ?? '—'],
                ['E-mail pro přihlášení', clinic.email],
                ['Úroveň registrace', clinic.plan.name],
              ]}
            />
          </section>

          <hr className="divider" />
          <h2 className="section-title">Kontaktní osoba</h2>
          <ContactPersonFields />
          <h2 className="section-title" style={{ marginTop: 32 }}>
            Adresa kliniky
          </h2>
          <AddressFields />

          <hr className="divider" />
          <h2 className="section-title">Kontakty pro pacienty</h2>
          <p className="muted small" style={{ marginTop: -8 }}>
            Telefony, e-maily a další informace, které budou sloužit pacientům k objednání.
          </p>
          <PublicContactFields />

          {plan?.fieldGroups.includes('operationalContact') && (
            <>
              <hr className="divider" />
              <OperationalContactFields />
            </>
          )}
          {plan?.fieldGroups.includes('serviceConfiguration') && (
            <>
              <hr className="divider" />
              <ServiceConfigurationFields />
            </>
          )}
        </div>
        <div className="dialog-footer">
          <Link to="/portal" className="btn">
            Zrušit
          </Link>
          <button type="submit" className="btn btn-primary" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? <Spinner /> : <Save size={16} aria-hidden="true" />}
            Uložit změny
          </button>
        </div>
      </form>
    </FormProvider>
  )
}
