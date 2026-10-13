import { Activity, ArrowRight, CircleCheck, CircleX, Clock, FilePlus2, LogIn, MailCheck, NotebookPen, RefreshCw, UserPen } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { adminApi } from '../../api/endpoints'
import type { ActivityItem, ActivityType, DashboardMetrics } from '../../api/types'
import { BarChart } from '../../components/BarChart'
import { Alert, EmptyState, LoadingBlock } from '../../components/ui'
import { describeActivity, formatDateTime, formatRelative } from '../../lib/format'
import { useAdminLoader } from './useAdminLoader'

interface MetricDef {
  key: keyof DashboardMetrics
  label: string
  hint: string
  icon: ReactNode
  to?: string
  attention?: boolean
}

// The definitions mirror DashboardMetrics in DashboardService.cs.
const metricDefs: MetricDef[] = [
  { key: 'totalClinics', label: 'Registrované kliniky', hint: 'Všechny registrace bez ohledu na stav', icon: <Activity size={15} aria-hidden="true" />, to: '/admin/kliniky' },
  { key: 'newToday', label: 'Nové dnes', hint: 'Registrace vytvořené od půlnoci', icon: <FilePlus2 size={15} aria-hidden="true" /> },
  { key: 'pendingReview', label: 'Čeká na posouzení', hint: 'Aktuálně ve stavu „Čeká“', icon: <Clock size={15} aria-hidden="true" />, to: '/admin/kliniky?status=pending', attention: true },
  { key: 'approved', label: 'Schválené', hint: 'Aktuálně schválené kliniky', icon: <CircleCheck size={15} aria-hidden="true" />, to: '/admin/kliniky?status=approved' },
  { key: 'rejected', label: 'Zamítnuté', hint: 'Aktuálně zamítnuté kliniky', icon: <CircleX size={15} aria-hidden="true" />, to: '/admin/kliniky?status=rejected' },
  { key: 'uniqueClinicSignInsToday', label: 'Přihlášené kliniky dnes', hint: 'Různé kliniky přihlášené kódem dnes', icon: <LogIn size={15} aria-hidden="true" /> },
]

const activityIcons: Record<ActivityType, ReactNode> = {
  clinicRegistered: <FilePlus2 size={16} aria-hidden="true" />,
  clinicEmailVerified: <MailCheck size={16} aria-hidden="true" />,
  clinicSignedIn: <LogIn size={16} aria-hidden="true" />,
  clinicProfileUpdated: <UserPen size={16} aria-hidden="true" />,
  clinicStatusChanged: <CircleCheck size={16} aria-hidden="true" />,
  adminNoteAdded: <NotebookPen size={16} aria-hidden="true" />,
  adminSignedIn: <LogIn size={16} aria-hidden="true" />,
}

function ActivityRow({ item }: { item: ActivityItem }) {
  return (
    <li>
      <span className="activity-icon">{activityIcons[item.type]}</span>
      <div className="activity-text">
        {item.clinicId && item.clinicName ? <Link to={`/admin/kliniky/${item.clinicId}`}>{item.clinicName}</Link> : <strong>Smazaná klinika</strong>}
        <div>{describeActivity(item)}</div>
        <time className="activity-time" dateTime={item.occurredAt} title={formatDateTime(item.occurredAt)}>
          {formatRelative(item.occurredAt)}
        </time>
      </div>
    </li>
  )
}

export function AdminDashboardPage() {
  const { data, error, loading, reload } = useAdminLoader(() => adminApi.dashboard(), [])

  return (
    <div className="container page">
      <div className="page-header">
        <div>
          <h1>Přehled</h1>
          <p>Statistiky z uložených registrací. Dny se počítají v časovém pásmu {data?.timeZone ?? 'Europe/Prague'}.</p>
        </div>
        <button type="button" className="btn btn-sm" onClick={() => void reload()} disabled={loading}>
          <RefreshCw size={16} aria-hidden="true" /> Obnovit
        </button>
      </div>

      {error && (
        <Alert kind="error" title="Přehled se nepodařilo načíst">
          <p>{error}</p>
        </Alert>
      )}
      {loading && !data && <LoadingBlock label="Načítám statistiky…" />}

      {data && (
        <>
          <div className="metric-grid">
            {metricDefs.map((m) => {
              const content = (
                <>
                  <span className="metric-label">
                    {m.icon}
                    {m.label}
                  </span>
                  <span className="metric-value">{data.metrics[m.key]}</span>
                  <span className="metric-hint">{m.hint}</span>
                </>
              )
              const className = `card metric ${m.attention && data.metrics[m.key] > 0 ? 'metric-attention' : ''}`
              return m.to ? (
                <Link key={m.key} to={m.to} className={className}>
                  {content}
                </Link>
              ) : (
                <div key={m.key} className={className}>
                  {content}
                </div>
              )
            })}
          </div>

          <div className="dashboard-grid">
            <section className="card" aria-labelledby="chart-title">
              <div className="card-header">
                <h2 id="chart-title">Registrace za posledních 7 dní</h2>
                <span className="small muted">
                  Celkem {data.registrationsLast7Days.reduce((sum, d) => sum + d.count, 0)}
                </span>
              </div>
              <div className="card-body">
                <BarChart data={data.registrationsLast7Days} title="Počet registrací za posledních 7 dní" />
              </div>
            </section>

            <section className="card" aria-labelledby="activity-title">
              <div className="card-header">
                <h2 id="activity-title">Poslední aktivita</h2>
                <Link to="/admin/kliniky" className="small">
                  Všechny kliniky <ArrowRight size={14} aria-hidden="true" style={{ verticalAlign: '-2px' }} />
                </Link>
              </div>
              {data.recentActivity.length === 0 ? (
                <EmptyState title="Zatím žádná aktivita">
                  <p style={{ margin: 0 }}>Jakmile se zaregistruje první klinika, uvidíte ji zde.</p>
                </EmptyState>
              ) : (
                <ul className="activity-list">
                  {data.recentActivity.map((item) => (
                    <ActivityRow key={item.id} item={item} />
                  ))}
                </ul>
              )}
            </section>
          </div>
          {data.metrics.emailUnverified > 0 && (
            <p className="small muted" style={{ marginTop: 16 }}>
              {data.metrics.emailUnverified} z registrovaných klinik zatím neověřilo e-mail.
            </p>
          )}
        </>
      )}
    </div>
  )
}
