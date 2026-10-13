import { describe, expect, it } from 'vitest'
import { testPlans } from '../test/utils'
import { emptyContact, isValidIco, registrationSchema, toRegistrationPayload, type RegistrationFormValues } from './validation'

const [basic, standard, premium] = testPlans

const valid: RegistrationFormValues = {
  ...emptyContact,
  planCode: 'basic',
  name: 'Poliklinika Žluťoučký kůň',
  organizationId: '255 966 41',
  email: 'rehor@example.cz',
  contactFirstName: 'Řehoř',
  contactLastName: 'Šťastný',
  phone: '+420 777 123 456',
  street: 'Čáslavská 12',
  city: 'Ústí nad Labem',
  postalCode: '400 01',
  privacyNoticeAccepted: true,
}

const errorPaths = (result: { success: boolean; error?: { issues: { path: PropertyKey[] }[] } }) =>
  result.success ? [] : result.error!.issues.map((i) => i.path.join('.'))

describe('IČO checksum', () => {
  it.each([
    ['25596641', true],
    ['00000019', true],
    ['25596642', false],
    ['1234567', false],
  ])('%s → %s', (value, expected) => expect(isValidIco(value)).toBe(expected))
})

describe('registration schema', () => {
  it('accepts a complete Basic registration with Czech diacritics', () => {
    expect(registrationSchema(basic).safeParse(valid).success).toBe(true)
  })

  it('reports Czech messages for missing required fields', () => {
    const result = registrationSchema(basic).safeParse({ ...valid, name: '', email: 'x', postalCode: '12', privacyNoticeAccepted: false })
    expect(errorPaths(result)).toEqual(expect.arrayContaining(['name', 'email', 'postalCode', 'privacyNoticeAccepted']))
    expect(result.error?.issues.find((i) => i.path[0] === 'name')?.message).toBe('Toto pole je povinné.')
  })

  it('requires the operational contact only for Standard and Premium', () => {
    expect(registrationSchema(basic).safeParse(valid).success).toBe(true)
    const standardResult = registrationSchema(standard).safeParse({ ...valid, planCode: 'standard' })
    expect(errorPaths(standardResult)).toEqual(expect.arrayContaining(['operationalContact.name', 'operationalContact.email']))
    expect(errorPaths(standardResult)).not.toContain('serviceConfiguration.workplaceCount')
  })

  it('requires a service configuration for Premium', () => {
    const result = registrationSchema(premium).safeParse({
      ...valid,
      planCode: 'premium',
      operationalContact: { name: 'Petr', email: 'p@example.cz', phone: '' },
      serviceConfiguration: { workplaceCount: '0', onboardingPreference: '', technicalContactEmail: '', notes: '' },
    })
    expect(errorPaths(result)).toEqual(['serviceConfiguration.workplaceCount', 'serviceConfiguration.onboardingPreference'])
  })

  it('builds an API payload without fields of groups the plan does not include', () => {
    const payload = toRegistrationPayload(
      { ...valid, operationalContact: { name: 'Nemá se poslat', email: 'x@example.cz', phone: '' } },
      basic,
    )
    expect(payload.operationalContact).toBeNull()
    expect(payload.serviceConfiguration).toBeNull()
    expect(payload.organizationId).toBe('25596641')
    expect(payload.city).toBe('Ústí nad Labem')
  })
})
