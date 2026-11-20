import { useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, today } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader, Tabs } from '../ui'
import type { Account, AccountMapping, AccountType, Journal, TrialBalance } from '../api/types'

type Tab = 'tb' | 'accounts' | 'journals' | 'mapping'
const TYPES: AccountType[] = ['Asset', 'Liability', 'Equity', 'Revenue', 'Expense']

function TrialBalanceTab() {
  const [asOf, setAsOf] = useState('')
  const tb = useApiQuery<TrialBalance>(`/finance/trial-balance${asOf ? `?asOf=${asOf}` : ''}`)
  return (
    <>
      <div className="row" style={{ marginBottom: 12 }}>
        <Field label="As of date"><input type="date" value={asOf} onChange={(e) => setAsOf(e.target.value)} /></Field>
        {tb.data && <Badge value={tb.data.isBalanced ? 'Balanced' : 'OUT OF BALANCE'} tone={tb.data.isBalanced ? 'ok' : 'bad'} />}
      </div>
      <ErrorNote error={tb.error} />
      {tb.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          {(tb.data?.rows ?? []).length === 0 ? <Empty>No postings yet.</Empty> : (
            <table className="data">
              <thead><tr><th>Account</th><th>Type</th><th className="num">Debit</th><th className="num">Credit</th></tr></thead>
              <tbody>{tb.data!.rows.map((r) => (
                <tr key={r.accountId}><td>{r.accountCode} — {r.accountName}</td><td><Badge value={r.type} /></td>
                  <td className="num">{r.debit ? formatMoney(r.debit) : ''}</td><td className="num">{r.credit ? formatMoney(r.credit) : ''}</td></tr>
              ))}</tbody>
              <tfoot><tr><td colSpan={2}>Total</td><td className="num">{formatMoney(tb.data!.totalDebit)}</td><td className="num">{formatMoney(tb.data!.totalCredit)}</td></tr></tfoot>
            </table>
          )}
        </div>
      )}
    </>
  )
}

function AccountForm({ onClose }: { onClose: () => void }) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [type, setType] = useState<AccountType>('Expense')
  const save = useApiMutation<void>(() => ({ path: '/finance/accounts', body: { code, name, type } }), onClose)
  return (
    <Modal title="New account" onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Code"><input value={code} onChange={(e) => setCode(e.target.value)} required autoFocus /></Field>
          <Field label="Name"><input value={name} onChange={(e) => setName(e.target.value)} required /></Field>
          <Field label="Type"><select value={type} onChange={(e) => setType(e.target.value as AccountType)}>{TYPES.map((t) => <option key={t}>{t}</option>)}</select></Field>
        </div>
        <ErrorNote error={save.error} />
        <div className="modal-actions"><button type="button" className="btn" onClick={onClose}>Cancel</button><button className="btn primary" disabled={save.isPending}>Save</button></div>
      </form>
    </Modal>
  )
}

function AccountsTab() {
  const { can } = useAuth()
  const accounts = useApiQuery<Account[]>('/finance/accounts')
  const [adding, setAdding] = useState(false)
  const toggle = useApiMutation<Account>((a) => ({ path: `/finance/accounts/${a.id}/${a.isActive ? 'deactivate' : 'activate'}` }))
  return (
    <>
      {can(P.accountsManage) && <div style={{ marginBottom: 12 }}><button className="btn primary" onClick={() => setAdding(true)}>New account</button></div>}
      <ErrorNote error={accounts.error ?? toggle.error} />
      {accounts.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          <table className="data">
            <thead><tr><th>Code</th><th>Name</th><th>Type</th><th>Status</th><th /></tr></thead>
            <tbody>{(accounts.data ?? []).map((a) => (
              <tr key={a.id}><td>{a.code}</td><td>{a.name}</td><td><Badge value={a.type} /></td><td><Badge value={a.isActive ? 'Active' : 'Inactive'} /></td>
                <td className="right">{can(P.accountsManage) && <button className="btn small" onClick={() => toggle.mutate(a)}>{a.isActive ? 'Deactivate' : 'Activate'}</button>}</td></tr>
            ))}</tbody>
          </table>
        </div>
      )}
      {adding && <AccountForm onClose={() => setAdding(false)} />}
    </>
  )
}

function MappingRow({ row, accounts, canManage }: { row: AccountMapping; accounts: Account[]; canManage: boolean }) {
  const options = accounts.filter((a) => a.isActive && row.allowedTypes.includes(a.type))
  const [selected, setSelected] = useState(row.accountId ?? '')
  const save = useApiMutation<void>(() => ({ path: `/finance/account-mappings/${row.role}`, method: 'PUT', body: { accountId: selected } }))
  const reset = useApiMutation<void>(() => ({ path: `/finance/account-mappings/${row.role}`, method: 'DELETE' }))
  const changed = !!selected && selected !== row.accountId
  const current = accounts.find((a) => a.id === row.accountId)

  return (
    <tr>
      <td>{row.label}<div className="muted">{row.description}</div></td>
      <td style={{ minWidth: 260 }}>
        {canManage ? (
          <select value={selected} onChange={(e) => setSelected(e.target.value)} aria-label={`Account for ${row.label}`}>
            {!row.accountId && <option value="">— not set —</option>}
            {options.map((a) => <option key={a.id} value={a.id}>{a.code} — {a.name}</option>)}
            {current && !options.some((a) => a.id === current.id) && <option value={current.id}>{current.code} — {current.name}</option>}
          </select>
        ) : <span>{row.accountCode ? `${row.accountCode} — ${row.accountName}` : '—'}</span>}
        <div className="muted">Accepts {row.allowedTypes.map((t) => t.toLowerCase()).join(' or ')} accounts</div>
        {changed && row.hasPostings && <div className="muted" style={{ color: 'var(--warn)' }}>Existing postings stay on {row.accountCode}; only new postings use the new account.</div>}
        <ErrorNote error={save.error ?? reset.error} />
      </td>
      <td>{row.isMissing ? <Badge value={`Missing (${row.defaultCode})`} tone="bad" /> : row.isDefault ? <Badge value={`Default ${row.defaultCode}`} /> : <Badge value="Custom" tone="info" />}</td>
      <td className="right">{canManage && (
        <div className="row" style={{ justifyContent: 'flex-end' }}>
          <button className="btn small primary" disabled={!changed || save.isPending} onClick={() => save.mutate()}>Save</button>
          {!row.isDefault && <button className="btn small" disabled={reset.isPending} onClick={() => { if (confirm(`Use the default account (${row.defaultCode}) for ${row.label} again?`)) reset.mutate() }}>Reset</button>}
        </div>
      )}</td>
    </tr>
  )
}

function MappingTab() {
  const { can } = useAuth()
  const mappings = useApiQuery<AccountMapping[]>('/finance/account-mappings')
  const accounts = useApiQuery<Account[]>('/finance/accounts')
  const missing = (mappings.data ?? []).filter((m) => m.isMissing)
  return (
    <>
      <p className="hint" style={{ marginBottom: 12 }}>
        Sales, purchasing and inventory post to these roles. Choose the ledger account each one uses; until you do, the default account is used.
        Changes affect new postings only: documents already posted stay on the account they were posted to.
      </p>
      {missing.length > 0 && <div className="note error">No account exists for: {missing.map((m) => m.label).join(', ')}. Postings that need them will be rejected until you map them or create the default account.</div>}
      <ErrorNote error={mappings.error ?? accounts.error} />
      {mappings.isLoading || accounts.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          <table className="data">
            <thead><tr><th>Posting role</th><th>Ledger account</th><th>Status</th><th /></tr></thead>
            <tbody>{(mappings.data ?? []).map((m) => (
              // keyed by the effective account so the selector resets after a save or reset
              <MappingRow key={`${m.role}:${m.accountId ?? 'none'}`} row={m} accounts={accounts.data ?? []} canManage={can(P.mappingManage)} />
            ))}</tbody>
          </table>
        </div>
      )}
    </>
  )
}

interface DraftLine { key: number; accountCode: string; debit: string; credit: string; description: string }

function JournalForm({ accounts, onClose }: { accounts: Account[]; onClose: () => void }) {
  const [date, setDate] = useState(today())
  const [description, setDescription] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([
    { key: 1, accountCode: '', debit: '', credit: '', description: '' },
    { key: 2, accountCode: '', debit: '', credit: '', description: '' },
  ])
  const debit = lines.reduce((s, l) => s + Number(l.debit || 0), 0)
  const credit = lines.reduce((s, l) => s + Number(l.credit || 0), 0)
  const balanced = debit > 0 && Math.abs(debit - credit) < 0.005
  const update = (key: number, patch: Partial<DraftLine>) => setLines((c) => c.map((l) => (l.key === key ? { ...l, ...patch } : l)))
  const save = useApiMutation<void>(() => ({
    path: '/finance/journals',
    body: {
      date, description,
      lines: lines.filter((l) => l.accountCode).map((l) => ({ accountCode: l.accountCode, debit: Number(l.debit || 0), credit: Number(l.credit || 0), description: l.description || null })),
    },
  }), onClose)
  return (
    <Modal title="New journal" onClose={onClose} wide>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
          <Field label="Description"><input value={description} onChange={(e) => setDescription(e.target.value)} required autoFocus /></Field>
        </div>
        <div className="scroll-x"><table className="data">
          <thead><tr><th>Account</th><th>Debit</th><th>Credit</th><th>Memo</th><th /></tr></thead>
          <tbody>{lines.map((l) => (
            <tr key={l.key}>
              <td><select value={l.accountCode} onChange={(e) => update(l.key, { accountCode: e.target.value })} aria-label="Account">
                <option value="">Select…</option>
                {accounts.filter((a) => a.isActive).map((a) => <option key={a.id} value={a.code}>{a.code} — {a.name}</option>)}
              </select></td>
              <td><input type="number" min="0" step="0.01" value={l.debit} onChange={(e) => update(l.key, { debit: e.target.value, credit: e.target.value ? '' : l.credit })} aria-label="Debit" /></td>
              <td><input type="number" min="0" step="0.01" value={l.credit} onChange={(e) => update(l.key, { credit: e.target.value, debit: e.target.value ? '' : l.debit })} aria-label="Credit" /></td>
              <td><input value={l.description} onChange={(e) => update(l.key, { description: e.target.value })} aria-label="Memo" /></td>
              <td><button type="button" className="icon-btn" aria-label="Remove line" disabled={lines.length <= 2} onClick={() => setLines((c) => c.filter((x) => x.key !== l.key))}>×</button></td>
            </tr>
          ))}</tbody>
          <tfoot><tr><td><button type="button" className="btn small" onClick={() => setLines((c) => [...c, { key: Date.now(), accountCode: '', debit: '', credit: '', description: '' }])}>Add line</button></td>
            <td className="num">{formatMoney(debit)}</td><td className="num">{formatMoney(credit)}</td>
            <td colSpan={2}>{balanced ? <Badge value="Balanced" tone="ok" /> : <Badge value={`Difference ${formatMoney(Math.abs(debit - credit))}`} tone="warn" />}</td></tr></tfoot>
        </table></div>
        <ErrorNote error={save.error} />
        <div className="modal-actions"><button type="button" className="btn" onClick={onClose}>Cancel</button><button className="btn primary" disabled={!balanced || save.isPending}>Post journal</button></div>
      </form>
    </Modal>
  )
}

function JournalDetail({ journal, onClose }: { journal: Journal; onClose: () => void }) {
  const { can } = useAuth()
  const reverse = useApiMutation<void>(() => ({ path: `/finance/journals/${journal.id}/reverse`, body: {} }), onClose)
  return (
    <Modal title={`${journal.number} — ${journal.description}`} onClose={onClose} wide>
      <p className="muted">{formatDate(journal.date)} · source {journal.source}{journal.sourceReference ? ` (${journal.sourceReference})` : ''}</p>
      <div className="scroll-x"><table className="data">
        <thead><tr><th>Account</th><th>Memo</th><th className="num">Debit</th><th className="num">Credit</th></tr></thead>
        <tbody>{journal.lines.map((l, index) => (
          <tr key={index}><td>{l.accountCode} — {l.accountName}</td><td>{l.description}</td>
            <td className="num">{l.debit ? formatMoney(l.debit) : ''}</td><td className="num">{l.credit ? formatMoney(l.credit) : ''}</td></tr>
        ))}</tbody>
        <tfoot><tr><td colSpan={2}>Total</td><td className="num">{formatMoney(journal.totalDebit)}</td><td className="num">{formatMoney(journal.totalCredit)}</td></tr></tfoot>
      </table></div>
      <ErrorNote error={reverse.error} />
      <div className="modal-actions">
        {journal.isReversed && <Badge value="Reversed" tone="warn" />}
        {journal.reversalOfJournalId && <Badge value="Reversal entry" tone="info" />}
        {can(P.journalsPost) && !journal.isReversed && !journal.reversalOfJournalId && (
          <button className="btn danger" disabled={reverse.isPending} onClick={() => { if (confirm('Post a reversing journal? The original stays on record.')) reverse.mutate() }}>Reverse journal</button>
        )}
      </div>
    </Modal>
  )
}

function JournalsTab() {
  const { can } = useAuth()
  const journals = useApiQuery<Journal[]>('/finance/journals?take=100')
  const accounts = useApiQuery<Account[]>('/finance/accounts', { enabled: can(P.accountsRead) })
  const [creating, setCreating] = useState(false)
  const [selected, setSelected] = useState<Journal | null>(null)
  return (
    <>
      {can(P.journalsPost) && <div style={{ marginBottom: 12 }}><button className="btn primary" onClick={() => setCreating(true)} disabled={!accounts.data}>New journal</button></div>}
      <ErrorNote error={journals.error} />
      {journals.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          {(journals.data ?? []).length === 0 ? <Empty>No journals yet.</Empty> : (
            <table className="data">
              <thead><tr><th>Number</th><th>Date</th><th>Description</th><th>Source</th><th className="num">Amount</th><th /></tr></thead>
              <tbody>{journals.data!.map((j) => (
                <tr key={j.id} className="click" onClick={() => setSelected(j)}>
                  <td>{j.number}</td><td>{formatDate(j.date)}</td><td>{j.description}</td><td>{j.source}</td>
                  <td className="num">{formatMoney(j.totalDebit)}</td>
                  <td>{j.isReversed ? <Badge value="Reversed" tone="warn" /> : j.reversalOfJournalId ? <Badge value="Reversal" tone="info" /> : null}</td>
                </tr>
              ))}</tbody>
            </table>
          )}
        </div>
      )}
      {creating && accounts.data && <JournalForm accounts={accounts.data} onClose={() => setCreating(false)} />}
      {selected && <JournalDetail journal={selected} onClose={() => setSelected(null)} />}
    </>
  )
}

export function Finance() {
  const { can } = useAuth()
  const [params, setParams] = useSearchParams()
  const tabs = [
    ...(can(P.reportsRead) ? [{ id: 'tb' as Tab, label: 'Trial balance' }] : []),
    ...(can(P.journalsRead) ? [{ id: 'journals' as Tab, label: 'Journals' }] : []),
    ...(can(P.accountsRead) ? [{ id: 'accounts' as Tab, label: 'Chart of accounts' }] : []),
    ...(can(P.accountsRead) ? [{ id: 'mapping' as Tab, label: 'Account mapping' }] : []),
  ]
  const requested = params.get('tab') as Tab | null
  const tab = tabs.find((t) => t.id === requested)?.id ?? tabs[0]?.id
  return (
    <>
      <PageHeader title="Ledger & reports" subtitle="General ledger postings are append-only; corrections are reversals." />
      <Tabs value={tab ?? 'tb'} onChange={(next) => setParams({ tab: next })} tabs={tabs} />
      {tab === 'tb' && <TrialBalanceTab />}
      {tab === 'journals' && <JournalsTab />}
      {tab === 'accounts' && <AccountsTab />}
      {tab === 'mapping' && <MappingTab />}
    </>
  )
}
