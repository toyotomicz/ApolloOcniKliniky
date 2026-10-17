import { Menu, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router'
import { Brand } from '../components/Brand'
import { branding } from '../config/branding'

const currentYear = new Date().getFullYear()

export function SiteHeader() {
  const [open, setOpen] = useState(false)

  return (
    <header className="site-header">
      <div className="container site-header-inner">
        <Brand inverse />
        <button
          type="button"
          className="btn btn-ghost nav-toggle"
          aria-expanded={open}
          aria-controls="site-nav"
          onClick={() => setOpen((o) => !o)}
        >
          {open ? <X size={22} aria-hidden="true" /> : <Menu size={22} aria-hidden="true" />}
          <span className="visually-hidden">{open ? 'Zavřít menu' : 'Otevřít menu'}</span>
        </button>
        <nav
          id="site-nav"
          className="site-nav"
          data-open={open}
          aria-label="Hlavní navigace"
          onClick={(event) => {
            // Close the mobile menu after a link was chosen.
            if ((event.target as HTMLElement).closest('a')) setOpen(false)
          }}
        >
          <Link to="/#postup">Jak to funguje</Link>
          <Link to="/#plany">Úrovně</Link>
          <NavLink to="/prihlaseni">Přihlášení</NavLink>
          <Link to="/registrace" className="btn btn-primary">
            Registrovat kliniku
          </Link>
        </nav>
      </div>
    </header>
  )
}

export function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="container">
        <div className="footer-grid">
          <div>
            <Brand inverse />
            <p className="footer-copy">
              ©{currentYear} {branding.appName}
              <br />
              Všechna práva vyhrazena
            </p>
          </div>
          <nav aria-label="Pro kliniky">
            <h2>Pro kliniky</h2>
            <ul>
              <li>
                <Link to="/#postup">Jak to funguje</Link>
              </li>
              <li>
                <Link to="/#plany">Úrovně registrace</Link>
              </li>
              <li>
                <Link to="/registrace">Registrace</Link>
              </li>
              <li>
                <Link to="/prihlaseni">Přihlášení do portálu</Link>
              </li>
            </ul>
          </nav>
          <nav aria-label="Informace">
            <h2>Informace</h2>
            <ul>
              <li>
                <Link to="/ochrana-osobnich-udaju">Ochrana osobních údajů</Link>
              </li>
              <li>
                <Link to="/admin/prihlaseni">Administrace</Link>
              </li>
            </ul>
          </nav>
        </div>
      </div>
    </footer>
  )
}

/** Scrolls to "#section" anchors after client-side navigation, or to the top on page change. */
function useScrollHandling() {
  const { pathname, hash } = useLocation()
  useEffect(() => {
    if (hash) {
      const target = document.getElementById(hash.slice(1))
      if (target) {
        target.scrollIntoView()
        return
      }
    }
    window.scrollTo(0, 0)
  }, [pathname, hash])
}

export function PublicLayout() {
  useScrollHandling()
  return (
    <>
      <a href="#main" className="skip-link">
        Přeskočit na obsah
      </a>
      <SiteHeader />
      <main id="main" tabIndex={-1}>
        <Outlet />
      </main>
      <SiteFooter />
    </>
  )
}
