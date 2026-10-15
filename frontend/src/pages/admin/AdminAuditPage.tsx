import { ChevronLeft, ChevronRight, Download, ScrollText, Search } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { adminApi, type AuditParams } from '../../api/endpoints'
import type { ActivityType } from '../../api/types'
import { Alert, EmptyState, LoadingBlock } from '../../components/ui'
import { activityLabels, auditActorLabels, describeAuditDetail, formatDateTime, pluralize } from '../../lib/format'
import { useAdminLoader } from './useAdminLoader'

const PAGE_SIZE = 50

export function AdminAuditPage() {
  const [params, setParams] = useSearchParams()
  const filters: AuditParams = {
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    type: params.get('type') ?? '',
    actor: params.get('actor') ?? '',
    clinicId: params.get('clinicId') ?? '',
    search: params.get('search') ?? '',
  }
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1)
  const [searchInput, setSearchInput] = useState(filters.search ?? '')

  const update = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('page' in changes)) next.delete('page')
    setParams(next, { replace: true })
  }

  useEffect(() => {
    if (searchInput === filters.search) return
    const timer = window.setTimeout(() => update({ search: searchInput.trim() }), 300)
    return () => window.clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchInput])

  const key = JSON.stringify({ ...filters, page })
  const { data, error, loading } = useAdminLoader(() => adminApi.audit({ ...filters, page, pageSize: PAGE_SIZE }), [key])
  const hasFilters = Object.values(filters).some(Boolean)

  return (
    <div className="container page">
      <div className="page-header">
        <div>
          <h1>Audit log</h1>
          <p>
            {data
              ? `${data.totalCount} ${pluralize(data.totalCount, 'záznam', 'záznamy', 'záznamů')}${hasFilters ? ` ${pluralize(data.totalCount, 'odpovídá', 'odpovídají', 'odpovídá')} filtrům` : ''}`
              : 'Kdo, kdy a co v systému udělal'}
          </p>
        </div>
        <a className="btn btn-dark btn-sm" href={adminApi.auditExportUrl(filters)} download>
          <Download size={16} aria-hidden="true" /> Stáhnout CSV
        </a>
      </div>

      <section className="card" aria-label="Záznamy audit logu">
        <div className="toolbar toolbar-audit" role="search">
          <div className="toolbar-search search-input">
            <label htmlFor="audit-search" className="visually-hidden">
              Hledat
            </label>
            <Search size={16} aria-hidden="true" />
            <input
              id="audit-search"
              className="input"
              type="search"
              placeholder="Klinika, administrátor nebo detail"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              maxLength={100}
            />
          </div>
          <div>
            <label htmlFor="audit-type" className="visually-hidden">
              Událost
            </label>
            <select id="audit-type" className="select" value={filters.type} onChange={(e) => update({ type: e.target.value })}>
              <option value="">Všechny události</option>
              {(Object.entries(activityLabels) as [ActivityType, string][])
                .sort((a, b) => a[1].localeCompare(b[1], 'cs'))
                .map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
            </select>
          </div>
          <div>
            <label htmlFor="audit-actor" className="visually-hidden">
              Kdo
            </label>
            <select id="audit-actor" className="select" value={filters.actor} onChange={(e) => update({ actor: e.target.value })}>
              <option value="">Kdokoli</option>
              {Object.entries(auditActorLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div className="date-range">
            <label>
              <span className="visually-hidden">Od</span>
              <input type="date" className="input" value={filters.from} max={filters.to || undefined} onChange={(e) => update({ from: e.target.value })} aria-label="Od data" />
            </label>
            <span aria-hidden="true">–</span>
            <label>
              <span className="visually-hidden">Do</span>
              <input type="date" className="input" value={filters.to} min={filters.from || undefined} onChange={(e) => update({ to: e.target.value })} aria-label="Do data" />
            </label>
          </div>
        </div>
        {filters.clinicId && (
          <div className="card-body" style={{ paddingBottom: 0 }}>
            <Alert kind="info">
              Zobrazeny jsou jen události jedné kliniky.{' '}
              <button type="button" className="btn-link" onClick={() => update({ clinicId: '' })}>
                Zobrazit vše
              </button>
            </Alert>
          </div>
        )}

        {error && (
          <div className="card-body">
            <Alert kind="error">{error}</Alert>
          </div>
        )}
        {loading && !data && <LoadingBlock />}
        {data && data.items.length === 0 && (
          <EmptyState title="Žádné záznamy" icon={<ScrollText size={40} aria-hidden="true" />}>
            {hasFilters && (
              <button
                type="button"
                className="btn btn-sm"
                onClick={() => {
                  setSearchInput('')
                  setParams(new URLSearchParams(), { replace: true })
                }}
              >
                Zrušit filtry
              </button>
            )}
          </EmptyState>
        )}
        {data && data.items.length > 0 && (
          <>
            <div className="table-wrap" aria-busy={loading}>
              <table className="table table-responsive">
                <thead>
                  <tr>
                    <th scope="col">Čas</th>
                    <th scope="col">Událost</th>
                    <th scope="col">Kdo</th>
                    <th scope="col">Klinika</th>
                    <th scope="col">Detail</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((item) => (
                    <tr key={item.id}>
                      <td data-label="Čas" className="nowrap">
                        {formatDateTime(item.occurredAt)}
                      </td>
                      <td data-label="Událost">{activityLabels[item.type] ?? item.type}</td>
                      <td data-label="Kdo">
                        <span className={`badge actor-${item.actor}`}>{auditActorLabels[item.actor]}</span>
                        {item.actorName && item.actor === 'administrator' && <div className="small muted">{item.actorName}</div>}
                      </td>
                      <td data-label="Klinika">
                        {item.clinicId && item.clinicName ? <Link to={`/admin/kliniky/${item.clinicId}`}>{item.clinicName}</Link> : <span className="muted">—</span>}
                      </td>
                      <td data-label="Detail" className="small">
                        {describeAuditDetail(item.type, item.detail) || <span className="muted">—</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <nav className="pagination" aria-label="Stránkování">
              <span>
                Strana {data.page} z {Math.max(data.totalPages, 1)}
              </span>
              <div className="pagination-controls">
                <button type="button" className="btn btn-sm" disabled={data.page <= 1} onClick={() => update({ page: String(data.page - 1) })}>
                  <ChevronLeft size={16} aria-hidden="true" /> Předchozí
                </button>
                <button type="button" className="btn btn-sm" disabled={data.page >= data.totalPages} onClick={() => update({ page: String(data.page + 1) })}>
                  Další <ChevronRight size={16} aria-hidden="true" />
                </button>
              </div>
            </nav>
          </>
        )}
      </section>
    </div>
  )
}
