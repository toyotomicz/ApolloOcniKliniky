import { zodResolver } from '@hookform/resolvers/zod'
import { KeyRound, LogIn, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { ApiError } from '../../api/client'
import { adminAuthApi } from '../../api/endpoints'
import type { AdminMe } from '../../api/types'
import { useAdminSession } from '../../auth/sessions'
import { Brand } from '../../components/Brand'
import { Alert, Field, Spinner } from '../../components/ui'
import { adminLoginSchema } from '../../lib/validation'

export function AdminLoginPage() {
  const session = useAdminSession()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const [twoFactor, setTwoFactor] = useState(false)
  const from = (location.state as { from?: string } | null)?.from ?? '/admin'

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<{ username: string; password: string }>({
    resolver: zodResolver(adminLoginSchema),
    defaultValues: { username: '', password: '' },
  })

  useEffect(() => session.ensureLoaded(), [session])

  if (session.status === 'authenticated') return <Navigate to={from} replace />

  const signedIn = (admin: AdminMe) => {
    session.setData(admin)
    navigate(from, { replace: true })
  }

  const onSubmit = handleSubmit(async ({ username, password }) => {
    setError(null)
    try {
      const result = await adminAuthApi.login(username.trim(), password)
      if ('twoFactorRequired' in result) {
        setTwoFactor(true)
        return
      }
      signedIn(result)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Přihlášení se nezdařilo.')
    }
  })

  return (
    <div className="admin-shell" style={{ display: 'grid', placeItems: 'center', padding: '48px 16px' }}>
      <main id="main" className="auth-card" style={{ width: '100%' }}>
        <div style={{ textAlign: 'center', marginBottom: 24 }}>
          <Brand subtitle="Administrace" />
        </div>
        <div className="card">
          {twoFactor ? (
            <TwoFactorStep
              onSignedIn={signedIn}
              onRestart={(message) => {
                setTwoFactor(false)
                reset({ username: '', password: '' })
                setError(message)
              }}
            />
          ) : (
            <form className="card-body stack" onSubmit={onSubmit} noValidate>
              <div>
                <h1 style={{ fontSize: '1.5rem', marginBottom: 4 }}>Přihlášení administrátora</h1>
                <p className="muted small" style={{ margin: 0 }}>
                  Přístup pouze pro oprávněné pracovníky. Účty administrátorů se nezakládají veřejnou registrací.
                </p>
              </div>
              {error && <Alert kind="error">{error}</Alert>}
              <Field label="Uživatelské jméno" error={errors.username?.message}>
                {(a) => <input {...a} {...register('username')} className="input" autoComplete="username" autoFocus />}
              </Field>
              <Field label="Heslo" error={errors.password?.message}>
                {(a) => <input {...a} {...register('password')} className="input" type="password" autoComplete="current-password" />}
              </Field>
              <button type="submit" className="btn btn-dark btn-block" disabled={isSubmitting}>
                {isSubmitting ? <Spinner /> : <LogIn size={18} aria-hidden="true" />}
                Přihlásit se
              </button>
            </form>
          )}
        </div>
        <p className="small muted" style={{ textAlign: 'center', marginTop: 16 }}>
          <Link to="/">Zpět na úvodní stránku</Link>
        </p>
      </main>
    </div>
  )
}

function TwoFactorStep({ onSignedIn, onRestart }: { onSignedIn: (admin: AdminMe) => void; onRestart: (message: string) => void }) {
  const [useRecovery, setUseRecovery] = useState(false)
  const [value, setValue] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    const input = value.trim()
    if (useRecovery ? input.length < 8 : !/^\d{6}$/.test(input.replace(/\s/g, ''))) {
      setError(useRecovery ? 'Zadejte záložní kód ve tvaru XXXXX-XXXXX.' : 'Zadejte šestimístný kód z aplikace.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      onSignedIn(await adminAuthApi.loginTwoFactor(useRecovery ? { recoveryCode: input } : { code: input.replace(/\s/g, '') }))
    } catch (e) {
      if (e instanceof ApiError && e.status === 401 && e.message.includes('vypršela')) {
        onRestart(e.message)
        return
      }
      setError(e instanceof ApiError ? e.message : 'Ověření se nezdařilo.')
      setValue('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card-body stack" onSubmit={submit} noValidate>
      <div>
        <h1 style={{ fontSize: '1.5rem', marginBottom: 4, display: 'flex', alignItems: 'center', gap: 8 }}>
          <ShieldCheck size={22} aria-hidden="true" /> Dvoufázové ověření
        </h1>
        <p className="muted small" style={{ margin: 0 }}>
          {useRecovery
            ? 'Zadejte jeden z nepoužitých záložních kódů. Každý kód lze použít jen jednou.'
            : 'Otevřete aplikaci Google Authenticator (nebo jinou) a zadejte aktuální šestimístný kód.'}
        </p>
      </div>
      {error && <Alert kind="error">{error}</Alert>}
      <Field label={useRecovery ? 'Záložní kód' : 'Kód z aplikace'}>
        {(a) => (
          <input
            {...a}
            key={useRecovery ? 'recovery' : 'totp'}
            className={`input ${useRecovery ? 'mono' : 'code-input'}`}
            value={value}
            onChange={(e) => setValue(e.target.value)}
            inputMode={useRecovery ? 'text' : 'numeric'}
            autoComplete="one-time-code"
            maxLength={useRecovery ? 16 : 7}
            placeholder={useRecovery ? 'XXXXX-XXXXX' : '123456'}
            autoFocus
          />
        )}
      </Field>
      <button type="submit" className="btn btn-dark btn-block" disabled={busy}>
        {busy ? <Spinner /> : <KeyRound size={18} aria-hidden="true" />}
        Ověřit a přihlásit
      </button>
      <button
        type="button"
        className="btn-link small"
        style={{ alignSelf: 'center' }}
        onClick={() => {
          setUseRecovery((v) => !v)
          setValue('')
          setError(null)
        }}
      >
        {useRecovery ? 'Použít kód z aplikace' : 'Nemám telefon – použít záložní kód'}
      </button>
    </form>
  )
}
