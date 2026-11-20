import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader, Tabs } from '../ui'
import type { AuditPage, Period, Sequence, User } from '../api/types'

type Tab = 'periods' | 'sequences' | 'users' | 'audit'

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

export function Admin() {
  const { can } = useAuth()
  const tabs = [
    ...(can(P.periodsRead) ? [{ id: 'periods' as Tab, label: 'Accounting periods' }] : []),
    ...(can(P.sequencesRead) ? [{ id: 'sequences' as Tab, label: 'Number sequences' }] : []),
    ...(can(P.usersRead) ? [{ id: 'users' as Tab, label: 'Users' }] : []),
    ...(can(P.auditRead) ? [{ id: 'audit' as Tab, label: 'Audit log' }] : []),
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
    </>
  )
}
