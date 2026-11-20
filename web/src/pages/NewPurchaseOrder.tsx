import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useApiMutation, useApiQuery } from '../hooks'
import { formatMoney, today } from '../format'
import { ErrorNote, Field, Loading, PageHeader } from '../ui'
import type { Item, PurchaseOrder, StockLevel, Vendor } from '../api/types'

interface Line { key: number; itemId: string; quantity: string; unitCost: string }

export function NewPurchaseOrder() {
  const navigate = useNavigate()
  const vendors = useApiQuery<Vendor[]>('/purchasing/vendors')
  const items = useApiQuery<Item[]>('/inventory/items')
  const stock = useApiQuery<StockLevel[]>('/inventory/stock')
  const [vendorId, setVendorId] = useState('')
  const [orderDate, setOrderDate] = useState(today())
  const [notes, setNotes] = useState('')
  const [lines, setLines] = useState<Line[]>([{ key: 1, itemId: '', quantity: '1', unitCost: '' }])

  const activeItems = (items.data ?? []).filter((i) => i.isActive)
  const onHand = (id: string) => stock.data?.find((s) => s.itemId === id)?.onHand
  const total = lines.reduce((sum, l) => sum + Math.round(Number(l.quantity || 0) * Number(l.unitCost || 0) * 100) / 100, 0)
  const update = (key: number, patch: Partial<Line>) => setLines((c) => c.map((l) => (l.key === key ? { ...l, ...patch } : l)))
  const pickItem = (key: number, itemId: string) => update(key, { itemId, unitCost: String(items.data?.find((i) => i.id === itemId)?.unitCost ?? '') })

  const create = useApiMutation<void, PurchaseOrder>(
    () => ({
      path: '/purchasing/orders',
      body: { vendorId, orderDate, notes: notes || null, lines: lines.map((l) => ({ itemId: l.itemId, quantity: Number(l.quantity), unitCost: Number(l.unitCost) })) },
    }),
    (order) => navigate(`/purchase-orders/${order.id}`),
  )

  if (vendors.isLoading || items.isLoading) return <Loading />

  return (
    <>
      <PageHeader title="New purchase order" subtitle="Saved as a draft. Confirm, send and receive it from the order page."
        actions={<Link className="btn" to="/purchase-orders">Cancel</Link>} />
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); create.mutate() }}>
        <section className="card">
          <div className="form-grid">
            <Field label="Vendor">
              <select value={vendorId} onChange={(e) => setVendorId(e.target.value)} required>
                <option value="">Select a vendor…</option>
                {(vendors.data ?? []).filter((v) => v.isActive).map((v) => <option key={v.id} value={v.id}>{v.code} — {v.name}</option>)}
              </select>
            </Field>
            <Field label="Order date"><input type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} required /></Field>
            <Field label="Notes"><input value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
          </div>
        </section>
        <section className="card flush scroll-x">
          <table className="data">
            <thead><tr><th style={{ width: '40%' }}>Item</th><th className="num">On hand</th><th>Quantity</th><th>Unit cost</th><th className="num">Line total</th><th /></tr></thead>
            <tbody>
              {lines.map((l) => (
                <tr key={l.key}>
                  <td>
                    <select value={l.itemId} onChange={(e) => pickItem(l.key, e.target.value)} required aria-label="Item">
                      <option value="">Select an item…</option>
                      {activeItems.map((i) => <option key={i.id} value={i.id}>{i.sku} — {i.name}</option>)}
                    </select>
                  </td>
                  <td className="num">{onHand(l.itemId) ?? '—'}</td>
                  <td><input type="number" min="0.0001" step="any" value={l.quantity} onChange={(e) => update(l.key, { quantity: e.target.value })} required aria-label="Quantity" /></td>
                  <td><input type="number" min="0" step="0.01" value={l.unitCost} onChange={(e) => update(l.key, { unitCost: e.target.value })} required aria-label="Unit cost" /></td>
                  <td className="num">{formatMoney(Number(l.quantity || 0) * Number(l.unitCost || 0))}</td>
                  <td><button type="button" className="icon-btn" aria-label="Remove line" disabled={lines.length === 1} onClick={() => setLines((c) => c.filter((x) => x.key !== l.key))}>×</button></td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={4}>
              <button type="button" className="btn small" onClick={() => setLines((c) => [...c, { key: Date.now(), itemId: '', quantity: '1', unitCost: '' }])}>Add line</button>
            </td><td className="num">{formatMoney(total)}</td><td /></tr></tfoot>
          </table>
        </section>
        <ErrorNote error={create.error} />
        <div><button className="btn primary" disabled={create.isPending}>{create.isPending ? 'Saving…' : 'Save draft'}</button></div>
      </form>
    </>
  )
}
