import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useApiMutation, useApiQuery } from '../hooks'
import { formatMoney, today } from '../format'
import { ErrorNote, Field, Loading, PageHeader } from '../ui'
import type { Customer, Item, Order, StockLevel } from '../api/types'

interface Line { key: number; itemId: string; quantity: string; unitPrice: string }

export function NewOrder() {
  const navigate = useNavigate()
  const customers = useApiQuery<Customer[]>('/sales/customers')
  const items = useApiQuery<Item[]>('/inventory/items')
  const stock = useApiQuery<StockLevel[]>('/inventory/stock')
  const [customerId, setCustomerId] = useState('')
  const [orderDate, setOrderDate] = useState(today())
  const [notes, setNotes] = useState('')
  const [lines, setLines] = useState<Line[]>([{ key: 1, itemId: '', quantity: '1', unitPrice: '' }])

  const activeItems = (items.data ?? []).filter((i) => i.isActive)
  const itemOf = (id: string) => items.data?.find((i) => i.id === id)
  const onHand = (id: string) => stock.data?.find((s) => s.itemId === id)?.onHand
  const total = lines.reduce((sum, l) => sum + Math.round(Number(l.quantity || 0) * Number(l.unitPrice || 0) * 100) / 100, 0)

  const update = (key: number, patch: Partial<Line>) => setLines((current) => current.map((l) => (l.key === key ? { ...l, ...patch } : l)))
  const pickItem = (key: number, itemId: string) => update(key, { itemId, unitPrice: String(itemOf(itemId)?.unitPrice ?? '') })

  const create = useApiMutation<void, Order>(
    () => ({
      path: '/sales/orders',
      body: {
        customerId, orderDate, notes: notes || null,
        lines: lines.map((l) => ({ itemId: l.itemId, quantity: Number(l.quantity), unitPrice: Number(l.unitPrice) })),
      },
    }),
    (order) => navigate(`/orders/${order.id}`),
  )
  const submit = (event: FormEvent) => { event.preventDefault(); create.mutate() }

  if (customers.isLoading || items.isLoading) return <Loading />

  return (
    <>
      <PageHeader title="New sales order" subtitle="Saved as a draft. Confirm and release it from the order page."
        actions={<Link className="btn" to="/orders">Cancel</Link>} />
      <form className="stack" onSubmit={submit}>
        <section className="card">
          <div className="form-grid">
            <Field label="Customer">
              <select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
                <option value="">Select a customer…</option>
                {(customers.data ?? []).filter((c) => c.isActive).map((c) => <option key={c.id} value={c.id}>{c.code} — {c.name}</option>)}
              </select>
            </Field>
            <Field label="Order date"><input type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} required /></Field>
            <Field label="Notes"><input value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
          </div>
        </section>
        <section className="card flush scroll-x">
          <table className="data">
            <thead><tr><th style={{ width: '40%' }}>Item</th><th className="num">Available</th><th>Quantity</th><th>Unit price</th><th className="num">Line total</th><th /></tr></thead>
            <tbody>
              {lines.map((l) => {
                const available = onHand(l.itemId)
                const short = available !== undefined && Number(l.quantity) > available
                return (
                  <tr key={l.key}>
                    <td>
                      <select value={l.itemId} onChange={(e) => pickItem(l.key, e.target.value)} required aria-label="Item">
                        <option value="">Select an item…</option>
                        {activeItems.filter((i) => i.id === l.itemId || !lines.some((x) => x.itemId === i.id)).map((i) => <option key={i.id} value={i.id}>{i.sku} — {i.name}</option>)}
                      </select>
                    </td>
                    <td className="num" style={{ color: short ? 'var(--bad)' : undefined }}>{available ?? '—'}{short ? ' ⚠' : ''}</td>
                    <td><input type="number" min="0.0001" step="any" value={l.quantity} onChange={(e) => update(l.key, { quantity: e.target.value })} required aria-label="Quantity" /></td>
                    <td><input type="number" min="0" step="0.01" value={l.unitPrice} onChange={(e) => update(l.key, { unitPrice: e.target.value })} required aria-label="Unit price" /></td>
                    <td className="num">{formatMoney(Number(l.quantity || 0) * Number(l.unitPrice || 0))}</td>
                    <td><button type="button" className="icon-btn" aria-label="Remove line" disabled={lines.length === 1}
                      onClick={() => setLines((c) => c.filter((x) => x.key !== l.key))}>×</button></td>
                  </tr>
                )
              })}
            </tbody>
            <tfoot><tr><td colSpan={4}>
              <button type="button" className="btn small" onClick={() => setLines((c) => [...c, { key: Date.now(), itemId: '', quantity: '1', unitPrice: '' }])}>Add line</button>
            </td><td className="num">{formatMoney(total)}</td><td /></tr></tfoot>
          </table>
        </section>
        <ErrorNote error={create.error} />
        <div><button className="btn primary" disabled={create.isPending}>{create.isPending ? 'Saving…' : 'Save draft'}</button></div>
      </form>
    </>
  )
}
