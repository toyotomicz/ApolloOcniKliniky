import { MapPinOff } from 'lucide-react'
import { useCallback, useRef } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { adminApi } from '../../api/endpoints'
import { useRealtimeEvents } from '../../admin/realtime'
import { ClinicMap, MapLegend } from '../../components/ClinicMap'
import { Alert, LoadingBlock } from '../../components/ui'
import { pluralize, statusLabels } from '../../lib/format'
import { usePlans } from '../../lib/usePlans'
import { useAdminLoader } from './useAdminLoader'

export function AdminMapPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const { plans } = usePlans()
  const status = params.get('status') ?? ''
  const plan = params.get('plan') ?? ''
  const { data, error, loading, reload } = useAdminLoader(() => adminApi.map({ status, plan }), [status, plan])

  // New registrations show up once their address is resolved; refresh shortly after the event.
  const timer = useRef<number | undefined>(undefined)
  useRealtimeEvents((event) => {
    if (event.type === 'messageReceived') return
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => void reload(), 3000)
  })

  const update = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    setParams(next, { replace: true })
  }
  const open = useCallback((id: string) => navigate(`/admin/kliniky/${id}`), [navigate])

  return (
    <div className="container page">
      <div className="page-header">
        <div>
          <h1>Mapa klinik</h1>
          <p>
            {data
              ? `${data.items.length} ${pluralize(data.items.length, 'klinika', 'kliniky', 'klinik')} na mapě`
              : 'Poloha klinik podle adresy provozovny'}
          </p>
        </div>
      </div>

      <section className="card" aria-label="Mapa klinik">
        <div className="toolbar toolbar-compact">
          <div>
            <label htmlFor="map-status" className="visually-hidden">
              Stav
            </label>
            <select id="map-status" className="select" value={status} onChange={(e) => update('status', e.target.value)}>
              <option value="">Všechny stavy</option>
              {Object.entries(statusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="map-plan" className="visually-hidden">
              Plán
            </label>
            <select id="map-plan" className="select" value={plan} onChange={(e) => update('plan', e.target.value)}>
              <option value="">Všechny plány</option>
              {plans?.map((p) => (
                <option key={p.code} value={p.code}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <MapLegend />
        </div>
        {error && (
          <div className="card-body">
            <Alert kind="error">{error}</Alert>
          </div>
        )}
        {loading && !data && <LoadingBlock label="Načítám mapu…" />}
        {data && (
          <div className={loading ? 'is-refreshing' : undefined}>
            <ClinicMap points={data.items} label="Mapa registrovaných klinik" height="min(68vh, 640px)" onOpen={open} />
            {data.withoutLocation > 0 && (
              <p className="small muted map-note">
                <MapPinOff size={14} aria-hidden="true" /> {data.withoutLocation}{' '}
                {pluralize(data.withoutLocation, 'klinika zatím nemá', 'kliniky zatím nemají', 'klinik zatím nemá')} určenou polohu (adresa se
                dohledává automaticky na pozadí).
              </p>
            )}
          </div>
        )}
      </section>
    </div>
  )
}
