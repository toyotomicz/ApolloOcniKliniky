import { Building2, LayoutDashboard, LogOut, Map as MapIcon, ScrollText, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { AdminRealtimeProvider, RealtimeIndicator } from '../admin/AdminRealtime'
import { useAdminSession } from '../auth/sessions'
import { Brand } from '../components/Brand'
import { useToast } from '../components/Toast'
import { Spinner } from '../components/ui'

const navItems = [
  { to: '/admin', label: 'Přehled', icon: LayoutDashboard, end: true },
  { to: '/admin/kliniky', label: 'Kliniky', icon: Building2 },
  { to: '/admin/mapa', label: 'Mapa', icon: MapIcon },
  { to: '/admin/audit', label: 'Audit log', icon: ScrollText },
  { to: '/admin/zabezpeceni', label: 'Zabezpečení', icon: ShieldCheck },
]

export function AdminLayout() {
  const session = useAdminSession()
  const navigate = useNavigate()
  const toast = useToast()
  const [busy, setBusy] = useState(false)

  // Prefetch the other admin sections right after sign-in, so switching in the top bar never waits for a download.
  useEffect(() => {
    if (import.meta.env.MODE === 'test') return
    const prefetch = () =>
      void Promise.all([
        import('../pages/admin/AdminDashboardPage'),
        import('../pages/admin/AdminClinicsPage'),
        import('../pages/admin/AdminClinicDetailPage'),
        import('../pages/admin/AdminMapPage'),
        import('../pages/admin/AdminAuditPage'),
        import('../pages/admin/AdminSecurityPage'),
      ]).catch(() => undefined)
    const timer = window.setTimeout(prefetch, 300)
    return () => window.clearTimeout(timer)
  }, [])

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
    <AdminRealtimeProvider>
      <div className="admin-shell">
        <a href="#main" className="skip-link">
          Přeskočit na obsah
        </a>
        <header className="admin-topbar">
          <div className="container admin-topbar-inner">
            <Brand to="/admin" inverse subtitle="Administrace" />
            <nav className="admin-nav" aria-label="Administrace">
              {navItems.map(({ to, label, icon: Icon, end }) => (
                <NavLink key={to} to={to} end={end}>
                  <Icon size={16} aria-hidden="true" />
                  {label}
                </NavLink>
              ))}
            </nav>
            <div className="admin-user">
              <RealtimeIndicator />
              <span className="admin-user-name">
                {session.data?.username}
                {session.data?.twoFactorEnabled && <ShieldCheck size={14} aria-label="Dvoufázové ověření zapnuto" style={{ marginLeft: 4, verticalAlign: '-2px' }} />}
              </span>
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
    </AdminRealtimeProvider>
  )
}
