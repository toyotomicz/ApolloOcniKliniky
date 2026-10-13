import { ChevronLeft, ChevronRight, SearchX, Search } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { adminApi } from '../../api/endpoints'
import { Alert, EmptyState, LoadingBlock, StatusBadge } from '../../components/ui'
import { formatDateTime, pluralize, statusLabels } from '../../lib/format'
import { usePlans } from '../../lib/usePlans'
import { useAdminLoader } from './useAdminLoader'

const sortOptions = [
  { value: 'createdAt:desc', label: 'Nejnovější' },
  { value: 'createdAt:asc', label: 'Nejstarší' },
  { value: 'name:asc', label: 'Název A–Z' },
  { value: 'name:desc', label: 'Název Z–A' },
  { value: 'status:asc', label: 'Podle stavu' },
  { value: 'plan:asc', label: 'Podle plánu' },
]

const PAGE_SIZE = 20

export function AdminClinicsPage() {
  const [params, setParams] = useSearchParams()
  const { plans } = usePlans()
  const search = params.get('search') ?? ''
  const status = params.get('status') ?? ''
  const plan = params.get('plan') ?? ''
  const sort = params.get('sort') ?? 'createdAt:desc'
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1)
  const [searchInput, setSearchInput] = useState(search)

  const update = (changes: Record<string, string>) => {
    const nextParams = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value) nextParams.set(key, value)
      else nextParams.delete(key)
    }
    if (!('page' in changes)) nextParams.delete('page')
    setParams(nextParams, { replace: true })
  }

  // Debounced search: the URL (and thus the request) updates 300 ms after typing stops.
  useEffect(() => {
    if (searchInput === search) return
    const timer = window.setTimeout(() => update({ search: searchInput.trim() }), 300)
    return () => window.clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchInput])

  const [sortField, direction] = sort.split(':')
  const { data, error, loading } = useAdminLoader(
    () => adminApi.clinics({ search, status, plan, sort: sortField, direction, page, pageSize: PAGE_SIZE }),
    [search, status, plan, sortField, direction, page],
  )

  const planName = (code: string) => plans?.find((p) => p.code === code)?.name ?? code
  const hasFilters = Boolean(search || status || plan)

  return (
    <div className="container page">
      <div className="page-header">
        <div>
          <h1>Kliniky</h1>
          <p>
            {data
              ? `${data.totalCount} ${pluralize(data.totalCount, 'klinika', 'kliniky', 'klinik')}${hasFilters ? ' odpovídá filtrům' : ''}`
              : 'Seznam registrovaných klinik'}
          </p>
        </div>
      </div>

      <section className="card" aria-label="Seznam klinik">
        <div className="toolbar" role="search">
          <div className="toolbar-search search-input">
            <label htmlFor="clinic-search" className="visually-hidden">
              Hledat kliniku
            </label>
            <Search size={16} aria-hidden="true" />
            <input
              id="clinic-search"
              className="input"
              type="search"
              placeholder="Název, e-mail nebo IČO"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              maxLength={100}
            />
          </div>
          <div>
            <label htmlFor="filter-status" className="visually-hidden">
              Stav
            </label>
            <select id="filter-status" className="select" value={status} onChange={(e) => update({ status: e.target.value })}>
              <option value="">Všechny stavy</option>
              {Object.entries(statusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="filter-plan" className="visually-hidden">
              Plán
            </label>
            <select id="filter-plan" className="select" value={plan} onChange={(e) => update({ plan: e.target.value })}>
              <option value="">Všechny plány</option>
              {plans?.map((p) => (
                <option key={p.code} value={p.code}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="sort" className="visually-hidden">
              Řazení
            </label>
            <select id="sort" className="select" value={sort} onChange={(e) => update({ sort: e.target.value })}>
              {sortOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
        </div>

        {error && (
          <div className="card-body">
            <Alert kind="error">{error}</Alert>
          </div>
        )}
        {loading && !data && <LoadingBlock />}

        {data && data.items.length === 0 && (
          <EmptyState title={hasFilters ? 'Žádná klinika neodpovídá filtrům' : 'Zatím nejsou registrované žádné kliniky'} icon={<SearchX size={40} aria-hidden="true" />}>
            {hasFilters ? (
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
            ) : (
              <p style={{ margin: 0 }}>Nové registrace se zde objeví automaticky.</p>
            )}
          </EmptyState>
        )}

        {data && data.items.length > 0 && (
          <>
            <div className="table-wrap" aria-busy={loading}>
              <table className="table table-responsive">
                <thead>
                  <tr>
                    <th scope="col">Klinika</th>
                    <th scope="col">IČO</th>
                    <th scope="col">Plán</th>
                    <th scope="col">Stav</th>
                    <th scope="col">E-mail ověřen</th>
                    <th scope="col">Registrováno</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((c) => (
                    <tr key={c.id}>
                      <td data-label="Klinika">
                        <Link to={`/admin/kliniky/${c.id}`}>{c.name}</Link>
                        <div className="small muted">
                          {c.email} · {c.city}
                        </div>
                      </td>
                      <td data-label="IČO" className="mono">
                        {c.organizationId ?? '—'}
                      </td>
                      <td data-label="Plán">{planName(c.planCode)}</td>
                      <td data-label="Stav">
                        <StatusBadge status={c.status} />
                      </td>
                      <td data-label="E-mail ověřen">{c.emailVerified ? 'Ano' : <span className="muted">Ne</span>}</td>
                      <td data-label="Registrováno">{formatDateTime(c.createdAt)}</td>
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
                <button
                  type="button"
                  className="btn btn-sm"
                  disabled={data.page >= data.totalPages}
                  onClick={() => update({ page: String(data.page + 1) })}
                >
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
