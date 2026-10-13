import { z } from 'zod'
import type { FieldGroup, Plan, ProfileUpdatePayload, RegistrationPayload } from '../api/types'

// Client-side rules mirror the backend validator (ClinicValidator.cs). The server remains authoritative.

const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/
const PHONE = /^\+?[0-9]{9,15}$/
const POSTAL = /^[0-9]{5}$/

const compact = (value: string) => value.replace(/[\s-]/g, '')

export function isValidIco(value: string): boolean {
  if (!/^\d{8}$/.test(value)) return false
  const digits = [...value].map(Number)
  const sum = digits.slice(0, 7).reduce((acc, digit, i) => acc + digit * (8 - i), 0)
  const remainder = sum % 11
  const check = remainder === 0 ? 1 : remainder === 1 ? 0 : 11 - remainder
  return digits[7] === check
}

const required = (max: number, min = 1) =>
  z
    .string()
    .trim()
    .min(1, 'Toto pole je povinné.')
    .min(min, `Zadejte alespoň ${min} znaky.`)
    .max(max, `Maximální délka je ${max} znaků.`)

const email = (requiredMessage = 'Zadejte e-mailovou adresu.') =>
  z.string().trim().min(1, requiredMessage).max(254, 'E-mail je příliš dlouhý.').regex(EMAIL, 'Zadejte platnou e-mailovou adresu.')

const optionalEmail = z
  .string()
  .trim()
  .refine((v) => v === '' || (v.length <= 254 && EMAIL.test(v)), 'Zadejte platnou e-mailovou adresu.')

const phone = z
  .string()
  .min(1, 'Zadejte telefonní číslo.')
  .refine((v) => PHONE.test(compact(v)), 'Zadejte platné telefonní číslo, např. +420 777 123 456.')

const optionalPhone = z
  .string()
  .refine((v) => compact(v) === '' || PHONE.test(compact(v)), 'Zadejte platné telefonní číslo, např. +420 777 123 456.')

const postalCode = z
  .string()
  .min(1, 'Zadejte PSČ.')
  .refine((v) => POSTAL.test(compact(v)), 'PSČ musí mít 5 číslic, např. 301 00.')

const organizationId = z
  .string()
  .refine((v) => compact(v) === '' || isValidIco(compact(v)), 'IČO musí mít 8 číslic a platný kontrolní součet.')

const operationalContactSchema = z.object({
  name: required(200),
  email: email(),
  phone: optionalPhone,
})

const serviceConfigurationSchema = z.object({
  workplaceCount: z
    .string()
    .trim()
    .min(1, 'Zadejte počet pracovišť.')
    .refine((v) => /^\d+$/.test(v) && Number(v) >= 1 && Number(v) <= 500, 'Počet pracovišť musí být mezi 1 a 500.'),
  onboardingPreference: z.enum(['online', 'onSite'], 'Vyberte preferovanou formu úvodního nastavení.'),
  technicalContactEmail: optionalEmail,
  notes: z.string().max(1000, 'Maximální délka je 1000 znaků.'),
})

// Plan-specific groups are validated only when the selected plan requires them.
const groupSchema = <T extends z.ZodType>(plan: Plan | undefined, group: FieldGroup, schema: T) =>
  plan?.fieldGroups.includes(group) ? schema : z.any()

const contactShape = (plan: Plan | undefined) => ({
  contactFirstName: required(100),
  contactLastName: required(100),
  phone,
  street: required(200),
  city: required(100),
  postalCode,
  operationalContact: groupSchema(plan, 'operationalContact', operationalContactSchema),
  serviceConfiguration: groupSchema(plan, 'serviceConfiguration', serviceConfigurationSchema),
})

export function registrationSchema(plan: Plan | undefined) {
  return z.object({
    planCode: z.string().min(1, 'Vyberte jeden z nabízených plánů.'),
    name: required(200, 2),
    organizationId,
    email: email(),
    ...contactShape(plan),
    privacyNoticeAccepted: z.literal(true, 'Pro odeslání registrace je nutné potvrdit seznámení se zásadami zpracování údajů.'),
  })
}

export function profileSchema(plan: Plan | undefined) {
  return z.object(contactShape(plan))
}

/** Form state: everything is a string so inputs stay controlled; converted to the API payload on submit. */
export interface ContactFormValues {
  contactFirstName: string
  contactLastName: string
  phone: string
  street: string
  city: string
  postalCode: string
  operationalContact: { name: string; email: string; phone: string }
  serviceConfiguration: {
    workplaceCount: string
    onboardingPreference: '' | 'online' | 'onSite'
    technicalContactEmail: string
    notes: string
  }
}

export interface RegistrationFormValues extends ContactFormValues {
  planCode: string
  name: string
  organizationId: string
  email: string
  privacyNoticeAccepted: boolean
}

export const emptyContact: ContactFormValues = {
  contactFirstName: '',
  contactLastName: '',
  phone: '',
  street: '',
  city: '',
  postalCode: '',
  operationalContact: { name: '', email: '', phone: '' },
  serviceConfiguration: { workplaceCount: '', onboardingPreference: '', technicalContactEmail: '', notes: '' },
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim())

function toContactPayload(values: ContactFormValues, plan: Plan | undefined): ProfileUpdatePayload {
  const oc = values.operationalContact
  const sc = values.serviceConfiguration
  return {
    contactFirstName: values.contactFirstName.trim(),
    contactLastName: values.contactLastName.trim(),
    phone: values.phone.trim(),
    street: values.street.trim(),
    city: values.city.trim(),
    postalCode: values.postalCode.trim(),
    operationalContact: plan?.fieldGroups.includes('operationalContact')
      ? { name: oc.name.trim(), email: oc.email.trim(), phone: orNull(oc.phone) }
      : null,
    serviceConfiguration:
      plan?.fieldGroups.includes('serviceConfiguration') && sc.onboardingPreference !== ''
        ? {
            workplaceCount: Number(sc.workplaceCount),
            onboardingPreference: sc.onboardingPreference,
            technicalContactEmail: orNull(sc.technicalContactEmail),
            notes: orNull(sc.notes),
          }
        : null,
  }
}

export function toRegistrationPayload(values: RegistrationFormValues, plan: Plan | undefined): RegistrationPayload {
  return {
    planCode: values.planCode,
    name: values.name.trim(),
    organizationId: orNull(compact(values.organizationId)),
    email: values.email.trim(),
    privacyNoticeAccepted: values.privacyNoticeAccepted,
    ...toContactPayload(values, plan),
  }
}

export const toProfilePayload = toContactPayload

export const codeSchema = z.object({
  code: z
    .string()
    .trim()
    .regex(/^\d{6}$/, 'Kód má 6 číslic.'),
})

export const emailOnlySchema = z.object({ email: email() })

export const adminLoginSchema = z.object({
  username: z.string().trim().min(1, 'Zadejte uživatelské jméno.'),
  password: z.string().min(1, 'Zadejte heslo.'),
})
