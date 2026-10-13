import { Check, CircleDashed, Pencil, RefreshCw, X } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import type { ClinicProfile } from '../api/types'
import { useClinicSession } from '../auth/sessions'
import { ClinicDetails, PlanSummary } from '../components/ClinicDetails'
import { useToast } from '../components/Toast'
import { StatusBadge } from '../components/ui'
import { formatDateTime } from '../lib/format'

const statusCopy = {
  pending: {
    title: 'Registrace čeká na posouzení',
    text: 'Administrátor zkontroluje údaje vaší kliniky. O výsledku se dozvíte zde v portálu – po přihlášení uvidíte aktuální stav.',
  },
  approved: {
    title: 'Registrace je schválena',
    text: 'Vaše klinika byla schválena. Údaje o klinice a kontaktech můžete dál spravovat v tomto portálu.',
  },
  rejected: {
    title: 'Registrace byla zamítnuta',
    text: 'Administrátor registraci zamítl. Důvod najdete níže. Pokud údaje upravíte, administrátor může registraci znovu posoudit.',
  },
} as const

function StatusChecklist({ clinic }: { clinic: ClinicProfile }) {
  const reviewState = clinic.status === 'approved' ? 'done' : clinic.status === 'rejected' ? 'failed' : 'current'
  const items = [
    { state: 'done', title: 'Registrace odeslána', meta: formatDateTime(clinic.createdAt) },
    {
      state: clinic.emailVerifiedAt ? 'done' : 'current',
      title: 'E-mail ověřen',
      meta: clinic.emailVerifiedAt ? formatDateTime(clinic.emailVerifiedAt) : 'Čeká na ověření',
    },
    {
      state: reviewState,
      title: clinic.status === 'rejected' ? 'Posouzení: zamítnuto' : 'Posouzení administrátorem',
      meta: clinic.status === 'pending' ? 'Probíhá' : formatDateTime(clinic.statusChangedAt),
    },
    { state: clinic.status === 'approved' ? 'done' : 'todo', title: 'Klinika aktivní v portálu', meta: clinic.status === 'approved' ? 'Ano' : 'Po schválení' },
  ] as const

  return (
    <ol className="checklist" aria-label="Průběh registrace">
      {items.map((item) => (
        <li key={item.title} data-state={item.state}>
          <span className="checklist-icon" aria-hidden="true">
            {item.state === 'done' && <Check size={14} />}
            {item.state === 'current' && <CircleDashed size={14} />}
            {item.state === 'failed' && <X size={14} />}
          </span>
          <div>
            <div className="checklist-title">{item.title}</div>
            <div className="checklist-meta">{item.meta}</div>
          </div>
        </li>
      ))}
    </ol>
  )
}

export function ClinicPortalPage() {
  const session = useClinicSession()
  const toast = useToast()
  const [refreshing, setRefreshing] = useState(false)
  const clinic = session.data
  if (!clinic) return null

  const copy = statusCopy[clinic.status]

  const refresh = async () => {
    setRefreshing(true)
    const ok = await session.refresh()
    setRefreshing(false)
    toast.show(ok ? 'Stav registrace je aktuální.' : 'Stav se nepodařilo načíst. Zkuste to prosím znovu.', ok ? 'info' : 'error')
  }

  return (
    <div className="container page">
      <div className="page-header">
        <div>
          <span className="eyebrow">Portál kliniky</span>
          <h1>{clinic.name}</h1>
          <p>
            Plán {clinic.plan.name} · registrováno {formatDateTime(clinic.createdAt)}
          </p>
        </div>
        <div className="inline-actions">
          <button type="button" className="btn btn-sm" onClick={refresh} disabled={refreshing}>
            <RefreshCw size={16} aria-hidden="true" /> Obnovit stav
          </button>
          <Link to="/portal/udaje" className="btn btn-sm btn-dark">
            <Pencil size={16} aria-hidden="true" /> Upravit údaje
          </Link>
        </div>
      </div>

      <div className="portal-grid">
        <div className="stack">
          <section className="card status-panel" data-status={clinic.status} aria-labelledby="status-title">
            <div className="card-body">
              <div className="status-panel-head">
                <h2 id="status-title">{copy.title}</h2>
                <StatusBadge status={clinic.status} />
              </div>
              <p className="muted" style={{ marginBottom: 20 }}>
                {copy.text}
              </p>
              <StatusChecklist clinic={clinic} />
              {clinic.statusMessage && (
                <div className="status-message">
                  <div className="status-message-label">{clinic.status === 'rejected' ? 'Důvod zamítnutí' : 'Zpráva od administrátora'}</div>
                  <div style={{ whiteSpace: 'pre-wrap' }}>{clinic.statusMessage}</div>
                </div>
              )}
            </div>
          </section>
          <ClinicDetails clinic={clinic} />
        </div>

        <aside className="stack" aria-label="Doplňující informace">
          <PlanSummary clinic={clinic} />
          <section className="card" aria-labelledby="next-steps">
            <div className="card-header">
              <h2 id="next-steps">Co dál</h2>
            </div>
            <div className="card-body">
              {clinic.status === 'pending' && (
                <ul className="feature-list">
                  <li>
                    <Check size={16} aria-hidden="true" /> Zkontrolujte, že jsou vaše kontaktní údaje aktuální.
                  </li>
                  <li>
                    <Check size={16} aria-hidden="true" /> Výsledek posouzení se zobrazí na této stránce.
                  </li>
                  <li>
                    <Check size={16} aria-hidden="true" /> Při dalším přihlášení použijte opět kód z e-mailu.
                  </li>
                </ul>
              )}
              {clinic.status === 'approved' && <p style={{ margin: 0 }}>Registrace je dokončena. Nic dalšího není potřeba.</p>}
              {clinic.status === 'rejected' && (
                <p style={{ margin: 0 }}>
                  Upravte údaje podle důvodu zamítnutí. Administrátor může registraci vrátit k novému posouzení.
                </p>
              )}
            </div>
          </section>
        </aside>
      </div>
    </div>
  )
}
