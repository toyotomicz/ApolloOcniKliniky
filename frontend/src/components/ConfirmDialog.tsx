import { useEffect, useRef, type ReactNode } from 'react'
import { Spinner } from './ui'

interface ConfirmDialogProps {
  open: boolean
  title: string
  confirmLabel: string
  confirmVariant?: 'primary' | 'success' | 'danger-solid' | 'dark'
  busy?: boolean
  onConfirm: () => void
  onCancel: () => void
  children?: ReactNode
}

/**
 * Modal confirmation built on the native <dialog> element, which provides focus trapping,
 * Escape to close and an inert background for free.
 */
export function ConfirmDialog({
  open,
  title,
  confirmLabel,
  confirmVariant = 'primary',
  busy = false,
  onConfirm,
  onCancel,
  children,
}: ConfirmDialogProps) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) {
      if (typeof dialog.showModal === 'function') dialog.showModal()
      else dialog.setAttribute('open', '')
    } else if (!open && dialog.open) {
      if (typeof dialog.close === 'function') dialog.close()
      else dialog.removeAttribute('open')
    }
  }, [open])

  return (
    <dialog
      ref={ref}
      className="dialog"
      aria-labelledby="confirm-dialog-title"
      onCancel={(event) => {
        event.preventDefault()
        if (!busy) onCancel()
      }}
    >
      <form
        method="dialog"
        onSubmit={(event) => {
          event.preventDefault()
          if (!busy) onConfirm()
        }}
      >
        <div className="dialog-header">
          <h2 id="confirm-dialog-title">{title}</h2>
        </div>
        <div className="dialog-body">{children}</div>
        <div className="dialog-footer">
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>
            Zrušit
          </button>
          <button type="submit" className={`btn btn-${confirmVariant}`} disabled={busy}>
            {busy && <Spinner />}
            {confirmLabel}
          </button>
        </div>
      </form>
    </dialog>
  )
}
