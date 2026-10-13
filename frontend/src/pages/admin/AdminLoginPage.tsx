import { zodResolver } from '@hookform/resolvers/zod'
import { LogIn } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { ApiError } from '../../api/client'
import { adminAuthApi } from '../../api/endpoints'
import { useAdminSession } from '../../auth/sessions'
import { Brand } from '../../components/Brand'
import { Alert, Field, Spinner } from '../../components/ui'
import { adminLoginSchema } from '../../lib/validation'

export function AdminLoginPage() {
  const session = useAdminSession()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const from = (location.state as { from?: string } | null)?.from ?? '/admin'

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<{ username: string; password: string }>({
    resolver: zodResolver(adminLoginSchema),
    defaultValues: { username: '', password: '' },
  })

  useEffect(() => session.ensureLoaded(), [session])

  if (session.status === 'authenticated') return <Navigate to={from} replace />

  const onSubmit = handleSubmit(async ({ username, password }) => {
    setError(null)
    try {
      session.setData(await adminAuthApi.login(username.trim(), password))
      navigate(from, { replace: true })
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
        </div>
        <p className="small muted" style={{ textAlign: 'center', marginTop: 16 }}>
          <Link to="/">Zpět na úvodní stránku</Link>
        </p>
      </main>
    </div>
  )
}
