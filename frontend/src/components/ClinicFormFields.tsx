import { useFormContext } from 'react-hook-form'
import type { ContactFormValues } from '../lib/validation'
import { Field } from './ui'

// Field groups shared by the registration wizard and the clinic profile editor.
// They read the surrounding form via FormProvider; field paths match the API JSON.

export function ContactPersonFields() {
  const {
    register,
    formState: { errors },
  } = useFormContext<ContactFormValues>()
  return (
    <div className="form-grid">
      <Field label="Jméno kontaktní osoby" error={errors.contactFirstName?.message}>
        {(a) => <input {...a} {...register('contactFirstName')} className="input" autoComplete="given-name" />}
      </Field>
      <Field label="Příjmení kontaktní osoby" error={errors.contactLastName?.message}>
        {(a) => <input {...a} {...register('contactLastName')} className="input" autoComplete="family-name" />}
      </Field>
      <Field label="Telefon" error={errors.phone?.message} hint="Např. +420 777 123 456">
        {(a) => <input {...a} {...register('phone')} className="input" type="tel" autoComplete="tel" />}
      </Field>
    </div>
  )
}

export function AddressFields() {
  const {
    register,
    formState: { errors },
  } = useFormContext<ContactFormValues>()
  return (
    <div className="form-grid">
      <Field label="Ulice a číslo popisné" error={errors.street?.message} className="span-2">
        {(a) => <input {...a} {...register('street')} className="input" autoComplete="street-address" />}
      </Field>
      <Field label="Obec" error={errors.city?.message}>
        {(a) => <input {...a} {...register('city')} className="input" autoComplete="address-level2" />}
      </Field>
      <Field label="PSČ" error={errors.postalCode?.message} hint="5 číslic, např. 301 00">
        {(a) => <input {...a} {...register('postalCode')} className="input" inputMode="numeric" autoComplete="postal-code" />}
      </Field>
    </div>
  )
}

export function OperationalContactFields() {
  const {
    register,
    formState: { errors },
  } = useFormContext<ContactFormValues>()
  const e = errors.operationalContact
  return (
    <fieldset className="stack">
      <legend>
        <h3 style={{ marginBottom: 4 }}>Provozní kontakt</h3>
        <p className="muted small" style={{ margin: 0 }}>
          Osoba, která řeší každodenní provoz kliniky. Může být stejná jako kontaktní osoba.
        </p>
      </legend>
      <div className="form-grid">
        <Field label="Jméno a příjmení" error={e?.name?.message} className="span-2">
          {(a) => <input {...a} {...register('operationalContact.name')} className="input" autoComplete="off" />}
        </Field>
        <Field label="E-mail" error={e?.email?.message}>
          {(a) => <input {...a} {...register('operationalContact.email')} className="input" type="email" autoComplete="off" />}
        </Field>
        <Field label="Telefon" error={e?.phone?.message} optional>
          {(a) => <input {...a} {...register('operationalContact.phone')} className="input" type="tel" autoComplete="off" />}
        </Field>
      </div>
    </fieldset>
  )
}

export function ServiceConfigurationFields() {
  const {
    register,
    formState: { errors },
  } = useFormContext<ContactFormValues>()
  const e = errors.serviceConfiguration
  return (
    <fieldset className="stack">
      <legend>
        <h3 style={{ marginBottom: 4 }}>Konfigurace služby</h3>
        <p className="muted small" style={{ margin: 0 }}>
          Pomůže nám připravit úvodní nastavení pro více pracovišť.
        </p>
      </legend>
      <div className="form-grid">
        <Field label="Počet pracovišť" error={e?.workplaceCount?.message} hint="1 až 500">
          {(a) => (
            <input {...a} {...register('serviceConfiguration.workplaceCount')} className="input" type="number" min={1} max={500} inputMode="numeric" />
          )}
        </Field>
        <Field label="Technický kontakt (e-mail)" error={e?.technicalContactEmail?.message} optional>
          {(a) => <input {...a} {...register('serviceConfiguration.technicalContactEmail')} className="input" type="email" autoComplete="off" />}
        </Field>
      </div>
      <fieldset aria-describedby={e?.onboardingPreference ? 'onboarding-error' : undefined}>
        <legend className="field-label" style={{ marginBottom: 8 }}>
          Preferovaná forma úvodního nastavení
        </legend>
        <div className="radio-cards" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
          <label className="radio-card">
            <input type="radio" value="online" {...register('serviceConfiguration.onboardingPreference')} />
            <span>
              <strong>Online</strong>
              <br />
              <span className="small muted">Videohovor a sdílená obrazovka</span>
            </span>
          </label>
          <label className="radio-card">
            <input type="radio" value="onSite" {...register('serviceConfiguration.onboardingPreference')} />
            <span>
              <strong>Na místě</strong>
              <br />
              <span className="small muted">Návštěva na vybraném pracovišti</span>
            </span>
          </label>
        </div>
        {e?.onboardingPreference && (
          <span id="onboarding-error" className="field-error" style={{ marginTop: 6 }}>
            {e.onboardingPreference.message}
          </span>
        )}
      </fieldset>
      <Field label="Poznámka ke konfiguraci" error={e?.notes?.message} optional hint="Nevkládejte údaje o pacientech.">
        {(a) => <textarea {...a} {...register('serviceConfiguration.notes')} className="textarea" maxLength={1000} />}
      </Field>
    </fieldset>
  )
}
