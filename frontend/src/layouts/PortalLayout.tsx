import { LogOut } from 'lucide-react'
import { useState } from 'react'
import { Outlet, useNavigate } from 'react-router'
import { useClinicSession } from '../auth/sessions'
import { Brand } from '../components/Brand'
import { useToast } from '../components/Toast'
import { Spinner } from '../components/ui'

export function PortalLayout() {
  const session = useClinicSession()
  const navigate = useNavigate()
  const toast = useToast()
  const [busy, setBusy] = useState(false)

  const logout = async () => {
    setBusy(true)
    try {
      await session.logout()
      toast.show('Byli jste odhlášeni.', 'info')
      navigate('/prihlaseni', { replace: true })
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <a href="#main" className="skip-link">
        Přeskočit na obsah
      </a>
      <header className="portal-header">
        <div className="container portal-header-inner">
          <Brand to="/portal" inverse subtitle="Portál kliniky" />
          <div className="portal-user">
            <span className="portal-user-name" title={session.data?.email}>
              {session.data?.name}
            </span>
            <button type="button" className="btn btn-sm" onClick={logout} disabled={busy}>
              {busy ? <Spinner /> : <LogOut size={16} aria-hidden="true" />}
              Odhlásit
            </button>
          </div>
        </div>
      </header>
      <main id="main" tabIndex={-1} className="surface-bg">
        <Outlet />
      </main>
    </>
  )
}
