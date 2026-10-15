import { MessageSquare, Send } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/client'
import type { Message, MessageSender } from '../api/types'
import { formatDateTime } from '../lib/format'
import { Alert, EmptyState, Spinner } from './ui'

interface MessageThreadProps {
  /** Whose view this is: own messages are aligned right. */
  viewer: MessageSender
  messages: Message[] | null
  loading: boolean
  error: string | null
  onSend: (body: string) => Promise<Message>
  onSent: (message: Message) => void
  /** Called on 401 so the session guard can redirect to the login page. */
  onUnauthorized: () => void
  emptyText: string
}

export const MAX_MESSAGE_LENGTH = 4000

/** Conversation between a clinic and the administrators, with a composer (Ctrl+Enter sends). */
export function MessageThread({ viewer, messages, loading, error, onSend, onSent, onUnauthorized, emptyText }: MessageThreadProps) {
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [sendError, setSendError] = useState<string | null>(null)
  const listRef = useRef<HTMLOListElement>(null)

  useEffect(() => {
    const list = listRef.current
    if (list) list.scrollTop = list.scrollHeight
  }, [messages])

  const send = async () => {
    const body = text.trim()
    if (!body) {
      setSendError('Napište text zprávy.')
      return
    }
    setBusy(true)
    setSendError(null)
    try {
      onSent(await onSend(body))
      setText('')
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        onUnauthorized()
        return
      }
      setSendError(e instanceof ApiError ? (e.fieldErrors.body ?? e.message) : 'Zprávu se nepodařilo odeslat.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="thread">
      {error && <Alert kind="error">{error}</Alert>}
      {loading && !messages && <Spinner label="Načítám zprávy…" />}
      {messages && messages.length === 0 && <EmptyState title="Zatím žádné zprávy" icon={<MessageSquare size={28} aria-hidden="true" />}><p style={{ margin: 0 }}>{emptyText}</p></EmptyState>}
      {messages && messages.length > 0 && (
        <ol className="thread-list" ref={listRef} aria-label="Zprávy">
          {messages.map((m) => {
            const own = m.sender === viewer
            const unread = !own && m.readAt === null
            const author = m.sender === 'clinic' ? (viewer === 'clinic' ? 'Vy' : 'Klinika') : own ? `Vy${m.authorName ? ` (${m.authorName})` : ''}` : (m.authorName ?? 'Administrátor')
            return (
              <li key={m.id} className={`thread-message ${own ? 'is-own' : ''}`}>
                <div className="thread-meta">
                  <span>{author}</span>
                  <time dateTime={m.createdAt}>{formatDateTime(m.createdAt)}</time>
                  {unread && <span className="badge badge-accent">Nová</span>}
                </div>
                <div className="thread-bubble">{m.body}</div>
              </li>
            )
          })}
        </ol>
      )}
      <form
        className="thread-composer"
        onSubmit={(e) => {
          e.preventDefault()
          void send()
        }}
        noValidate
      >
        <label htmlFor={`composer-${viewer}`} className="visually-hidden">
          Nová zpráva
        </label>
        <textarea
          id={`composer-${viewer}`}
          className="textarea"
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
              e.preventDefault()
              void send()
            }
          }}
          maxLength={MAX_MESSAGE_LENGTH}
          placeholder="Napište zprávu… (Ctrl+Enter odešle)"
          aria-invalid={Boolean(sendError)}
          aria-describedby={sendError ? `composer-${viewer}-error` : undefined}
        />
        {sendError && (
          <span id={`composer-${viewer}-error`} className="field-error">
            {sendError}
          </span>
        )}
        <button type="submit" className="btn btn-primary btn-sm" disabled={busy} style={{ alignSelf: 'flex-end' }}>
          {busy ? <Spinner /> : <Send size={16} aria-hidden="true" />}
          Odeslat
        </button>
      </form>
    </div>
  )
}
