import { ChevronLeft, ChevronRight, CircleCheck, CircleX, MessageSquare, RotateCcw, Search, SearchX, Trash2, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../../api/client'
import { adminApi } from '../../api/endpoints'
import type { ClinicStatus } from '../../api/types'
import { bulkSummary, useRealtimeEvents } from '../../admin/realtime'
import { useAdminSession } from '../../auth/sessions'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { useToast } from '../../components/Toast'
import { Alert, EmptyState, Field, LoadingBlock, StatusBadge } from '../../components/ui'
import { formatDateTime, pluralize, statusActionLabels, statusLabels } from '../../lib/format'
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

const bulkActions: { status: ClinicStatus; icon: typeof CircleCheck; button: string; variant: 'success' | 'danger-solid' | 'dark' }[] = [
  { status: 'approved', icon: CircleCheck, button: 'btn-success', variant: 'success' },
  { status: 'rejected', icon: CircleX, button: 'btn-danger', variant: 'danger-solid' },
  { status: 'pending', icon: RotateCcw, button: '', variant: 'dark' },
]

const PAGE_SIZE = 20

export function AdminClinicsPage() {
  const [params, setParams] = useSearchParams()
  const { plans } = usePlans()
  const session = useAdminSession()
  const toast = useToast()
  const search = params.get('search') ?? ''
  const status = params.get('status') ?? ''
  const plan = params.get('plan') ?? ''
  const unread = params.get('unread') === '1'
  const sort = params.get('sort') ?? 'createdAt:desc'
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1)
  const [searchInput, setSearchInput] = useState(search)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [bulkTarget, setBulkTarget] = useState<ClinicStatus | null>(null)
  const [bulkMessage, setBulkMessage] = useState('')
  const [bulkError, setBulkError] = useState<string | null>(null)
  const [bulkBusy, setBulkBusy] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)

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
  const { data, error, loading, reload } = useAdminLoader(
    () => adminApi.clinics({ search, status, plan, unread, sort: sortField, direction, page, pageSize: PAGE_SIZE }),
    [search, status, plan, unread, sortField, direction, page],
  )

  // The selection belongs to the visible page; it is cleared whenever the list changes.
  useEffect(() => setSelected(new Set()), [search, status, plan, unread, sort, page])

  // Live updates: new registrations, messages and status changes refresh the list (coalesced).
  const timer = useRef<number | undefined>(undefined)
  useRealtimeEvents(() => {
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => void reload(), 400)
  })

  const planName = (code: string) => plans?.find((p) => p.code === code)?.name ?? code
  const hasFilters = Boolean(search || status || plan || unread)
  const pageIds = data?.items.map((c) => c.id) ?? []
  const allSelected = pageIds.length > 0 && pageIds.every((id) => selected.has(id))

  const toggle = (id: string) =>
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const openBulk = (target: ClinicStatus) => {
    setBulkTarget(target)
    setBulkMessage('')
    setBulkError(null)
  }

  const confirmBulk = async () => {
    if (!bulkTarget) return
    if (bulkTarget === 'rejected' && bulkMessage.trim() === '') {
      setBulkError('Uveďte důvod zamítnutí. Zobrazí se všem vybraným klinikám a přijde jim e-mailem.')
      return
    }
    setBulkBusy(true)
    setBulkError(null)
    try {
      const result = await adminApi.bulkStatus({ clinicIds: [...selected], status: bulkTarget, message: bulkMessage.trim() || null })
      toast.show(`${bulkSummary(result.updated.length, result.skipped.length)}.`)
      setBulkTarget(null)
      setSelected(new Set())
      await reload()
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        session.markSignedOut()
        return
      }
      setBulkError(e instanceof ApiError ? (e.fieldErrors.message ?? e.message) : 'Změnu se nepodařilo uložit.')
    } finally {
      setBulkBusy(false)
    }
  }

  const confirmDelete = async () => {
    setBulkBusy(true)
    setBulkError(null)
    try {
      const result = await adminApi.bulkDelete([...selected])
      toast.show(`Smazáno ${result.deleted.length} ${pluralize(result.deleted.length, 'klinika', 'kliniky', 'klinik')}.`)
      setDeleteOpen(false)
      setSelected(new Set())
      await reload()
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        session.markSignedOut()
        return
      }
      setBulkError(e instanceof ApiError ? e.message : 'Kliniky se nepodařilo smazat.')
    } finally {
      setBulkBusy(false)
    }
  }

  return (
    <div className="container page">
      <div className="page-header">
        <div>
          <h1>Kliniky</h1>
          <p>
            {data
              ? `${data.totalCount} ${pluralize(data.totalCount, 'klinika', 'kliniky', 'klinik')}${hasFilters ? ` ${pluralize(data.totalCount, 'odpovídá', 'odpovídají', 'odpovídá')} filtrům` : ''}`
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
              placeholder="Název, e-mail, město nebo IČO"
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
          <label className="checkbox toolbar-toggle">
            <input type="checkbox" checked={unread} onChange={(e) => update({ unread: e.target.checked ? '1' : '' })} />
            <span>Jen s nepřečtenými zprávami</span>
          </label>
        </div>

        {selected.size > 0 && (
          <div className="bulk-bar" role="region" aria-label="Hromadné akce">
            <strong>
              Vybráno {selected.size} {pluralize(selected.size, 'klinika', 'kliniky', 'klinik')}
            </strong>
            <div className="inline-actions">
              {bulkActions.map(({ status: target, icon: Icon, button }) => (
                <button key={target} type="button" className={`btn btn-sm ${button}`} onClick={() => openBulk(target)}>
                  <Icon size={16} aria-hidden="true" /> {statusActionLabels[target].replace(' registraci', '')}
                </button>
              ))}
              <button
                type="button"
                className="btn btn-sm btn-danger"
                onClick={() => {
                  setBulkError(null)
                  setDeleteOpen(true)
                }}
              >
                <Trash2 size={16} aria-hidden="true" /> Smazat
              </button>
              <button type="button" className="btn btn-sm btn-ghost" onClick={() => setSelected(new Set())}>
                <X size={16} aria-hidden="true" /> Zrušit výběr
              </button>
            </div>
          </div>
        )}

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
            <div className={`table-wrap ${loading ? 'is-refreshing' : ''}`} aria-busy={loading}>
              <table className="table table-responsive">
                <thead>
                  <tr>
                    <th scope="col" className="col-select">
                      <input
                        type="checkbox"
                        checked={allSelected}
                        aria-label="Vybrat všechny kliniky na stránce"
                        onChange={() => setSelected(allSelected ? new Set() : new Set(pageIds))}
                      />
                    </th>
                    <th scope="col">Klinika</th>
                    <th scope="col">IČO</th>
                    <th scope="col">Plán</th>
                    <th scope="col">Stav</th>
                    <th scope="col">Zprávy</th>
                    <th scope="col">Registrováno</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((c) => (
                    <tr key={c.id} data-selected={selected.has(c.id)}>
                      <td className="col-select" data-label="Vybrat">
                        <input type="checkbox" checked={selected.has(c.id)} onChange={() => toggle(c.id)} aria-label={`Vybrat ${c.name}`} />
                      </td>
                      <td data-label="Klinika">
                        <Link to={`/admin/kliniky/${c.id}`}>{c.name}</Link>
                        <div className="small muted">
                          {c.email} · {c.city}
                          {!c.emailVerified && ' · e-mail neověřen'}
                        </div>
                      </td>
                      <td data-label="IČO" className="mono">
                        {c.organizationId ?? '—'}
                      </td>
                      <td data-label="Plán">{planName(c.planCode)}</td>
                      <td data-label="Stav">
                        <StatusBadge status={c.status} />
                      </td>
                      <td data-label="Zprávy">
                        {c.unreadMessages > 0 ? (
                          <Link to={`/admin/kliniky/${c.id}#zpravy`} className="badge badge-accent unread-badge">
                            <MessageSquare size={12} aria-hidden="true" /> {c.unreadMessages} {pluralize(c.unreadMessages, 'nová', 'nové', 'nových')}
                          </Link>
                        ) : (
                          <span className="muted">—</span>
                        )}
                      </td>
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
                <button type="button" className="btn btn-sm" disabled={data.page >= data.totalPages} onClick={() => update({ page: String(data.page + 1) })}>
                  Další <ChevronRight size={16} aria-hidden="true" />
                </button>
              </div>
            </nav>
          </>
        )}
      </section>

      <ConfirmDialog
        open={bulkTarget !== null}
        title={bulkTarget ? `${statusActionLabels[bulkTarget]} – ${selected.size} ${pluralize(selected.size, 'klinika', 'kliniky', 'klinik')}?` : ''}
        confirmLabel={bulkTarget ? statusActionLabels[bulkTarget].replace(' registraci', '') : ''}
        confirmVariant={bulkActions.find((a) => a.status === bulkTarget)?.variant ?? 'primary'}
        busy={bulkBusy}
        onCancel={() => setBulkTarget(null)}
        onConfirm={() => void confirmBulk()}
      >
        <div className="stack">
          <p style={{ margin: 0 }}>
            Vybrané kliniky změní stav na „{bulkTarget ? statusLabels[bulkTarget] : ''}“. Kliniky, které tento stav už mají, se přeskočí.
            Každá změna se zapíše do historie a klinice přijde e-mail.
          </p>
          {bulkError && <Alert kind="error">{bulkError}</Alert>}
          <Field
            label={bulkTarget === 'rejected' ? 'Důvod zamítnutí (uvidí kliniky)' : 'Zpráva pro kliniky'}
            optional={bulkTarget !== 'rejected'}
            hint="Zobrazí se klinikám v portálu a v e-mailu."
          >
            {(a) => <textarea {...a} className="textarea" value={bulkMessage} onChange={(e) => setBulkMessage(e.target.value)} maxLength={1000} />}
          </Field>
        </div>
      </ConfirmDialog>

      <ConfirmDialog
        open={deleteOpen}
        title={`Smazat ${selected.size} ${pluralize(selected.size, 'kliniku', 'kliniky', 'klinik')} natrvalo?`}
        confirmLabel="Smazat natrvalo"
        confirmVariant="danger-solid"
        busy={bulkBusy}
        onCancel={() => setDeleteOpen(false)}
        onConfirm={() => void confirmDelete()}
      >
        <div className="stack">
          <p style={{ margin: 0 }}>
            Vybrané kliniky budou odstraněny i se všemi údaji, historií stavů, poznámkami a zprávami. V audit logu zůstane záznam
            o smazání. Tuto akci nelze vrátit.
          </p>
          {bulkError && <Alert kind="error">{bulkError}</Alert>}
        </div>
      </ConfirmDialog>
    </div>
  )
}
