import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth'
import { ErrorNote } from '../ui'

export function Login() {
  const { login } = useAuth()
  const [userName, setUserName] = useState('admin')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login(userName, password)
    } catch (e) {
      setError(e instanceof Error && e.message.startsWith('Your session') ? new Error('Invalid user name or password.') : e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <form className="card" onSubmit={submit}>
        <div>
          <div className="brand" style={{ padding: 0 }}>GTEK <span>ERP</span></div>
          <p className="muted">Sign in to continue</p>
        </div>
        <label className="field"><span>User name</span>
          <input value={userName} onChange={(e) => setUserName(e.target.value)} autoComplete="username" required />
        </label>
        <label className="field"><span>Password</span>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required autoFocus />
        </label>
        <ErrorNote error={error} />
        <button className="btn primary" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
        {import.meta.env.DEV && <div className="hint">Demo data: <b>admin</b> / <b>Admin#123456</b> (run <code>scripts/dev-setup.sh --seed</code>).</div>}
      </form>
    </div>
  )
}
