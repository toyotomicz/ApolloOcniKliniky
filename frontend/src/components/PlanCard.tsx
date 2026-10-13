import { Check } from 'lucide-react'
import type { ReactNode } from 'react'
import type { FieldGroup, Plan } from '../api/types'

export const fieldGroupLabels: Record<FieldGroup, string> = {
  operationalContact: 'provozní kontakt',
  serviceConfiguration: 'konfigurace služby (počet pracovišť, forma úvodního nastavení)',
}

export function planRequirementText(plan: Plan): string {
  if (plan.fieldGroups.length === 0) return 'K registraci stačí základní údaje o klinice a kontaktní osobě.'
  return `K registraci navíc: ${plan.fieldGroups.map((g) => fieldGroupLabels[g]).join(' a ')}.`
}

interface PlanCardProps {
  plan: Plan
  selected?: boolean
  action: ReactNode
  headingLevel?: 'h2' | 'h3'
}

export function PlanCard({ plan, selected = false, action, headingLevel: Heading = 'h3' }: PlanCardProps) {
  return (
    <article
      className={`plan-card ${plan.highlighted ? 'plan-card-highlighted' : ''}`}
      data-selected={selected}
      aria-labelledby={`plan-${plan.code}-name`}
    >
      {plan.badge && <span className="badge badge-accent plan-badge">{plan.badge}</span>}
      <Heading id={`plan-${plan.code}-name`} className="plan-name">
        {plan.name}
      </Heading>
      <p className="plan-tagline">{plan.tagline}</p>
      <p className="plan-description">{plan.description}</p>
      <ul className="feature-list">
        {plan.features.map((feature) => (
          <li key={feature}>
            <Check size={16} aria-hidden="true" />
            <span>{feature}</span>
          </li>
        ))}
      </ul>
      <p className="plan-requirements">{planRequirementText(plan)}</p>
      {action}
    </article>
  )
}
