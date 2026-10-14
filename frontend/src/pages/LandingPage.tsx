import { ArrowRight } from 'lucide-react'
import { useRef } from 'react'
import { Link } from 'react-router'
import { HeroIris } from '../components/HeroIris'
import { PlanCard } from '../components/PlanCard'
import { Alert, LoadingBlock } from '../components/ui'
import { usePlans } from '../lib/usePlans'
import { useReveal } from '../lib/useReveal'

export function LandingPage() {
  const { plans, error, retry } = usePlans()
  const plansRef = useRef<HTMLDivElement>(null)
  useReveal(plansRef, '.plan-card', Boolean(plans))

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

      <section id="plany" className="section section-alt" aria-labelledby="plans-title">
        <div className="container">
          <div className="section-head section-head-center">
            <h2 id="plans-title">Plány</h2>
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
                      aria-label={`Zvolit plán ${plan.name} a pokračovat k registraci`}
                    >
                      Zvolit {plan.name}
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
