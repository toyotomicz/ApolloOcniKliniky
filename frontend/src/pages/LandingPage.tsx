import { ArrowRight } from 'lucide-react'
import { useRef } from 'react'
import { Link } from 'react-router'
import { HeroIris } from '../components/HeroIris'
import { PlanCard } from '../components/PlanCard'
import { Alert, LoadingBlock } from '../components/ui'
import { usePlans } from '../lib/usePlans'
import { useReveal } from '../lib/useReveal'

const steps = [
  {
    title: 'Vyplňte kontaktní údaje a základní informace organizace',
    text: 'Zadáte kontaktní údaje, základní identifikační údaje organizace a potvrdíte oprávnění jednat jejím jménem.',
  },
  {
    title: 'Ověříte e-mail a zvolíte úroveň',
    text: 'Pomocí PINu ověříte e-mail kontaktní osoby a vyberete úroveň registrace.',
  },
  {
    title: 'Doplníte veřejné kontaktní údaje',
    text: 'Vyplníte e-maily, telefony, web a další informace, které budou sloužit pacientům k rezervaci.',
  },
  {
    title: 'Odešlete registraci a získáte potvrzení',
    text: 'Registraci odešlete ke zpracování a na zadaný e-mail obdržíte potvrzení s PDF přehledem vyplněných údajů.',
  },
]

export function LandingPage() {
  const { plans, error, retry } = usePlans()
  const plansRef = useRef<HTMLDivElement>(null)
  const stepsRef = useRef<HTMLOListElement>(null)
  useReveal(plansRef, '.plan-card', Boolean(plans))
  useReveal(stepsRef, '.step-card', true)

  return (
    <>
      <section className="hero" aria-labelledby="hero-title">
        <HeroIris />
        <div className="container hero-inner">
          <h1 id="hero-title">Registrace oční kliniky jednoduše</h1>
          <p className="hero-lead">
            Pro oční kliniky a ambulance. Vyberte plán, vyplňte údaje a přihlaste se kódem z <span className="nowrap">e-mailu</span>.
          </p>
          <div className="hero-actions">
            <a href="#plany" className="btn btn-primary btn-lg">
              Vybrat plán
            </a>
            <Link to="/prihlaseni" className="btn btn-lg">
              Přihlásit se
            </Link>
          </div>
        </div>
      </section>

      <section id="postup" className="section steps-section" aria-labelledby="steps-title">
        <div className="container">
          <div className="section-head section-head-center">
            <h2 id="steps-title">Jak to funguje</h2>
          </div>
          <ol className="steps" ref={stepsRef}>
            {steps.map((step, index) => (
              <li key={step.title} className="step-card">
                <span className="step-number" aria-hidden="true">
                  {index + 1}
                </span>
                <h3>
                  <span className="visually-hidden">Krok {index + 1}: </span>
                  {step.title}
                </h3>
                <p>{step.text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      <section id="plany" className="section section-alt" aria-labelledby="plans-title">
        <div className="container">
          <div className="section-head section-head-center">
            <h2 id="plans-title">Úrovně registrace</h2>
          </div>

          {error && (
            <Alert kind="error" title="Plány se nepodařilo načíst">
              <p>{error}</p>
              <button type="button" className="btn btn-sm" onClick={() => void retry()}>
                Zkusit znovu
              </button>
            </Alert>
          )}
          {!plans && !error && <LoadingBlock label="Načítám plány…" />}
          {plans && (
            <div className="plans" ref={plansRef}>
              {plans.map((plan) => (
                <PlanCard
                  key={plan.code}
                  plan={plan}
                  action={
                    <Link
                      to={`/registrace?plan=${plan.code}`}
                      className={`btn btn-block ${plan.highlighted ? 'btn-primary' : 'btn-dark'}`}
                      aria-label={`Zvolit ${plan.name.toLowerCase()} a pokračovat k registraci`}
                    >
                      Zvolit {plan.name.toLowerCase()}
                      <ArrowRight size={16} aria-hidden="true" />
                    </Link>
                  }
                />
              ))}
            </div>
          )}
        </div>
      </section>
    </>
  )
}
