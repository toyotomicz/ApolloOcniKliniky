import { ArrowLeft, CircleCheck, CircleX, FileDown, MessageSquare, NotebookPen, RotateCcw, ScrollText, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { ApiError } from '../../api/client'
import { adminApi } from '../../api/endpoints'
import type { AdminClinicDetail, ClinicStatus } from '../../api/types'
import { useRealtimeEvents } from '../../admin/realtime'
import { useAdminSession } from '../../auth/sessions'
import { ClinicDetails, PlanSummary } from '../../components/ClinicDetails'
import { ClinicMap } from '../../components/ClinicMap'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { MessageThread } from '../../components/MessageThread'
import { useToast } from '../../components/Toast'
import { Alert, DefinitionList, EmptyState, Field, LoadingBlock, Spinner, StatusBadge } from '../../components/ui'
import { formatDateTime, statusActionLabels, statusLabels } from '../../lib/format'
import { useAdminLoader } from './useAdminLoader'

const actionStyle: Record<ClinicStatus, { variant: 'success' | 'danger-solid' | 'dark'; button: string; icon: typeof CircleCheck }> = {
  approved: { variant: 'success', button: 'btn-success', icon: CircleCheck },
  rejected: { variant: 'danger-solid', button: 'btn-danger', icon: CircleX },
  pending: { variant: 'dark', button: '', icon: RotateCcw },
}

export function AdminClinicDetailPage() {
  const { id = '' } = useParams()
  const { data, setData, error, loading, reload } = useAdminLoader(() => adminApi.clinic(id), [id])

  // Another administrator (or the clinic itself) changed this clinic: show the current data.
  useRealtimeEvents((event) => {
    if (event.type === 'clinicsChanged' && event.clinicIds.includes(id)) void reload()
  })

  return (
    <div className="container page">
      <Link to="/admin/kliniky" className="back-link">
        <ArrowLeft size={16} aria-hidden="true" /> Zpět na seznam klinik
      </Link>
      {error && (
        <Alert kind="error" title="Detail kliniky se nepodařilo načíst">
          <p>{error}</p>
        </Alert>
      )}
      {loading && !data && <LoadingBlock />}
      {data && <ClinicDetailView detail={data} onChange={setData} onReload={reload} />}
    </div>
  )
}

function ClinicDetailView({
  detail,
  onChange,
  onReload,
}: {
  detail: AdminClinicDetail
  onChange: (detail: AdminClinicDetail) => void
  onReload: () => Promise<void>
}) {
  const clinic = detail.profile
  const mapPoints = useMemo(
    () =>
      detail.location
        ? [{ id: clinic.id, name: clinic.name, city: clinic.city, status: clinic.status, latitude: detail.location.latitude, longitude: detail.location.longitude }]
        : [],
    [detail.location, clinic.id, clinic.name, clinic.city, clinic.status],
  )
  return (
    <>
      <div className="page-header">
        <div>
          <h1>{clinic.name}</h1>
          <p>
            Plán {clinic.plan.name} · registrováno {formatDateTime(clinic.createdAt)}
            {detail.isDemo && <span className="badge" style={{ marginLeft: 8 }}>Demo data</span>}
          </p>
        </div>
        <div className="inline-actions">
          <StatusBadge status={clinic.status} />
          <a className="btn btn-sm" href={adminApi.confirmationUrl(clinic.id)} download>
            <FileDown size={16} aria-hidden="true" /> Potvrzení (PDF)
          </a>
          <Link className="btn btn-sm" to={`/admin/audit?clinicId=${clinic.id}`}>
            <ScrollText size={16} aria-hidden="true" /> Audit
          </Link>
        </div>
      </div>

      <div className="portal-grid">
        <div className="stack">
          <ClinicDetails clinic={clinic} />
          <MessagesPanel clinicId={clinic.id} />
          <StatusHistory detail={detail} />
        </div>
        <aside className="stack" aria-label="Administrace kliniky">
          <DecisionPanel detail={detail} onChange={onChange} onReload={onReload} />
          <section className="card" aria-labelledby="meta-title">
            <div className="card-header">
              <h2 id="meta-title">Stav účtu</h2>
            </div>
            <div className="card-body">
              <DefinitionList
                items={[
                  ['E-mail ověřen', clinic.emailVerifiedAt ? formatDateTime(clinic.emailVerifiedAt) : <span className="muted">Neověřen</span>],
                  ['Poslední přihlášení', formatDateTime(detail.lastSignInAt)],
                  ['Změna stavu', formatDateTime(clinic.statusChangedAt)],
                  ['Zásady zpracování', `${formatDateTime(detail.privacyNoticeAcceptedAt)} (verze ${detail.privacyNoticeVersion})`],
                ]}
              />
            </div>
          </section>
          <NotesPanel detail={detail} onChange={onChange} />
          <section className="card" aria-labelledby="location-title">
            <div className="card-header">
              <h2 id="location-title">Poloha</h2>
            </div>
            {mapPoints.length > 0 ? (
              <ClinicMap points={mapPoints} label={`Poloha kliniky ${clinic.name}`} height={220} singleZoom={14} expandable />
            ) : (
              <div className="card-body">
                <p className="muted small" style={{ margin: 0 }}>
                  Poloha zatím není určena. Adresa se dohledává automaticky na pozadí.
                </p>
              </div>
            )}
          </section>
          <PlanSummary clinic={clinic} />
          <DeletePanel detail={detail} />
        </aside>
      </div>
    </>
  )
}

function DecisionPanel({ detail, onChange, onReload }: { detail: AdminClinicDetail; onChange: (d: AdminClinicDetail) => void; onReload: () => Promise<void> }) {
  const session = useAdminSession()
  const toast = useToast()
  const [target, setTarget] = useState<ClinicStatus | null>(null)
  const [message, setMessage] = useState('')
  const [internalNote, setInternalNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [dialogError, setDialogError] = useState<string | null>(null)
  const [fieldError, setFieldError] = useState<string | null>(null)
  const clinic = detail.profile

  const open = (status: ClinicStatus) => {
    setTarget(status)
    setMessage(status === 'approved' ? '' : (clinic.statusMessage ?? ''))
    setInternalNote('')
    setDialogError(null)
    setFieldError(null)
  }

  const confirm = async () => {
    if (!target) return
    if (target === 'rejected' && message.trim() === '') {
      setFieldError('Uveďte důvod zamítnutí. Zobrazí se klinice v jejím portálu.')
      return
    }
    setBusy(true)
    setDialogError(null)
    try {
      const updated = await adminApi.changeStatus(clinic.id, {
        status: target,
        message: message.trim() || null,
        internalNote: internalNote.trim() || null,
        version: detail.version,
      })
      onChange(updated)
      setTarget(null)
      toast.show(`Stav kliniky změněn na „${statusLabels[target]}“.`)
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        session.markSignedOut()
        return
      }
      if (e instanceof ApiError && e.code === 'concurrency_conflict') {
        setTarget(null)
        toast.show(e.message, 'error')
        await onReload()
        return
      }
      setDialogError(e instanceof ApiError ? e.message : 'Změnu se nepodařilo uložit.')
      if (e instanceof ApiError && e.fieldErrors.message) setFieldError(e.fieldErrors.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card" aria-labelledby="decision-title">
      <div className="card-header">
        <h2 id="decision-title">Rozhodnutí</h2>
      </div>
      <div className="card-body stack">
        <p className="muted small" style={{ margin: 0 }}>
          Aktuální stav: <strong>{statusLabels[clinic.status]}</strong>
        </p>
        {!clinic.emailVerifiedAt && (
          <Alert kind="warning">Klinika zatím neověřila e-mail.</Alert>
        )}
        <div className="decision-actions">
          {detail.allowedStatusChanges.map((status) => {
            const style = actionStyle[status]
            const Icon = style.icon
            return (
              <button key={status} type="button" className={`btn btn-block ${style.button}`} onClick={() => open(status)}>
                <Icon size={18} aria-hidden="true" />
                {statusActionLabels[status]}
              </button>
            )
          })}
        </div>
      </div>

      <ConfirmDialog
        open={target !== null}
        title={target ? `${statusActionLabels[target]}?` : ''}
        confirmLabel={target ? statusActionLabels[target] : ''}
        confirmVariant={target ? actionStyle[target].variant : 'primary'}
        busy={busy}
        onCancel={() => setTarget(null)}
        onConfirm={() => void confirm()}
      >
        <div className="stack">
          <p style={{ margin: 0 }}>
            Klinika <strong>{clinic.name}</strong> změní stav z „{statusLabels[clinic.status]}“ na „{target ? statusLabels[target] : ''}“.
            Změna se zaznamená do historie.
          </p>
          {target === 'approved' && !clinic.emailVerifiedAt && (
            <Alert kind="warning">Klinika zatím neověřila svůj e-mail. Schválení je přesto možné.</Alert>
          )}
          {dialogError && <Alert kind="error">{dialogError}</Alert>}
          <Field
            label={target === 'rejected' ? 'Důvod zamítnutí (uvidí klinika)' : 'Zpráva pro kliniku'}
            optional={target !== 'rejected'}
            error={fieldError ?? undefined}
            hint="Zobrazí se klinice v jejím portálu."
          >
            {(a) => <textarea {...a} className="textarea" value={message} onChange={(e) => setMessage(e.target.value)} maxLength={1000} />}
          </Field>
          <Field label="Interní poznámka" optional hint="Vidí pouze administrátoři.">
            {(a) => <textarea {...a} className="textarea" value={internalNote} onChange={(e) => setInternalNote(e.target.value)} maxLength={2000} />}
          </Field>
        </div>
      </ConfirmDialog>
    </section>
  )
}

function DeletePanel({ detail }: { detail: AdminClinicDetail }) {
  const session = useAdminSession()
  const toast = useToast()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const clinic = detail.profile

  const confirm = async () => {
    setBusy(true)
    setError(null)
    try {
      await adminApi.deleteClinic(clinic.id)
      toast.show(`Klinika „${clinic.name}“ byla smazána.`)
      navigate('/admin/kliniky', { replace: true })
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        session.markSignedOut()
        return
      }
      setError(e instanceof ApiError ? e.message : 'Kliniku se nepodařilo smazat.')
      setBusy(false)
    }
  }

  return (
    <section className="card danger-zone" aria-labelledby="delete-title">
      <div className="card-header">
        <h2 id="delete-title">Smazání kliniky</h2>
      </div>
      <div className="card-body stack">
        <p className="small muted" style={{ margin: 0 }}>
          Trvale odstraní kliniku včetně historie, poznámek, zpráv a přihlášení. V audit logu zůstane záznam o smazání.
        </p>
        <button type="button" className="btn btn-danger btn-sm" style={{ alignSelf: 'flex-start' }} onClick={() => setOpen(true)}>
          <Trash2 size={16} aria-hidden="true" /> Smazat kliniku
        </button>
      </div>
      <ConfirmDialog
        open={open}
        title="Smazat kliniku natrvalo?"
        confirmLabel="Smazat natrvalo"
        confirmVariant="danger-solid"
        busy={busy}
        onCancel={() => setOpen(false)}
        onConfirm={() => void confirm()}
      >
        <div className="stack">
          <p style={{ margin: 0 }}>
            Klinika <strong>{clinic.name}</strong> ({clinic.email}) bude odstraněna i se všemi svými údaji, historií stavů,
            poznámkami a zprávami. Tuto akci nelze vrátit.
          </p>
          {error && <Alert kind="error">{error}</Alert>}
        </div>
      </ConfirmDialog>
    </section>
  )
}

function MessagesPanel({ clinicId }: { clinicId: string }) {
  const session = useAdminSession()
  const location = useLocation()
  const { data, setData, error, loading, reload } = useAdminLoader(() => adminApi.messages(clinicId), [clinicId])

  useRealtimeEvents((event) => {
    if (event.type === 'messageReceived' && event.clinicId === clinicId) void reload()
  })

  // Links like /admin/kliniky/:id#zpravy jump straight to the conversation.
  useEffect(() => {
    if (location.hash === '#zpravy' && data) document.getElementById('zpravy')?.scrollIntoView({ block: 'start' })
  }, [location.hash, data])

  return (
    <section className="card" aria-labelledby="messages-title" id="zpravy">
      <div className="card-header">
        <h2 id="messages-title">
          <MessageSquare size={18} aria-hidden="true" style={{ verticalAlign: '-3px', marginRight: 6 }} />
          Zprávy s klinikou
        </h2>
        <span className="small muted">Klinice přijde e-mail, že na ni čeká zpráva.</span>
      </div>
      <div className="card-body">
        <MessageThread
          viewer="administrator"
          messages={data}
          loading={loading}
          error={error}
          onSend={(body) => adminApi.sendMessage(clinicId, body)}
          onSent={(message) => setData([...(data ?? []), message])}
          onUnauthorized={session.markSignedOut}
          emptyText="Napište klinice například, co je potřeba doplnit."
        />
      </div>
    </section>
  )
}

function StatusHistory({ detail }: { detail: AdminClinicDetail }) {
  return (
    <section className="card" aria-labelledby="history-title">
      <div className="card-header">
        <h2 id="history-title">Historie stavů</h2>
      </div>
      <div className="card-body">
        {detail.statusHistory.length === 0 ? (
          <p className="muted" style={{ margin: 0 }}>
            Stav zatím nebyl změněn. Registrace je od {formatDateTime(detail.profile.createdAt)} ve stavu „{statusLabels.pending}“.
          </p>
        ) : (
          <ol className="timeline">
            {detail.statusHistory.map((change) => (
              <li key={change.id}>
                <div>
                  <StatusBadge status={change.fromStatus} /> → <StatusBadge status={change.toStatus} />
                </div>
                <div className="timeline-meta">
                  {formatDateTime(change.changedAt)} · {change.administratorUsername}
                </div>
                {change.reason && <p style={{ margin: '4px 0 0', whiteSpace: 'pre-wrap' }}>{change.reason}</p>}
              </li>
            ))}
          </ol>
        )}
      </div>
    </section>
  )
}

function NotesPanel({ detail, onChange }: { detail: AdminClinicDetail; onChange: (d: AdminClinicDetail) => void }) {
  const toast = useToast()
  const session = useAdminSession()
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (text.trim() === '') {
      setError('Napište text poznámky.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const note = await adminApi.addNote(detail.profile.id, text.trim())
      onChange({ ...detail, notes: [note, ...detail.notes] })
      setText('')
      toast.show('Poznámka byla uložena.')
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        session.markSignedOut()
        return
      }
      setError(e instanceof ApiError ? (e.fieldErrors.text ?? e.message) : 'Poznámku se nepodařilo uložit.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card" aria-labelledby="notes-title">
      <div className="card-header">
        <h2 id="notes-title">Interní poznámky</h2>
        <span className="badge">Jen pro administrátory</span>
      </div>
      <div className="card-body stack">
        <form onSubmit={submit} className="stack" noValidate>
          <Field label="Nová poznámka" error={error ?? undefined}>
            {(a) => <textarea {...a} className="textarea" value={text} onChange={(e) => setText(e.target.value)} maxLength={2000} />}
          </Field>
          <button type="submit" className="btn btn-sm" disabled={busy} style={{ alignSelf: 'flex-start' }}>
            {busy ? <Spinner /> : <NotebookPen size={16} aria-hidden="true" />}
            Přidat poznámku
          </button>
        </form>
        {detail.notes.length === 0 ? (
          <EmptyState title="Bez poznámek" icon={<NotebookPen size={28} aria-hidden="true" />} />
        ) : (
          <div>
            {detail.notes.map((note) => (
              <div key={note.id} className="note">
                <p className="note-text">{note.text}</p>
                <div className="timeline-meta">
                  {note.administratorUsername} · {formatDateTime(note.createdAt)}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  )
}
