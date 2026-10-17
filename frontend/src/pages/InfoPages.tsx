import { Compass, TriangleAlert } from 'lucide-react'
import { isRouteErrorResponse, Link, useRouteError } from 'react-router'
import { Alert } from '../components/ui'
import { branding } from '../config/branding'

export function PrivacyPage() {
  return (
    <div className="container page page-narrow">
      <span className="eyebrow">Informace</span>
      <h1>Zásady zpracování osobních údajů</h1>
      <Alert kind="warning" title="Vzorový text pro demonstrační aplikaci">
        Tento text popisuje, jak s údaji zachází tato ukázková aplikace. Nejde o právně posouzený dokument a před
        produkčním nasazením jej musí nahradit text schválený provozovatelem.
      </Alert>
      <div className="stack" style={{ marginTop: 32 }}>
        <section>
          <h2 style={{ fontSize: '1.3rem' }}>Jaké údaje zpracováváme</h2>
          <p>
            Identifikační a kontaktní údaje kliniky (název, IČO, adresa), jméno, e-mail a telefon kontaktní osoby,
            případně provozního a technického kontaktu, a údaje o zvolené úrovni registrace. Portál nezpracovává žádné údaje
            o pacientech ani zdravotní dokumentaci.
          </p>
        </section>
        <section>
          <h2 style={{ fontSize: '1.3rem' }}>Proč je zpracováváme</h2>
          <p>
            Údaje slouží k posouzení registrace administrátorem, ke komunikaci s klinikou a k přihlášení do portálu
            pomocí jednorázového kódu zaslaného e-mailem. Zpracování je nezbytné pro vyřízení registrace; potvrzení ve
            formuláři vyjadřuje, že jste se s těmito informacemi seznámili, nikoli udělení souhlasu.
          </p>
        </section>
        <section>
          <h2 style={{ fontSize: '1.3rem' }}>Zabezpečení</h2>
          <p>
            Přihlašovací kódy se ukládají pouze v podobě otisku (hash), platí 10 minut a lze je použít jen jednou.
            Přihlášení probíhá přes zabezpečenou relaci (HttpOnly cookie). Administrátorské poznámky nejsou klinikám
            zobrazovány.
          </p>
        </section>
        <section>
          <h2 style={{ fontSize: '1.3rem' }}>Vaše práva a kontakt</h2>
          <p>
            Kontaktní údaje provozovatele doplní provozovatel před nasazením. Změnu kontaktních údajů kliniky můžete
            provést sami v portálu kliniky.
          </p>
        </section>
        <p className="small muted">{branding.disclaimer}</p>
      </div>
    </div>
  )
}

/** Route error boundary: replaces React Router's developer error screen. */
export function RouteErrorPage() {
  const error = useRouteError()
  if (isRouteErrorResponse(error) && error.status === 404) return <NotFoundPage />
  return (
    <div className="container page page-narrow" style={{ textAlign: 'center' }}>
      <TriangleAlert size={48} aria-hidden="true" style={{ color: 'var(--color-subtle)' }} />
      <h1>Stránku se nepodařilo načíst</h1>
      <p className="lead" style={{ marginInline: 'auto' }}>
        Aplikace byla mezitím pravděpodobně aktualizována nebo došlo k výpadku připojení. Načtěte stránku znovu.
      </p>
      <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'center', flexWrap: 'wrap' }}>
        <button type="button" className="btn btn-primary" onClick={() => window.location.reload()}>
          Načíst znovu
        </button>
        <a href="/" className="btn">
          Zpět na úvodní stránku
        </a>
      </div>
    </div>
  )
}

export function NotFoundPage() {
  return (
    <div className="container page page-narrow" style={{ textAlign: 'center' }}>
      <Compass size={48} aria-hidden="true" style={{ color: 'var(--color-subtle)' }} />
      <h1>Stránka nenalezena</h1>
      <p className="lead" style={{ marginInline: 'auto' }}>
        Požadovaná stránka neexistuje nebo byla přesunuta.
      </p>
      <Link to="/" className="btn btn-primary">
        Zpět na úvodní stránku
      </Link>
    </div>
  )
}
