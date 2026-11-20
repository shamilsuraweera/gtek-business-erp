import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatMoney } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import type { Bill, Vendor } from '../api/types'

function VendorForm({ vendor, onClose }: { vendor?: Vendor; onClose: () => void }) {
  const [code, setCode] = useState(vendor?.code ?? '')
  const [name, setName] = useState(vendor?.name ?? '')
  const [email, setEmail] = useState(vendor?.email ?? '')
  const [phone, setPhone] = useState(vendor?.phone ?? '')
  const [terms, setTerms] = useState(String(vendor?.paymentTermsDays ?? 30))
  const save = useApiMutation<void>(
    () => vendor
      ? { path: `/purchasing/vendors/${vendor.id}`, method: 'PUT', body: { name, email: email || null, phone: phone || null, paymentTermsDays: Number(terms) } }
      : { path: '/purchasing/vendors', body: { code, name, email: email || null, phone: phone || null, paymentTermsDays: Number(terms) } },
    onClose,
  )
  const submit = (event: FormEvent) => { event.preventDefault(); save.mutate() }

  return (
    <Modal title={vendor ? `Edit ${vendor.code}` : 'New vendor'} onClose={onClose}>
      <form className="stack" onSubmit={submit}>
        <div className="form-grid">
          {!vendor && <Field label="Code"><input value={code} onChange={(e) => setCode(e.target.value)} required maxLength={30} autoFocus /></Field>}
          <Field label="Name"><input value={name} onChange={(e) => setName(e.target.value)} required /></Field>
          <Field label="Email"><input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
          <Field label="Phone"><input value={phone} onChange={(e) => setPhone(e.target.value)} /></Field>
          <Field label="Payment terms (days)"><input type="number" min={0} max={365} value={terms} onChange={(e) => setTerms(e.target.value)} required /></Field>
        </div>
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save'}</button>
        </div>
      </form>
    </Modal>
  )
}

export function Vendors() {
  const { can } = useAuth()
  const vendors = useApiQuery<Vendor[]>('/purchasing/vendors')
  const bills = useApiQuery<Bill[]>('/purchasing/bills?take=500', { enabled: can(P.billsRead) })
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<Vendor | 'new' | null>(null)
  const toggle = useApiMutation<Vendor>((c) => ({ path: `/purchasing/vendors/${c.id}/${c.isActive ? 'deactivate' : 'activate'}` }))

  const outstanding = (id: string) => (bills.data ?? []).filter((b) => b.vendorId === id).reduce((sum, b) => sum + b.balance, 0)
  const rows = (vendors.data ?? []).filter((c) => `${c.code} ${c.name} ${c.email ?? ''}`.toLowerCase().includes(search.toLowerCase()))

  return (
    <>
      <PageHeader title="Vendors" subtitle={`${vendors.data?.length ?? 0} vendors`}
        actions={can(P.vendorsManage) && <button className="btn primary" onClick={() => setEditing('new')}>New vendor</button>} />
      <div className="row" style={{ marginBottom: 12 }}>
        <input className="search" placeholder="Search vendors…" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search vendors" />
      </div>
      <ErrorNote error={vendors.error ?? toggle.error} />
      {vendors.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          {rows.length === 0 ? <Empty>No vendors match.</Empty> : (
            <table className="data">
              <thead><tr><th>Code</th><th>Name</th><th>Contact</th><th className="num">Terms</th><th className="num">Payable</th><th>Status</th><th /></tr></thead>
              <tbody>
                {rows.map((c) => (
                  <tr key={c.id}>
                    <td>{c.code}</td><td>{c.name}</td>
                    <td>{c.email ?? '—'}<div className="muted">{c.phone ?? ''}</div></td>
                    <td className="num">{c.paymentTermsDays}d</td>
                    <td className="num">{formatMoney(outstanding(c.id))}</td>
                    <td><Badge value={c.isActive ? 'Active' : 'Inactive'} /></td>
                    <td className="right">
                      {can(P.vendorsManage) && <div className="row" style={{ justifyContent: 'flex-end' }}>
                        <button className="btn small" onClick={() => setEditing(c)}>Edit</button>
                        <button className="btn small" onClick={() => toggle.mutate(c)}>{c.isActive ? 'Deactivate' : 'Activate'}</button>
                      </div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
      {editing && <VendorForm vendor={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </>
  )
}
