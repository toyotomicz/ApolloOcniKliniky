import { Check } from 'lucide-react'
import type { ClinicProfile } from '../api/types'
import { formatDateTime, formatPhone, onboardingLabels } from '../lib/format'
import { DefinitionList } from './ui'

/** Read-only clinic data cards, shared by the clinic portal and the administrator detail page. */
export function ClinicDetails({ clinic }: { clinic: ClinicProfile }) {
  const website = clinic.website ? (
    <a href={clinic.website} target="_blank" rel="noopener noreferrer">
      {clinic.website.replace(/^https?:\/\//, '')}
    </a>
  ) : (
    '—'
  )
  const patientInfo = clinic.patientInfo ? <span style={{ whiteSpace: 'pre-wrap' }}>{clinic.patientInfo}</span> : '—'

  return (
    <div className="stack">
      <section className="card" aria-labelledby="clinic-section">
        <div className="card-header">
          <h2 id="clinic-section">Klinika</h2>
        </div>
        <div className="card-body">
          <DefinitionList
            items={[
              ['Název', clinic.name],
              ['IČO', clinic.organizationId ?? '—'],
              ['Adresa', `${clinic.street}, ${clinic.postalCode} ${clinic.city}`],
              ['Registrováno', formatDateTime(clinic.createdAt)],
            ]}
          />
        </div>
      </section>

      <section className="card" aria-labelledby="contact-section">
        <div className="card-header">
          <h2 id="contact-section">Kontaktní osoba</h2>
        </div>
        <div className="card-body">
          <DefinitionList
            items={[
              ['Jméno', `${clinic.contactFirstName} ${clinic.contactLastName}`],
              ['E-mail (přihlášení)', clinic.email],
              ['Telefon', formatPhone(clinic.phone)],
            ]}
          />
        </div>
      </section>

      <section className="card" aria-labelledby="public-section">
        <div className="card-header">
          <h2 id="public-section">Kontakty pro pacienty</h2>
        </div>
        <div className="card-body">
          <DefinitionList
            items={[
              ['Telefon', clinic.publicPhones.length > 0 ? clinic.publicPhones.map(formatPhone).join(', ') : '—'],
              ['E-mail', clinic.publicEmails.length > 0 ? clinic.publicEmails.join(', ') : '—'],
              ['Web', website],
              ['Další informace', patientInfo],
            ]}
          />
        </div>
      </section>

      {clinic.operationalContact && (
        <section className="card" aria-labelledby="operational-section">
          <div className="card-header">
            <h2 id="operational-section">Provozní kontakt</h2>
          </div>
          <div className="card-body">
            <DefinitionList
              items={[
                ['Jméno', clinic.operationalContact.name],
                ['E-mail', clinic.operationalContact.email],
                ['Telefon', formatPhone(clinic.operationalContact.phone)],
              ]}
            />
          </div>
        </section>
      )}

      {clinic.serviceConfiguration && (
        <section className="card" aria-labelledby="service-section">
          <div className="card-header">
            <h2 id="service-section">Konfigurace služby</h2>
          </div>
          <div className="card-body">
            <DefinitionList
              items={[
                ['Počet pracovišť', clinic.serviceConfiguration.workplaceCount],
                ['Úvodní nastavení', onboardingLabels[clinic.serviceConfiguration.onboardingPreference]],
                ['Technický kontakt', clinic.serviceConfiguration.technicalContactEmail ?? '—'],
                ['Poznámka', clinic.serviceConfiguration.notes ?? '—'],
              ]}
            />
          </div>
        </section>
      )}
    </div>
  )
}

export function PlanSummary({ clinic }: { clinic: ClinicProfile }) {
  return (
    <section className="card" aria-labelledby="plan-section">
      <div className="card-header">
        <h2 id="plan-section">{clinic.plan.name}</h2>
      </div>
      <div className="card-body">
        <p className="muted small">{clinic.plan.tagline}</p>
        <ul className="feature-list">
          {clinic.plan.features.map((feature) => (
            <li key={feature}>
              <Check size={16} aria-hidden="true" />
              <span>{feature}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  )
}
