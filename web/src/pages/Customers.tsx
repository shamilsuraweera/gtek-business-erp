import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatMoney } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import type { Customer, Invoice } from '../api/types'

function CustomerForm({ customer, onClose }: { customer?: Customer; onClose: () => void }) {
  const [code, setCode] = useState(customer?.code ?? '')
  const [name, setName] = useState(customer?.name ?? '')
  const [email, setEmail] = useState(customer?.email ?? '')
  const [phone, setPhone] = useState(customer?.phone ?? '')
  const [terms, setTerms] = useState(String(customer?.paymentTermsDays ?? 30))
  const save = useApiMutation<void>(
    () => customer
      ? { path: `/sales/customers/${customer.id}`, method: 'PUT', body: { name, email: email || null, phone: phone || null, paymentTermsDays: Number(terms) } }
      : { path: '/sales/customers', body: { code, name, email: email || null, phone: phone || null, paymentTermsDays: Number(terms) } },
    onClose,
  )
  const submit = (event: FormEvent) => { event.preventDefault(); save.mutate() }

  return (
    <Modal title={customer ? `Edit ${customer.code}` : 'New customer'} onClose={onClose}>
      <form className="stack" onSubmit={submit}>
        <div className="form-grid">
          {!customer && <Field label="Code"><input value={code} onChange={(e) => setCode(e.target.value)} required maxLength={30} autoFocus /></Field>}
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

export function Customers() {
  const { can } = useAuth()
  const customers = useApiQuery<Customer[]>('/sales/customers')
  const invoices = useApiQuery<Invoice[]>('/sales/invoices?take=500', { enabled: can(P.invoicesRead) })
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<Customer | 'new' | null>(null)
  const toggle = useApiMutation<Customer>((c) => ({ path: `/sales/customers/${c.id}/${c.isActive ? 'deactivate' : 'activate'}` }))

  const outstanding = (id: string) => (invoices.data ?? []).filter((i) => i.customerId === id).reduce((sum, i) => sum + i.balance, 0)
  const rows = (customers.data ?? []).filter((c) => `${c.code} ${c.name} ${c.email ?? ''}`.toLowerCase().includes(search.toLowerCase()))

  return (
    <>
      <PageHeader title="Customers" subtitle={`${customers.data?.length ?? 0} customers`}
        actions={can(P.customersManage) && <button className="btn primary" onClick={() => setEditing('new')}>New customer</button>} />
      <div className="row" style={{ marginBottom: 12 }}>
        <input className="search" placeholder="Search customers…" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search customers" />
      </div>
      <ErrorNote error={customers.error ?? toggle.error} />
      {customers.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          {rows.length === 0 ? <Empty>No customers match.</Empty> : (
            <table className="data">
              <thead><tr><th>Code</th><th>Name</th><th>Contact</th><th className="num">Terms</th><th className="num">Outstanding</th><th>Status</th><th /></tr></thead>
              <tbody>
                {rows.map((c) => (
                  <tr key={c.id}>
                    <td>{c.code}</td><td>{c.name}</td>
                    <td>{c.email ?? '—'}<div className="muted">{c.phone ?? ''}</div></td>
                    <td className="num">{c.paymentTermsDays}d</td>
                    <td className="num">{formatMoney(outstanding(c.id))}</td>
                    <td><Badge value={c.isActive ? 'Active' : 'Inactive'} /></td>
                    <td className="right">
                      {can(P.customersManage) && <div className="row" style={{ justifyContent: 'flex-end' }}>
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
      {editing && <CustomerForm customer={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </>
  )
}
