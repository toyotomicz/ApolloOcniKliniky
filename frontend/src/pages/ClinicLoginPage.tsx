import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Mail } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { ApiError } from '../api/client'
import { clinicAuthApi } from '../api/endpoints'
import { useClinicSession } from '../auth/sessions'
import { CodeVerification } from '../components/CodeVerification'
import { useToast } from '../components/Toast'
import { Alert, Field, Spinner } from '../components/ui'
import { emailOnlySchema } from '../lib/validation'

export function ClinicLoginPage() {
  const session = useClinicSession()
  const navigate = useNavigate()
  const location = useLocation()
  const toast = useToast()
  const [sentTo, setSentTo] = useState<{ email: string; cooldown: number } | null>(null)
  const [error, setError] = useState<string | null>(null)

  const state = location.state as { from?: string; email?: string } | null
  const from = state?.from ?? '/portal'

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<{ email: string }>({ resolver: zodResolver(emailOnlySchema), defaultValues: { email: state?.email ?? '' } })

  useEffect(() => session.ensureLoaded(), [session])

  if (session.status === 'authenticated') return <Navigate to={from} replace />

  const onSubmit = handleSubmit(async ({ email }) => {
    setError(null)
    try {
      const response = await clinicAuthApi.requestCode(email.trim())
      setSentTo({ email: email.trim(), cooldown: response.resendAfterSeconds })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Kód se nepodařilo odeslat.')
    }
  })

  return (
    <div className="surface-bg">
      <div className="container page">
        <div className="auth-card">
          <div className="card">
            <div className="card-body stack">
              <div>
                <span className="eyebrow">Portál kliniky</span>
                <h1 style={{ fontSize: '1.75rem' }}>Přihlášení</h1>
                <p className="muted" style={{ margin: 0 }}>
                  {sentTo
                    ? 'Zadejte kód, který jsme poslali na váš e-mail.'
                    : 'Zadejte e-mail, se kterým jste kliniku registrovali. Pošleme vám jednorázový kód.'}
                </p>
              </div>

              {!sentTo && (
                <form onSubmit={onSubmit} noValidate className="stack">
                  {error && <Alert kind="error">{error}</Alert>}
                  <Field label="E-mail" error={errors.email?.message}>
                    {(a) => <input {...a} {...register('email')} className="input" type="email" autoComplete="email" autoFocus />}
                  </Field>
                  <button type="submit" className="btn btn-primary btn-block" disabled={isSubmitting}>
                    {isSubmitting ? <Spinner /> : <Mail size={18} aria-hidden="true" />}
                    Poslat přihlašovací kód
                  </button>
                </form>
              )}

              {sentTo && (
                <>
                  <Alert kind="info">
                    Pokud je e-mail <strong>{sentTo.email}</strong> registrován, poslali jsme na něj kód. Zkontrolujte i složku
                    s nevyžádanou poštou.
                  </Alert>
                  <CodeVerification
                    email={sentTo.email}
                    initialCooldown={sentTo.cooldown}
                    onVerified={(profile) => {
                      session.setData(profile)
                      toast.show('Přihlášení proběhlo úspěšně.')
                      navigate(from, { replace: true })
                    }}
                  />
                  <button type="button" className="btn-link small" onClick={() => setSentTo(null)} style={{ alignSelf: 'flex-start' }}>
                    <ArrowLeft size={14} aria-hidden="true" style={{ verticalAlign: '-2px' }} /> Použít jiný e-mail
                  </button>
                </>
              )}
            </div>
          </div>
          <p className="small muted" style={{ textAlign: 'center', marginTop: 16 }}>
            Ještě nemáte registraci? <Link to="/registrace">Zaregistrujte kliniku</Link>
          </p>
        </div>
      </div>
    </div>
  )
}
