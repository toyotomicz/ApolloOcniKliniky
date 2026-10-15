import { Copy, Download, KeyRound, ShieldCheck, ShieldOff, Smartphone } from 'lucide-react'
import { useState } from 'react'
import { ApiError } from '../../api/client'
import { adminSecurityApi } from '../../api/endpoints'
import type { TwoFactorSetup } from '../../api/types'
import { useAdminSession } from '../../auth/sessions'
import { useToast } from '../../components/Toast'
import { Alert, DefinitionList, Field, LoadingBlock, Spinner } from '../../components/ui'
import { formatDateTime } from '../../lib/format'
import { useAdminLoader } from './useAdminLoader'

export function AdminSecurityPage() {
  const session = useAdminSession()
  const { data: status, error, loading, reload } = useAdminLoader(() => adminSecurityApi.status(), [])
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)

  const changed = async (codes?: string[]) => {
    setRecoveryCodes(codes ?? null)
    await reload()
    await session.refresh()
  }

  return (
    <div className="container page page-narrow">
      <div className="page-header">
        <div>
          <h1>Zabezpečení účtu</h1>
          <p>Dvoufázové ověření chrání administraci i v případě, že by někdo získal vaše heslo.</p>
        </div>
      </div>

      {error && <Alert kind="error">{error}</Alert>}
      {loading && !status && <LoadingBlock />}

      {recoveryCodes && <RecoveryCodes codes={recoveryCodes} username={session.data?.username ?? 'admin'} onDone={() => setRecoveryCodes(null)} />}

      {status && !recoveryCodes && (
        <section className="card" aria-labelledby="tfa-title">
          <div className="card-header">
            <h2 id="tfa-title">Dvoufázové ověření (Google Authenticator)</h2>
            <span className={`badge ${status.enabled ? 'badge-approved' : ''}`}>{status.enabled ? 'Zapnuto' : 'Vypnuto'}</span>
          </div>
          <div className="card-body stack">
            {status.enabled ? (
              <>
                <DefinitionList
                  items={[
                    ['Zapnuto od', formatDateTime(status.enabledAt)],
                    ['Nepoužité záložní kódy', String(status.recoveryCodesRemaining)],
                  ]}
                />
                {status.recoveryCodesRemaining <= 3 && (
                  <Alert kind="warning">Dochází vám záložní kódy. Vygenerujte si nové a uložte je na bezpečné místo.</Alert>
                )}
                <EnabledActions onChanged={changed} />
              </>
            ) : (
              <SetupFlow onEnabled={(codes) => void changed(codes)} />
            )}
          </div>
        </section>
      )}
    </div>
  )
}

function SetupFlow({ onEnabled }: { onEnabled: (codes: string[]) => void }) {
  const session = useAdminSession()
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null)
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handle = (e: unknown, fallback: string) => {
    if (e instanceof ApiError && e.status === 401) session.markSignedOut()
    else setError(e instanceof ApiError ? (e.fieldErrors.code ?? e.message) : fallback)
  }

  const start = async () => {
    setBusy(true)
    setError(null)
    try {
      setSetup(await adminSecurityApi.setup())
    } catch (e) {
      handle(e, 'Nastavení se nepodařilo spustit.')
    } finally {
      setBusy(false)
    }
  }

  const confirm = async (event: React.FormEvent) => {
    event.preventDefault()
    const normalized = code.replace(/\s/g, '')
    if (!/^\d{6}$/.test(normalized)) {
      setError('Zadejte šestimístný kód, který aplikace právě zobrazuje.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      onEnabled((await adminSecurityApi.enable(normalized)).recoveryCodes)
    } catch (e) {
      handle(e, 'Dvoufázové ověření se nepodařilo zapnout.')
    } finally {
      setBusy(false)
    }
  }

  if (!setup) {
    return (
      <>
        <p style={{ margin: 0 }}>
          Po zapnutí budete při přihlášení kromě hesla zadávat i šestimístný kód z aplikace v telefonu (Google Authenticator,
          Microsoft Authenticator, 1Password a další).
        </p>
        {error && <Alert kind="error">{error}</Alert>}
        <button type="button" className="btn btn-primary" style={{ alignSelf: 'flex-start' }} onClick={() => void start()} disabled={busy}>
          {busy ? <Spinner /> : <Smartphone size={18} aria-hidden="true" />}
          Zapnout dvoufázové ověření
        </button>
      </>
    )
  }

  return (
    <form className="stack" onSubmit={confirm} noValidate>
      <ol className="setup-steps">
        <li>
          <strong>Naskenujte QR kód</strong> v aplikaci Google Authenticator (tlačítko + → Naskenovat QR kód).
          <div className="qr-box">
            <img src={setup.qrCodeDataUri} width={196} height={196} alt="QR kód pro přidání účtu do aplikace pro ověřování" />
            <div className="small muted">
              Nejde naskenovat? Zadejte klíč ručně:
              <div className="mono secret-key">{setup.secret}</div>
            </div>
          </div>
        </li>
        <li>
          <strong>Zadejte kód</strong>, který aplikace zobrazí, a nastavení potvrďte.
        </li>
      </ol>
      {error && <Alert kind="error">{error}</Alert>}
      <Field label="Kód z aplikace">
        {(a) => (
          <input
            {...a}
            className="input code-input"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={7}
            placeholder="123456"
            style={{ maxWidth: 220 }}
          />
        )}
      </Field>
      <div className="inline-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? <Spinner /> : <ShieldCheck size={18} aria-hidden="true" />}
          Ověřit a zapnout
        </button>
        <button type="button" className="btn" onClick={() => setSetup(null)} disabled={busy}>
          Zrušit
        </button>
      </div>
    </form>
  )
}

function EnabledActions({ onChanged }: { onChanged: (codes?: string[]) => Promise<void> }) {
  const session = useAdminSession()
  const toast = useToast()
  const [mode, setMode] = useState<'renew' | 'disable' | null>(null)
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (!code.trim()) {
      setError('Zadejte kód.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      if (mode === 'renew') {
        const result = await adminSecurityApi.renewRecoveryCodes(code.replace(/\s/g, ''))
        await onChanged(result.recoveryCodes)
      } else {
        await adminSecurityApi.disable(code.trim())
        toast.show('Dvoufázové ověření bylo vypnuto.', 'info')
        await onChanged()
      }
      setMode(null)
      setCode('')
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) session.markSignedOut()
      else setError(e instanceof ApiError ? (e.fieldErrors.code ?? e.message) : 'Akce se nezdařila.')
    } finally {
      setBusy(false)
    }
  }

  if (!mode) {
    return (
      <div className="inline-actions">
        <button type="button" className="btn" onClick={() => setMode('renew')}>
          <KeyRound size={16} aria-hidden="true" /> Nové záložní kódy
        </button>
        <button type="button" className="btn btn-danger" onClick={() => setMode('disable')}>
          <ShieldOff size={16} aria-hidden="true" /> Vypnout dvoufázové ověření
        </button>
      </div>
    )
  }

  return (
    <form className="stack" onSubmit={submit} noValidate>
      <p style={{ margin: 0 }}>
        {mode === 'renew'
          ? 'Pro vytvoření nových záložních kódů zadejte aktuální kód z aplikace. Dosavadní záložní kódy přestanou platit.'
          : 'Pro vypnutí zadejte aktuální kód z aplikace nebo nepoužitý záložní kód.'}
      </p>
      {error && <Alert kind="error">{error}</Alert>}
      <Field label={mode === 'renew' ? 'Kód z aplikace' : 'Kód z aplikace nebo záložní kód'}>
        {(a) => (
          <input {...a} className="input mono" value={code} onChange={(e) => setCode(e.target.value)} autoComplete="one-time-code" maxLength={16} style={{ maxWidth: 260 }} autoFocus />
        )}
      </Field>
      <div className="inline-actions">
        <button type="submit" className={`btn ${mode === 'disable' ? 'btn-danger-solid' : 'btn-primary'}`} disabled={busy}>
          {busy && <Spinner />}
          {mode === 'renew' ? 'Vytvořit nové kódy' : 'Vypnout'}
        </button>
        <button type="button" className="btn" onClick={() => { setMode(null); setError(null); setCode('') }} disabled={busy}>
          Zrušit
        </button>
      </div>
    </form>
  )
}

function RecoveryCodes({ codes, username, onDone }: { codes: string[]; username: string; onDone: () => void }) {
  const toast = useToast()
  const text = `Záložní kódy pro administraci (${username})\nKaždý kód lze použít jen jednou.\n\n${codes.join('\n')}\n`

  const download = () => {
    const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'zalozni-kody.txt'
    link.click()
    URL.revokeObjectURL(url)
  }

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      toast.show('Záložní kódy jsou ve schránce.')
    } catch {
      toast.show('Kopírování se nepodařilo, kódy si prosím opište.', 'error')
    }
  }

  return (
    <section className="card" aria-labelledby="codes-title">
      <div className="card-header">
        <h2 id="codes-title">Uložte si záložní kódy</h2>
      </div>
      <div className="card-body stack">
        <Alert kind="warning" title="Kódy uvidíte jen teď">
          Pokud ztratíte telefon, přihlásíte se jedním z těchto kódů. Uložte je mimo počítač (např. do správce hesel). Každý kód
          lze použít jen jednou.
        </Alert>
        <ul className="recovery-codes" aria-label="Záložní kódy">
          {codes.map((code) => (
            <li key={code} className="mono">
              {code}
            </li>
          ))}
        </ul>
        <div className="inline-actions">
          <button type="button" className="btn" onClick={download}>
            <Download size={16} aria-hidden="true" /> Stáhnout (.txt)
          </button>
          <button type="button" className="btn" onClick={() => void copy()}>
            <Copy size={16} aria-hidden="true" /> Kopírovat
          </button>
          <button type="button" className="btn btn-primary" onClick={onDone}>
            Mám je uložené
          </button>
        </div>
      </div>
    </section>
  )
}
