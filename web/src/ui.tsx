import { useEffect, type ReactNode } from 'react'
import { ApiError } from './api/client'

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <header className="page-header">
      <div>
        <h1>{title}</h1>
        {subtitle && <p className="muted">{subtitle}</p>}
      </div>
      <div className="row">{actions}</div>
    </header>
  )
}

const TONES: Record<string, string> = {
  Draft: 'neutral', Open: 'info', Released: 'warn', Invoiced: 'ok', Received: 'warn', Billed: 'ok', Cancelled: 'bad', Paid: 'ok',
  Closed: 'warn', Locked: 'bad', Active: 'ok', Inactive: 'neutral', Succeeded: 'ok', Failed: 'bad',
  Overdue: 'bad', Asset: 'info', Liability: 'warn', Equity: 'neutral', Revenue: 'ok', Expense: 'bad',
}

export function Badge({ value, tone }: { value: string; tone?: string }) {
  return <span className={`badge ${tone ?? TONES[value] ?? 'neutral'}`}>{value}</span>
}

export function ErrorNote({ error }: { error: unknown }) {
  if (!error) return null
  const message = error instanceof ApiError ? error.message : error instanceof Error ? error.message : 'Something went wrong.'
  return <div className="note error" role="alert">{message}</div>
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return <p className="muted" role="status">{label}</p>
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="empty">{children}</div>
}

export function Modal({ title, onClose, children, wide }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])
  return (
    <div className="overlay" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}>
      <div className={`modal${wide ? ' wide' : ''}`} role="dialog" aria-modal="true" aria-label={title}>
        <div className="modal-head">
          <h2>{title}</h2>
          <button className="icon-btn" onClick={onClose} aria-label="Close">×</button>
        </div>
        {children}
      </div>
    </div>
  )
}

export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {hint && <small className="muted">{hint}</small>}
    </label>
  )
}

export function Tabs<T extends string>({ value, onChange, tabs }: { value: T; onChange: (tab: T) => void; tabs: { id: T; label: string }[] }) {
  return (
    <div className="tabs" role="tablist">
      {tabs.map((tab) => (
        <button key={tab.id} role="tab" aria-selected={value === tab.id} className={value === tab.id ? 'active' : ''} onClick={() => onChange(tab.id)}>
          {tab.label}
        </button>
      ))}
    </div>
  )
}

export function Kpi({ label, value, hint, tone }: { label: string; value: string; hint?: string; tone?: string }) {
  return (
    <div className={`kpi ${tone ?? ''}`}>
      <span className="muted">{label}</span>
      <strong>{value}</strong>
      {hint && <small className="muted">{hint}</small>}
    </div>
  )
}
