import { zodResolver } from '@hookform/resolvers/zod'
import { RotateCw, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { ApiError } from '../api/client'
import { clinicAuthApi } from '../api/endpoints'
import type { ClinicProfile } from '../api/types'
import { codeSchema } from '../lib/validation'
import { Alert, Field, Spinner } from './ui'

interface CodeVerificationProps {
  email: string
  /** Seconds before "send again" is enabled (matches the server-side cooldown). */
  initialCooldown?: number
  /** Set when the initial email could not be delivered, so the user is prompted to retry. */
  initialDeliveryFailed?: boolean
  onVerified: (profile: ClinicProfile) => void
}

/** Second step of the passwordless flow: enter the 6-digit code from the email. */
export function CodeVerification({ email, initialCooldown = 60, initialDeliveryFailed = false, onVerified }: CodeVerificationProps) {
  const [cooldown, setCooldown] = useState(initialDeliveryFailed ? 0 : initialCooldown)
  const [resendState, setResendState] = useState<'idle' | 'sending' | 'sent' | 'failed'>(initialDeliveryFailed ? 'failed' : 'idle')
  const [resendMessage, setResendMessage] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setFocus,
    formState: { errors, isSubmitting },
  } = useForm<{ code: string }>({ resolver: zodResolver(codeSchema), defaultValues: { code: '' } })

  useEffect(() => {
    setFocus('code')
  }, [setFocus])

  useEffect(() => {
    if (cooldown <= 0) return
    const timer = window.setTimeout(() => setCooldown((c) => c - 1), 1000)
    return () => window.clearTimeout(timer)
  }, [cooldown])

  const onSubmit = handleSubmit(async ({ code }) => {
    setFormError(null)
    try {
      const profile = await clinicAuthApi.verifyCode(email, code.trim())
      onVerified(profile)
    } catch (error) {
      setFormError(error instanceof ApiError ? error.message : 'Ověření se nezdařilo.')
    }
  })

  const resend = async () => {
    setResendState('sending')
    setResendMessage(null)
    try {
      const response = await clinicAuthApi.requestCode(email)
      setResendState('sent')
      setCooldown(response.resendAfterSeconds)
      setResendMessage('Pokud byl kód odeslán, najdete jej ve schránce. Platí jen nejnovější kód.')
    } catch (error) {
      setResendState('failed')
      setResendMessage(error instanceof ApiError ? error.message : 'Kód se nepodařilo odeslat.')
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate className="stack">
      {resendState === 'failed' && (
        <Alert kind="warning" title="E-mail s kódem se nepodařilo odeslat">
          <p>{resendMessage ?? 'Zkuste prosím kód odeslat znovu.'}</p>
        </Alert>
      )}
      {resendState === 'sent' && resendMessage && <Alert kind="info">{resendMessage}</Alert>}
      {formError && <Alert kind="error">{formError}</Alert>}

      <Field label="Jednorázový kód" error={errors.code?.message} hint={`Poslali jsme jej na ${email}. Kód platí 10 minut.`}>
        {(a11y) => (
          <input
            {...a11y}
            {...register('code')}
            className="input input-code"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            placeholder="••••••"
          />
        )}
      </Field>

      <div className="inline-actions">
        <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
          {isSubmitting ? <Spinner /> : <ShieldCheck size={18} aria-hidden="true" />}
          Ověřit kód
        </button>
        <button type="button" className="btn-link" onClick={resend} disabled={cooldown > 0 || resendState === 'sending'}>
          <RotateCw size={14} aria-hidden="true" style={{ verticalAlign: '-2px', marginRight: 4 }} />
          {resendState === 'sending'
            ? 'Odesílám…'
            : cooldown > 0
              ? `Poslat nový kód (${cooldown} s)`
              : 'Poslat nový kód'}
        </button>
      </div>
    </form>
  )
}
