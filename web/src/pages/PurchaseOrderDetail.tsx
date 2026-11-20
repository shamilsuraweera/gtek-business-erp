import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, formatNumber, today } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import type { Bill, PurchaseOrder } from '../api/types'

/** Mirrors ThreeWayMatch.PriceTolerancePercent on the server. */
const TOLERANCE = 2
const variancePercent = (order: number, billed: number) => (order === 0 ? (billed === 0 ? 0 : 100) : ((billed - order) / order) * 100)

function ReceiveDialog({ order, onClose }: { order: PurchaseOrder; onClose: () => void }) {
  const open = order.lines.filter((l) => l.quantity - l.quantityReceived > 0)
  const [qty, setQty] = useState<Record<string, string>>(Object.fromEntries(open.map((l) => [l.itemId, String(l.quantity - l.quantityReceived)])))
  const [date, setDate] = useState(today())
  const [reference, setReference] = useState('')
  const lines = open.map((l) => ({ itemId: l.itemId, quantity: Number(qty[l.itemId] || 0) })).filter((l) => l.quantity > 0)
  const save = useApiMutation<void>(() => ({ path: `/purchasing/orders/${order.id}/receive`, body: { date, reference: reference || null, lines } }), onClose)

  return (
    <Modal title={`Receive goods — ${order.number}`} onClose={onClose} wide>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Receipt date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
          <Field label="Delivery note no."><input value={reference} onChange={(e) => setReference(e.target.value)} placeholder="Vendor's delivery note" /></Field>
        </div>
        <div className="scroll-x"><table className="data">
          <thead><tr><th>Item</th><th className="num">Ordered</th><th className="num">Received</th><th className="num">Outstanding</th><th style={{ width: 140 }}>Receive now</th></tr></thead>
          <tbody>{open.map((l) => {
            const outstanding = l.quantity - l.quantityReceived
            return (
              <tr key={l.itemId}>
                <td>{l.sku}<div className="muted">{l.description}</div></td>
                <td className="num">{formatNumber(l.quantity)}</td><td className="num">{formatNumber(l.quantityReceived)}</td><td className="num">{formatNumber(outstanding)}</td>
                <td><input type="number" min="0" max={outstanding} step="any" value={qty[l.itemId]} aria-label={`Quantity to receive for ${l.sku}`}
                  onChange={(e) => setQty({ ...qty, [l.itemId]: e.target.value })} /></td>
              </tr>
            )
          })}</tbody>
        </table></div>
        <p className="hint">Stock is added at the order cost and the liability accrued: Dr Inventory / Cr Goods Received Not Invoiced. Leave a line at 0 to receive it later.</p>
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={lines.length === 0 || save.isPending}>{save.isPending ? 'Posting…' : 'Receive goods'}</button>
        </div>
      </form>
    </Modal>
  )
}

function BillDialog({ order, onClose, onCreated }: { order: PurchaseOrder; onClose: () => void; onCreated: (bill: Bill) => void }) {
  const { can } = useAuth()
  const billable = order.lines.filter((l) => l.quantityReceived - l.quantityBilled > 0)
  const [qty, setQty] = useState<Record<string, string>>(Object.fromEntries(billable.map((l) => [l.itemId, String(l.quantityReceived - l.quantityBilled)])))
  const [cost, setCost] = useState<Record<string, string>>(Object.fromEntries(billable.map((l) => [l.itemId, String(l.unitCost)])))
  const [date, setDate] = useState(today())
  const [vendorReference, setVendorReference] = useState('')
  const [override, setOverride] = useState(false)

  const rows = billable.map((l) => {
    const quantity = Number(qty[l.itemId] || 0)
    const unitCost = Number(cost[l.itemId] || 0)
    const pct = variancePercent(l.unitCost, unitCost)
    return { l, quantity, unitCost, pct, outside: Math.abs(pct) > TOLERANCE, variance: quantity * (unitCost - l.unitCost) }
  }).filter((r) => r.quantity > 0)
  const outside = rows.some((r) => r.outside)
  const total = rows.reduce((s, r) => s + r.quantity * r.unitCost, 0)
  const variance = rows.reduce((s, r) => s + r.variance, 0)

  const save = useApiMutation<void, Bill>(
    () => ({
      path: `/purchasing/orders/${order.id}/bill`,
      body: {
        date, vendorReference: vendorReference || null, overrideVariance: outside && override,
        lines: rows.map((r) => ({ itemId: r.l.itemId, quantity: r.quantity, unitCost: r.unitCost })),
      },
    }),
    onCreated,
  )

  return (
    <Modal title={`Enter vendor bill — ${order.number}`} onClose={onClose} wide>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Vendor invoice no."><input value={vendorReference} onChange={(e) => setVendorReference(e.target.value)} required autoFocus /></Field>
          <Field label="Bill date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
        </div>
        <div className="scroll-x"><table className="data">
          <thead><tr><th>Item</th><th className="num">Received, not billed</th><th style={{ width: 120 }}>Bill qty</th><th className="num">Order cost</th><th style={{ width: 130 }}>Billed cost</th><th>Match</th></tr></thead>
          <tbody>{billable.map((l) => {
            const unbilled = l.quantityReceived - l.quantityBilled
            const pct = variancePercent(l.unitCost, Number(cost[l.itemId] || 0))
            return (
              <tr key={l.itemId}>
                <td>{l.sku}<div className="muted">{l.description}</div></td>
                <td className="num">{formatNumber(unbilled)}</td>
                <td><input type="number" min="0" max={unbilled} step="any" value={qty[l.itemId]} aria-label={`Quantity to bill for ${l.sku}`} onChange={(e) => setQty({ ...qty, [l.itemId]: e.target.value })} /></td>
                <td className="num">{formatMoney(l.unitCost)}</td>
                <td><input type="number" min="0" step="0.01" value={cost[l.itemId]} aria-label={`Billed unit cost for ${l.sku}`} onChange={(e) => setCost({ ...cost, [l.itemId]: e.target.value })} /></td>
                <td>{pct === 0 ? <Badge value="Matched" tone="ok" /> : Math.abs(pct) <= TOLERANCE
                  ? <Badge value={`${pct > 0 ? '+' : ''}${pct.toFixed(2)}% ok`} tone="warn" />
                  : <Badge value={`${pct > 0 ? '+' : ''}${pct.toFixed(2)}% over`} tone="bad" />}</td>
              </tr>
            )
          })}</tbody>
          <tfoot><tr><td colSpan={4}>Bill total {variance !== 0 && <span className="muted"> · price variance {formatMoney(variance)}</span>}</td><td className="num" colSpan={2}>{formatMoney(total)}</td></tr></tfoot>
        </table></div>
        {outside && (
          <div className="note error">
            Price differs from the order by more than {TOLERANCE}%. {can(P.billsOverride)
              ? <label className="row" style={{ marginTop: 6 }}><input type="checkbox" style={{ width: 'auto' }} checked={override} onChange={(e) => setOverride(e.target.checked)} /> Approve this variance (posted to Purchase Price Variance)</label>
              : 'Correct the billed cost, or ask an approver to enter this bill.'}
          </div>
        )}
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={rows.length === 0 || (outside && !override) || save.isPending}>{save.isPending ? 'Posting…' : 'Post bill'}</button>
        </div>
      </form>
    </Modal>
  )
}

export function PurchaseOrderDetail() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { can } = useAuth()
  const order = useApiQuery<PurchaseOrder>(`/purchasing/orders/${id}`)
  const [dialog, setDialog] = useState<'receive' | 'bill' | null>(null)
  const act = useApiMutation<'open' | 'release' | 'cancel' | 'close-short'>((action) => ({ path: `/purchasing/orders/${id}/${action}` }))

  if (order.isLoading) return <Loading />
  const o = order.data
  if (!o) return <ErrorNote error={order.error ?? new Error('Purchase order not found.')} />
  const manage = can(P.poManage)
  const canReceive = (o.status === 'Released' || o.status === 'PartiallyReceived') && can(P.poReceive)
  const canBill = (o.status === 'PartiallyReceived' || o.status === 'Received') && can(P.billsPost) && o.lines.some((l) => l.quantityReceived > l.quantityBilled)
  const outstanding = o.lines.some((l) => l.quantity > l.quantityReceived)
  const hasReceipts = o.receipts.length > 0

  return (
    <>
      <PageHeader title={o.number} subtitle={`${o.vendorName} · ${formatDate(o.orderDate)}`}
        actions={<><Badge value={o.status} /><Link className="btn" to="/purchase-orders">Back</Link></>} />
      <ErrorNote error={act.error} />
      <section className="card flush scroll-x">
        <table className="data">
          <thead><tr><th>SKU</th><th>Description</th><th className="num">Ordered</th><th className="num">Received</th><th className="num">Billed</th><th className="num">Unit cost</th><th className="num">Total</th></tr></thead>
          <tbody>{o.lines.map((l) => (
            <tr key={l.itemId}>
              <td>{l.sku}</td><td>{l.description}</td><td className="num">{formatNumber(l.quantity)}</td>
              <td className="num">{l.quantityReceived >= l.quantity ? <Badge value={formatNumber(l.quantityReceived)} tone="ok" /> : l.quantityReceived > 0 ? <Badge value={`${formatNumber(l.quantityReceived)} of ${formatNumber(l.quantity)}`} tone="warn" /> : '—'}</td>
              <td className="num">{l.quantityBilled > 0 ? formatNumber(l.quantityBilled) : '—'}</td>
              <td className="num">{formatMoney(l.unitCost)}</td><td className="num">{formatMoney(l.lineTotal)}</td>
            </tr>
          ))}</tbody>
          <tfoot><tr><td colSpan={6}>Total</td><td className="num">{formatMoney(o.total)}</td></tr></tfoot>
        </table>
      </section>
      {o.notes && <p className="muted" style={{ marginTop: 12 }}>Notes: {o.notes}</p>}

      <div className="row" style={{ marginTop: 16 }}>
        {manage && o.status === 'Draft' && <button className="btn primary" disabled={act.isPending} onClick={() => act.mutate('open')}>Confirm order</button>}
        {manage && o.status === 'Open' && <button className="btn primary" disabled={act.isPending} onClick={() => act.mutate('release')}>Send to vendor</button>}
        {canReceive && <button className="btn primary" onClick={() => setDialog('receive')}>Receive goods</button>}
        {canBill && <button className="btn primary" onClick={() => setDialog('bill')}>Enter vendor bill</button>}
        {can(P.poReceive) && o.status === 'PartiallyReceived' && outstanding && (
          <button className="btn" disabled={act.isPending} onClick={() => { if (confirm('Accept the delivery as complete? The outstanding quantities will no longer be expected.')) act.mutate('close-short') }}>Close short</button>
        )}
        {manage && ['Draft', 'Open', 'Released'].includes(o.status) && !hasReceipts && (
          <button className="btn danger" disabled={act.isPending} onClick={() => { if (confirm('Cancel this purchase order?')) act.mutate('cancel') }}>Cancel order</button>
        )}
      </div>
      {o.status === 'PartiallyReceived' && <p className="hint" style={{ marginTop: 12 }}>Part of the order is still outstanding. Receive the rest when it arrives, or close short to accept what was delivered.</p>}

      <div className="grid two" style={{ marginTop: 20 }}>
        <section>
          <h3>Goods receipts</h3>
          <div className="card flush scroll-x">
            {o.receipts.length === 0 ? <Empty>Nothing received yet.</Empty> : (
              <table className="data">
                <thead><tr><th>Receipt</th><th>Date</th><th>Delivery note</th><th className="num">Value</th></tr></thead>
                <tbody>{o.receipts.map((r) => (
                  <tr key={r.id}><td>{r.number}<div className="muted">{r.journalNumber}</div></td><td>{formatDate(r.receiptDate)}</td><td>{r.reference || '—'}</td><td className="num">{formatMoney(r.total)}</td></tr>
                ))}</tbody>
              </table>
            )}
          </div>
        </section>
        <section>
          <h3>Vendor bills</h3>
          <div className="card flush scroll-x">
            {o.bills.length === 0 ? <Empty>No bills entered yet.</Empty> : (
              <table className="data">
                <thead><tr><th>Bill</th><th>Date</th><th className="num">Total</th><th className="num">Variance</th></tr></thead>
                <tbody>{o.bills.map((b) => (
                  <tr key={b.id}><td><Link to={`/bills/${b.id}`}>{b.number}</Link></td><td>{formatDate(b.billDate)}</td>
                    <td className="num">{formatMoney(b.total)}</td><td className="num">{b.priceVariance ? formatMoney(b.priceVariance) : '—'}</td></tr>
                ))}</tbody>
              </table>
            )}
          </div>
        </section>
      </div>

      {dialog === 'receive' && <ReceiveDialog order={o} onClose={() => setDialog(null)} />}
      {dialog === 'bill' && <BillDialog order={o} onClose={() => setDialog(null)} onCreated={(bill) => navigate(`/bills/${bill.id}`)} />}
    </>
  )
}
