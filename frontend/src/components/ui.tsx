import { CircleAlert, CircleCheck, Info, Inbox, TriangleAlert } from 'lucide-react'
import { useId, type ReactNode } from 'react'
import type { ClinicStatus } from '../api/types'
import { statusLabels } from '../lib/format'

export function Spinner({ label }: { label?: string }) {
  return (
    <span role={label ? 'status' : undefined} style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      <span className="spinner" aria-hidden="true" />
      {label && <span>{label}</span>}
    </span>
  )
}

export function LoadingBlock({ label = 'Načítám…' }: { label?: string }) {
  return (
    <div className="loading-block">
      <Spinner label={label} />
    </div>
  )
}

type AlertKind = 'error' | 'success' | 'warning' | 'info'
const alertIcons = { error: CircleAlert, success: CircleCheck, warning: TriangleAlert, info: Info }

export function Alert({ kind = 'info', title, children }: { kind?: AlertKind; title?: string; children?: ReactNode }) {
  const Icon = alertIcons[kind]
  return (
    <div className={`alert alert-${kind}`} role={kind === 'error' ? 'alert' : undefined}>
      <Icon size={18} aria-hidden="true" />
      <div>
        {title && <div className="alert-title">{title}</div>}
        {children}
      </div>
    </div>
  )
}

export function StatusBadge({ status }: { status: ClinicStatus }) {
  return <span className={`badge badge-dot badge-${status}`}>{statusLabels[status]}</span>
}

export function EmptyState({ title, children, icon }: { title: string; children?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="empty-state">
      {icon ?? <Inbox size={40} aria-hidden="true" />}
      <h3>{title}</h3>
      {children}
    </div>
  )
}

interface FieldProps {
  label: string
  error?: string
  hint?: string
  optional?: boolean
  className?: string
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby': string | undefined }) => ReactNode
}

/** Label + control + hint + error, with the accessibility wiring done once. */
export function Field({ label, error, hint, optional, className, children }: FieldProps) {
  const id = useId()
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined
  return (
    <div className={`field ${className ?? ''}`}>
      <label className="field-label" htmlFor={id}>
        {label} {optional && <span className="field-optional">(nepovinné)</span>}
      </label>
      {children({ id, 'aria-invalid': Boolean(error), 'aria-describedby': describedBy })}
      {hint && (
        <span id={hintId} className="field-hint">
          {hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="field-error">
          <CircleAlert size={14} aria-hidden="true" style={{ marginTop: 3 }} />
          {error}
        </span>
      )}
    </div>
  )
}

export function DefinitionList({ items }: { items: [string, ReactNode][] }) {
  return (
    <dl className="dl-grid">
      {items.map(([term, value]) => (
        <div key={term} style={{ display: 'contents' }}>
          <dt>{term}</dt>
          <dd>{value ?? '—'}</dd>
        </div>
      ))}
    </dl>
  )
}
