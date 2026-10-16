import {
  Activity,
  ArrowRight,
  ChevronRight,
  CircleCheck,
  CircleX,
  Clock,
  FilePlus2,
  Hourglass,
  LogIn,
  MailCheck,
  MessageSquare,
  NotebookPen,
  Percent,
  RefreshCw,
  UserPen,
} from 'lucide-react'
import { useEffect, useRef, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { adminApi } from '../../api/endpoints'
import type { ActivityItem, ActivityType, Dashboard } from '../../api/types'
import { useRealtimeEvents } from '../../admin/realtime'
import { BarChart, HorizontalBars } from '../../components/BarChart'
import { Alert, EmptyState, LoadingBlock } from '../../components/ui'
import { describeActivity, formatDateTime, formatHours, formatRelative } from '../../lib/format'
import { useAdminLoader } from './useAdminLoader'

interface MetricDef {
  key: string
  label: string
  hint: string
  icon: ReactNode
  value: (d: Dashboard) => string
  to?: string
  attention?: (d: Dashboard) => boolean
}

// The definitions mirror DashboardMetrics in DashboardService.cs.
// First row: clinic counts that open the filtered clinic list. Second row: information only.
const linkMetrics: MetricDef[] = [
  { key: 'total', label: 'Registrované kliniky', hint: 'Všechny registrace', icon: <Activity size={15} aria-hidden="true" />, value: (d) => String(d.metrics.totalClinics), to: '/admin/kliniky' },
  { key: 'pending', label: 'Čeká na posouzení', hint: 'Ve stavu „Čeká“', icon: <Clock size={15} aria-hidden="true" />, value: (d) => String(d.metrics.pendingReview), to: '/admin/kliniky?status=pending', attention: (d) => d.metrics.pendingReview > 0 },
  { key: 'unread', label: 'Nepřečtené zprávy', hint: 'Od klinik, nikdo neotevřel', icon: <MessageSquare size={15} aria-hidden="true" />, value: (d) => String(d.metrics.unreadMessages), to: '/admin/kliniky?unread=1', attention: (d) => d.metrics.unreadMessages > 0 },
  { key: 'approved', label: 'Schválené', hint: 'Aktuálně schválené', icon: <CircleCheck size={15} aria-hidden="true" />, value: (d) => String(d.metrics.approved), to: '/admin/kliniky?status=approved' },
  { key: 'rejected', label: 'Zamítnuté', hint: 'Aktuálně zamítnuté', icon: <CircleX size={15} aria-hidden="true" />, value: (d) => String(d.metrics.rejected), to: '/admin/kliniky?status=rejected' },
]

const infoMetrics: MetricDef[] = [
  { key: 'new', label: 'Nové dnes', hint: 'Registrace od půlnoci', icon: <FilePlus2 size={15} aria-hidden="true" />, value: (d) => String(d.metrics.newToday) },
  { key: 'signins', label: 'Přihlášené dnes', hint: 'Různé kliniky přihlášené kódem', icon: <LogIn size={15} aria-hidden="true" />, value: (d) => String(d.metrics.uniqueClinicSignInsToday) },
  { key: 'decision', label: 'Doba do rozhodnutí', hint: 'Průměr za 90 dní', icon: <Hourglass size={15} aria-hidden="true" />, value: (d) => formatHours(d.metrics.averageDecisionHours) },
  { key: 'rate', label: 'Míra schválení', hint: 'Schválené z rozhodnutých', icon: <Percent size={15} aria-hidden="true" />, value: (d) => (d.metrics.approvalRatePercent === null ? '—' : `${d.metrics.approvalRatePercent} %`) },
]

function MetricTile({ metric, data }: { metric: MetricDef; data: Dashboard }) {
  const attention = metric.attention?.(data) ? 'metric-attention' : ''
  const body = (
    <>
      <span className="metric-label">
        {metric.icon}
        <span>{metric.label}</span>
        {metric.to && <ChevronRight size={16} aria-hidden="true" className="metric-arrow" />}
      </span>
      <span className="metric-value">{metric.value(data)}</span>
      <span className="metric-hint">{metric.hint}</span>
    </>
  )
  return metric.to ? (
    <Link to={metric.to} className={`card metric metric-link ${attention}`}>
      {body}
    </Link>
  ) : (
    <div className={`metric metric-info ${attention}`}>{body}</div>
  )
}

const activityIcons: Partial<Record<ActivityType, ReactNode>> = {
  clinicRegistered: <FilePlus2 size={16} aria-hidden="true" />,
  clinicEmailVerified: <MailCheck size={16} aria-hidden="true" />,
  clinicProfileUpdated: <UserPen size={16} aria-hidden="true" />,
  clinicStatusChanged: <CircleCheck size={16} aria-hidden="true" />,
  clinicMessageSent: <MessageSquare size={16} aria-hidden="true" />,
  adminMessageSent: <MessageSquare size={16} aria-hidden="true" />,
  adminNoteAdded: <NotebookPen size={16} aria-hidden="true" />,
}

function ActivityRow({ item }: { item: ActivityItem }) {
  return (
    <li>
      <span className="activity-icon">{activityIcons[item.type] ?? <Activity size={16} aria-hidden="true" />}</span>
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

const percent = (part: number, whole: number) => (whole > 0 ? `${Math.round((part / whole) * 100)} %` : '—')

export function AdminDashboardPage() {
  const [params, setParams] = useSearchParams()
  const days = params.get('dny') === '30' ? 30 : 7
  const { data, error, loading, reload } = useAdminLoader(() => adminApi.dashboard(days), [days])

  // New registrations, messages and status changes refresh the numbers (coalesced to one reload).
  const pending = useRef<number | undefined>(undefined)
  useRealtimeEvents(() => {
    window.clearTimeout(pending.current)
    pending.current = window.setTimeout(() => void reload(), 400)
  })
  useEffect(() => () => window.clearTimeout(pending.current), [])

  const setDays = (value: 7 | 30) => setParams(value === 30 ? { dny: '30' } : {}, { replace: true })

  return (
    <div className="container page">
      <div className="page-header">
        <div>
          <h1>Přehled</h1>
          <p>Statistiky z uložených registrací. Dny se počítají v časovém pásmu {data?.timeZone ?? 'Europe/Prague'}.</p>
        </div>
        <div className="inline-actions">
          <div className="segmented" role="group" aria-label="Období grafu registrací">
            {([7, 30] as const).map((value) => (
              <button key={value} type="button" aria-pressed={days === value} onClick={() => setDays(value)}>
                {value} dní
              </button>
            ))}
          </div>
          <button type="button" className="btn btn-sm" onClick={() => void reload()} disabled={loading}>
            <RefreshCw size={16} aria-hidden="true" /> Obnovit
          </button>
        </div>
      </div>

      {error && (
        <Alert kind="error" title="Přehled se nepodařilo načíst">
          <p>{error}</p>
        </Alert>
      )}
      {loading && !data && <LoadingBlock label="Načítám statistiky…" />}

      {data && (
        // While refetching, the previous numbers stay visible (no skeleton flash).
        <div className={loading ? 'is-refreshing' : undefined} aria-busy={loading}>
          <h2 className="metric-group-title">Kliniky</h2>
          <div className="metric-grid metric-grid-links">
            {linkMetrics.map((m) => (
              <MetricTile key={m.key} metric={m} data={data} />
            ))}
          </div>
          <h2 className="metric-group-title">Aktivita a výkon</h2>
          <div className="metric-grid metric-grid-info">
            {infoMetrics.map((m) => (
              <MetricTile key={m.key} metric={m} data={data} />
            ))}
          </div>

          <div className="dashboard-grid">
            <div className="stack">
              <section className="card" aria-labelledby="chart-title">
                <div className="card-header">
                  <h2 id="chart-title">Registrace za posledních {data.seriesDays} dní</h2>
                  <span className="small muted">Celkem {data.registrations.reduce((sum, d) => sum + d.count, 0)}</span>
                </div>
                <div className="card-body">
                  <BarChart data={data.registrations} title={`Počet registrací za posledních ${data.seriesDays} dní`} />
                </div>
              </section>

              <div className="dashboard-pair">
                <section className="card" aria-labelledby="funnel-title">
                  <div className="card-header">
                    <h2 id="funnel-title">Průchod registrací</h2>
                  </div>
                  <div className="card-body">
                    <HorizontalBars
                      label="Počty klinik v jednotlivých krocích"
                      rows={[
                        { key: 'registered', label: 'Registrace', value: data.funnel.registered },
                        { key: 'verified', label: 'Ověřený e-mail', value: data.funnel.emailVerified, note: percent(data.funnel.emailVerified, data.funnel.registered) },
                        { key: 'approved', label: 'Schváleno', value: data.funnel.approved, note: percent(data.funnel.approved, data.funnel.registered) },
                      ]}
                    />
                  </div>
                </section>
                <section className="card" aria-labelledby="plans-title">
                  <div className="card-header">
                    <h2 id="plans-title">Kliniky podle plánu</h2>
                  </div>
                  <div className="card-body">
                    <HorizontalBars
                      label="Počty klinik podle plánu"
                      rows={data.plans.map((p) => ({
                        key: p.planCode,
                        label: p.planName,
                        value: p.count,
                        note: percent(p.count, data.metrics.totalClinics),
                      }))}
                    />
                  </div>
                </section>
              </div>
            </div>

            <section className="card" aria-labelledby="activity-title">
              <div className="card-header">
                <h2 id="activity-title">Poslední aktivita</h2>
                <Link to="/admin/audit" className="small">
                  Audit log <ArrowRight size={14} aria-hidden="true" style={{ verticalAlign: '-2px' }} />
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
        </div>
      )}
    </div>
  )
}
