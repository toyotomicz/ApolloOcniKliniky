import { Building2, LayoutDashboard, LogOut } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { useAdminSession } from '../auth/sessions'
import { Brand } from '../components/Brand'
import { useToast } from '../components/Toast'
import { Spinner } from '../components/ui'

export function AdminLayout() {
  const session = useAdminSession()
  const navigate = useNavigate()
  const toast = useToast()
  const [busy, setBusy] = useState(false)

  const logout = async () => {
    setBusy(true)
    try {
      await session.logout()
      toast.show('Byli jste odhlášeni z administrace.', 'info')
      navigate('/admin/prihlaseni', { replace: true })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="admin-shell">
      <a href="#main" className="skip-link">
        Přeskočit na obsah
      </a>
      <header className="admin-topbar">
        <div className="container admin-topbar-inner">
          <Brand to="/admin" inverse subtitle="Administrace" />
          <nav className="admin-nav" aria-label="Administrace">
            <NavLink to="/admin" end>
              <LayoutDashboard size={16} aria-hidden="true" />
              Přehled
            </NavLink>
            <NavLink to="/admin/kliniky">
              <Building2 size={16} aria-hidden="true" />
              Kliniky
            </NavLink>
          </nav>
          <div className="admin-user">
            <span className="admin-user-name">{session.data?.username}</span>
            <button type="button" className="btn btn-sm" onClick={logout} disabled={busy}>
              {busy ? <Spinner /> : <LogOut size={16} aria-hidden="true" />}
              Odhlásit
            </button>
          </div>
        </div>
      </header>
      <main id="main" tabIndex={-1}>
        <Outlet />
      </main>
    </div>
  )
}
