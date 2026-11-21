import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader, Tabs } from '../ui'
import type { AuditPage, CategoryRetention, EmailStatus, Period, RetentionSettings, Sequence, User } from '../api/types'

type Tab = 'periods' | 'sequences' | 'users' | 'audit' | 'notifications'

function PeriodForm({ onClose }: { onClose: () => void }) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const save = useApiMutation<void>(() => ({ path: '/accounting-periods', body: { code, name, startDate: start, endDate: end } }), onClose)
  return (
    <Modal title="New accounting period" onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Code"><input value={code} onChange={(e) => setCode(e.target.value)} placeholder="2027-04" required autoFocus /></Field>
          <Field label="Name"><input value={name} onChange={(e) => setName(e.target.value)} required /></Field>
          <Field label="Start"><input type="date" value={start} onChange={(e) => setStart(e.target.value)} required /></Field>
          <Field label="End"><input type="date" value={end} onChange={(e) => setEnd(e.target.value)} required /></Field>
        </div>
        <ErrorNote error={save.error} />
        <div className="modal-actions"><button type="button" className="btn" onClick={onClose}>Cancel</button><button className="btn primary" disabled={save.isPending}>Save</button></div>
      </form>
    </Modal>
  )
}

function PeriodsTab() {
  const { can } = useAuth()
  const periods = useApiQuery<Period[]>('/accounting-periods')
  const [adding, setAdding] = useState(false)
  const change = useApiMutation<{ id: string; action: 'close' | 'reopen' | 'lock' }>(({ id, action }) => ({ path: `/accounting-periods/${id}/${action}` }))
  const manage = can(P.periodsManage)
  return (
    <>
      {manage && <div style={{ marginBottom: 12 }}><button className="btn primary" onClick={() => setAdding(true)}>New period</button></div>}
      <p className="hint" style={{ marginBottom: 12 }}>Postings are rejected in closed or locked periods and in dates no period covers.</p>
      <ErrorNote error={periods.error ?? change.error} />
      {periods.isLoading ? <Loading /> : (
        <div className="card flush scroll-x"><table className="data">
          <thead><tr><th>Code</th><th>Name</th><th>From</th><th>To</th><th>Status</th><th /></tr></thead>
          <tbody>{(periods.data ?? []).map((p) => (
            <tr key={p.id}><td>{p.code}</td><td>{p.name}</td><td>{formatDate(p.startDate)}</td><td>{formatDate(p.endDate)}</td><td><Badge value={p.status} /></td>
              <td>{manage && <div className="row" style={{ justifyContent: 'flex-end' }}>
                {p.status === 'Open' && <button className="btn small" onClick={() => change.mutate({ id: p.id, action: 'close' })}>Close</button>}
                {p.status === 'Closed' && <button className="btn small" onClick={() => change.mutate({ id: p.id, action: 'reopen' })}>Reopen</button>}
                {p.status === 'Closed' && <button className="btn small danger" onClick={() => { if (confirm('Locking is permanent for normal users. Continue?')) change.mutate({ id: p.id, action: 'lock' }) }}>Lock</button>}
              </div>}</td></tr>
          ))}</tbody>
        </table></div>
      )}
      {adding && <PeriodForm onClose={() => setAdding(false)} />}
    </>
  )
}

function SequencesTab() {
  const sequences = useApiQuery<Sequence[]>('/number-sequences')
  return sequences.isLoading ? <Loading /> : (
    <>
      <ErrorNote error={sequences.error} />
      <div className="card flush scroll-x">
        {(sequences.data ?? []).length === 0 ? <Empty>No sequences yet. They are created on first use.</Empty> : (
          <table className="data">
            <thead><tr><th>Code</th><th>Name</th><th>Format</th><th className="num">Next value</th><th>Status</th></tr></thead>
            <tbody>{sequences.data!.map((s) => (
              <tr key={s.id}><td>{s.code}</td><td>{s.name}</td><td>{s.prefix}{'0'.repeat(Math.max(s.padding - 1, 0))}1{s.suffix ?? ''}</td><td className="num">{s.nextValue}</td><td><Badge value={s.status} /></td></tr>
            ))}</tbody>
          </table>
        )}
      </div>
    </>
  )
}

function UsersTab() {
  const users = useApiQuery<User[]>('/users')
  return users.isLoading ? <Loading /> : (
    <>
      <ErrorNote error={users.error} />
      <div className="card flush scroll-x"><table className="data">
        <thead><tr><th>User name</th><th>Email</th><th>Status</th><th>Created</th></tr></thead>
        <tbody>{(users.data ?? []).map((u) => (
          <tr key={u.id}><td>{u.userName}</td><td>{u.email}</td><td><Badge value={u.status} /></td><td>{formatDate(u.createdAt)}</td></tr>
        ))}</tbody>
      </table></div>
    </>
  )
}

function AuditTab() {
  const [page, setPage] = useState(1)
  const [category, setCategory] = useState('')
  const query = `/audit?page=${page}&pageSize=25${category ? `&category=${encodeURIComponent(category)}` : ''}`
  const audit = useApiQuery<AuditPage>(query)
  const pages = audit.data ? Math.max(1, Math.ceil(audit.data.totalCount / audit.data.pageSize)) : 1
  return (
    <>
      <div className="row" style={{ marginBottom: 12 }}>
        <select value={category} onChange={(e) => { setCategory(e.target.value); setPage(1) }} aria-label="Category" style={{ width: 'auto' }}>
          <option value="">All categories</option>
          {['Finance', 'Inventory', 'Sales', 'CompanyManagement', 'UserManagement', 'RoleManagement'].map((c) => <option key={c}>{c}</option>)}
        </select>
        <span className="muted">{audit.data?.totalCount ?? 0} entries</span>
      </div>
      <ErrorNote error={audit.error} />
      {audit.isLoading ? <Loading /> : (
        <div className="card flush scroll-x"><table className="data">
          <thead><tr><th>When</th><th>Who</th><th>Action</th><th>Entity</th><th>Outcome</th></tr></thead>
          <tbody>{(audit.data?.items ?? []).map((e) => (
            <tr key={e.id}><td>{new Date(e.occurredAt).toLocaleString('en-GB')}</td><td>{e.actorUserName ?? 'system'}</td><td>{e.action}</td>
              <td>{e.entityType}{e.entityDisplay ? ` · ${e.entityDisplay}` : ''}</td><td><Badge value={e.outcome} /></td></tr>
          ))}</tbody>
        </table></div>
      )}
      <div className="row" style={{ marginTop: 12, justifyContent: 'flex-end' }}>
        <button className="btn small" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
        <span className="muted">Page {page} of {pages}</span>
        <button className="btn small" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
      </div>
    </>
  )
}

/** Whether notification emails are being delivered, and what is waiting or has failed. */
function EmailCard() {
  const status = useApiQuery<EmailStatus>('/notification-settings/email')
  const s = status.data
  if (!s) return status.error ? <ErrorNote error={status.error} /> : null
  return (
    <section className="card stack" style={{ maxWidth: 640, marginTop: 16 }}>
      <h3 style={{ margin: 0 }}>Email delivery</h3>
      {!s.enabled ? (
        <p className="muted" style={{ margin: 0 }}>Email is switched off for this installation, so approval requests are shown in the app only. An administrator turns it on in the API's <code>Email</code> configuration.</p>
      ) : (
        <>
          <p style={{ margin: 0 }}>Sending through <strong>{s.host}:{s.port}</strong> as <strong>{s.fromAddress}</strong>.</p>
          <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
            <Badge value={`${s.pending} waiting`} tone={s.pending > 0 ? 'warn' : 'neutral'} />
            <Badge value={`${s.sentLast24Hours} sent in 24 h`} tone="ok" />
            <Badge value={`${s.failed} failed`} tone={s.failed > 0 ? 'bad' : 'neutral'} />
          </div>
          {s.lastSentAt && <p className="muted" style={{ margin: 0 }}>Last sent {formatDate(s.lastSentAt)}.</p>}
          {s.oldestPendingAt && s.pending > 0 && <p className="muted" style={{ margin: 0 }}>Oldest waiting since {formatDate(s.oldestPendingAt)}; undelivered messages are retried with a growing delay.</p>}
          {s.lastError && <div className="note error">Last error: {s.lastError}</div>}
        </>
      )}
    </section>
  )
}

/** One category's own retention, or blank to inherit the company's value. */
function CategoryRow({ row, manage, onSaved }: { row: CategoryRetention; manage: boolean; onSaved: () => void }) {
  const str = (n: number | null) => (n == null ? '' : String(n))
  const num = (v: string) => (v.trim() === '' ? null : Number(v))
  const [read, setRead] = useState<string | null>(null)
  const [unread, setUnread] = useState<string | null>(null)
  const readValue = read ?? str(row.readDaysOverride)
  const unreadValue = unread ?? str(row.unreadDaysOverride)
  const save = useApiMutation<{ readDays: number | null; unreadDays: number | null }>((body) => ({
    path: `/notification-settings/retention/categories/${encodeURIComponent(row.category)}`, method: 'PUT', body,
  }), () => { setRead(null); setUnread(null); onSaved() })
  const dirty = readValue !== str(row.readDaysOverride) || unreadValue !== str(row.unreadDaysOverride)
  const own = row.readDaysOverride != null || row.unreadDaysOverride != null
  return (
    <>
      <tr>
        <td><strong>{row.category}</strong>{own && <span className="muted"> · own rule</span>}</td>
        <td><input type="number" min={1} max={3650} aria-label={`${row.category} read days`} value={readValue} placeholder="inherit" disabled={!manage} style={{ width: 90 }} onChange={(e) => setRead(e.target.value)} /></td>
        <td><input type="number" min={0} max={3650} aria-label={`${row.category} unread days`} value={unreadValue} placeholder="inherit" disabled={!manage} style={{ width: 90 }} onChange={(e) => setUnread(e.target.value)} /></td>
        <td style={{ whiteSpace: 'nowrap' }}>{row.effectiveReadDays} / {row.effectiveUnreadDays === 0 ? 'kept' : row.effectiveUnreadDays}</td>
        <td>{manage && (
          <span style={{ display: 'flex', gap: 6 }}>
            <button type="button" className="btn primary" disabled={!dirty || save.isPending} onClick={() => save.mutate({ readDays: num(readValue), unreadDays: num(unreadValue) })}>Save</button>
            <button type="button" className="btn" disabled={!own || save.isPending} onClick={() => save.mutate({ readDays: null, unreadDays: null })}>Inherit</button>
          </span>
        )}</td>
      </tr>
      {save.error != null && <tr><td colSpan={5}><ErrorNote error={save.error} /></td></tr>}
    </>
  )
}

/** How long this company keeps notifications: its own values, or blank to use the system default. */
function NotificationsTab() {
  const { can, company } = useAuth()
  const settings = useApiQuery<RetentionSettings>('/notification-settings/retention')
  const [read, setRead] = useState<string | null>(null)
  const [unread, setUnread] = useState<string | null>(null)
  const save = useApiMutation<void>(() => ({
    path: '/notification-settings/retention', method: 'PUT',
    body: { readDays: num(read ?? str(settings.data?.readDaysOverride ?? null)), unreadDays: num(unread ?? str(settings.data?.unreadDaysOverride ?? null)) },
  }), () => { setRead(null); setUnread(null) })
  const str = (n: number | null) => (n == null ? '' : String(n))
  const num = (v: string) => (v.trim() === '' ? null : Number(v))
  if (settings.isLoading) return <Loading />
  const s = settings.data
  if (!s) return <ErrorNote error={settings.error} />
  const readValue = read ?? str(s.readDaysOverride)
  const unreadValue = unread ?? str(s.unreadDaysOverride)
  const manage = can(P.retentionManage)
  const dirty = readValue !== str(s.readDaysOverride) || unreadValue !== str(s.unreadDaysOverride)
  return (
    <div className="stack" style={{ maxWidth: 640 }}>
    <form className="card stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
      <h3 style={{ margin: 0 }}>Notification retention{company ? ` — ${company.name}` : ''}</h3>
      <p className="muted" style={{ margin: 0 }}>Notifications are deleted automatically. Leave a field blank to use the system default.</p>
      {!s.enabled && <div className="note error">The scheduled purge is switched off for this installation, so nothing is deleted regardless of these settings.</div>}
      <div className="form-grid">
        <Field label="Keep read messages for (days)" hint={`Counted from when the message was read. System default: ${s.systemReadDays} days.`}>
          <input type="number" min={1} max={3650} value={readValue} placeholder={`${s.systemReadDays} (default)`} disabled={!manage} onChange={(e) => setRead(e.target.value)} />
        </Field>
        <Field label="Keep unread messages for (days)" hint={`Counted from when it was sent; 0 keeps unread messages forever. System default: ${s.systemUnreadDays} days.`}>
          <input type="number" min={0} max={3650} value={unreadValue} placeholder={`${s.systemUnreadDays} (default)`} disabled={!manage} onChange={(e) => setUnread(e.target.value)} />
        </Field>
      </div>
      <p>In effect now: read messages are removed <strong>{s.effectiveReadDays} days</strong> after they are read; unread ones {s.effectiveUnreadDays === 0 ? <strong>are kept</strong> : <>after <strong>{s.effectiveUnreadDays} days</strong></>}.
        {s.updatedAt && <span className="muted"> Last changed {formatDate(s.updatedAt)}{s.updatedBy ? ` by ${s.updatedBy}` : ''}.</span>}</p>
      <ErrorNote error={save.error} />
      {manage && (
        <div className="modal-actions" style={{ justifyContent: 'flex-start' }}>
          <button className="btn primary" disabled={!dirty || save.isPending}>{save.isPending ? 'Saving…' : 'Save'}</button>
          <button type="button" className="btn" disabled={dirty === false && s.readDaysOverride == null && s.unreadDaysOverride == null}
            onClick={() => { setRead(''); setUnread('') }}>Use system defaults</button>
        </div>
      )}
    </form>
    <section className="card stack">
      <h3 style={{ margin: 0 }}>By category</h3>
      <p className="muted" style={{ margin: 0 }}>A category can keep its messages for longer or shorter than the rest of the company. Blank inherits the company value above.</p>
      <table className="data">
        <thead><tr><th>Category</th><th>Read days</th><th>Unread days</th><th title="read / unread days">In effect</th><th /></tr></thead>
        <tbody>{s.categories.map((c) => <CategoryRow key={c.category} row={c} manage={manage} onSaved={() => settings.refetch()} />)}</tbody>
      </table>
    </section>
    </div>
  )
}

export function Admin() {
  const { can } = useAuth()
  const tabs = [
    ...(can(P.periodsRead) ? [{ id: 'periods' as Tab, label: 'Accounting periods' }] : []),
    ...(can(P.sequencesRead) ? [{ id: 'sequences' as Tab, label: 'Number sequences' }] : []),
    ...(can(P.usersRead) ? [{ id: 'users' as Tab, label: 'Users' }] : []),
    ...(can(P.auditRead) ? [{ id: 'audit' as Tab, label: 'Audit log' }] : []),
    ...(can(P.retentionRead) ? [{ id: 'notifications' as Tab, label: 'Notifications' }] : []),
  ]
  const [tab, setTab] = useState<Tab | undefined>(tabs[0]?.id)
  return (
    <>
      <PageHeader title="Administration" />
      <Tabs value={tab ?? 'periods'} onChange={setTab} tabs={tabs} />
      {tab === 'periods' && <PeriodsTab />}
      {tab === 'sequences' && <SequencesTab />}
      {tab === 'users' && <UsersTab />}
      {tab === 'audit' && <AuditTab />}
      {tab === 'notifications' && <><NotificationsTab /><EmailCard /></>}
    </>
  )
}
