import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, today } from '../format'
import { Badge, ErrorNote, Loading, PageHeader } from '../ui'
import type { Bill, PurchaseOrder } from '../api/types'

export function PurchaseOrderDetail() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { can } = useAuth()
  const order = useApiQuery<PurchaseOrder>(`/purchasing/orders/${id}`)
  const [date, setDate] = useState(today())
  const [vendorReference, setVendorReference] = useState('')
  const act = useApiMutation<'open' | 'release' | 'cancel'>((action) => ({ path: `/purchasing/orders/${id}/${action}` }))
  const receive = useApiMutation<void>(() => ({ path: `/purchasing/orders/${id}/receive`, body: { date } }))
  const bill = useApiMutation<void, Bill>(
    () => ({ path: `/purchasing/orders/${id}/bill`, body: { date, vendorReference: vendorReference || null } }),
    (created) => navigate(`/bills/${created.id}`),
  )

  if (order.isLoading) return <Loading />
  const o = order.data
  if (!o) return <ErrorNote error={order.error ?? new Error('Purchase order not found.')} />
  const manage = can(P.poManage)

  return (
    <>
      <PageHeader title={o.number} subtitle={`${o.vendorName} · ${formatDate(o.orderDate)}`}
        actions={<><Badge value={o.status} /><Link className="btn" to="/purchase-orders">Back</Link></>} />
      <ErrorNote error={act.error ?? receive.error ?? bill.error} />
      <section className="card flush scroll-x">
        <table className="data">
          <thead><tr><th>SKU</th><th>Description</th><th className="num">Qty</th><th className="num">Unit cost</th><th className="num">Total</th></tr></thead>
          <tbody>{o.lines.map((l, index) => (
            <tr key={index}><td>{l.sku}</td><td>{l.description}</td><td className="num">{l.quantity}</td><td className="num">{formatMoney(l.unitCost)}</td><td className="num">{formatMoney(l.lineTotal)}</td></tr>
          ))}</tbody>
          <tfoot><tr><td colSpan={4}>Total</td><td className="num">{formatMoney(o.total)}</td></tr></tfoot>
        </table>
      </section>
      {o.notes && <p className="muted" style={{ marginTop: 12 }}>Notes: {o.notes}</p>}
      {o.receivedDate && <p className="muted" style={{ marginTop: 12 }}>Goods received {formatDate(o.receivedDate)} — posted as journal {o.receiptJournalNumber}.</p>}

      <div className="row" style={{ marginTop: 16 }}>
        {manage && o.status === 'Draft' && <button className="btn primary" disabled={act.isPending} onClick={() => act.mutate('open')}>Confirm order</button>}
        {manage && o.status === 'Open' && <button className="btn primary" disabled={act.isPending} onClick={() => act.mutate('release')}>Send to vendor</button>}
        {(o.status === 'Released' || o.status === 'Received') && (
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} aria-label="Posting date" style={{ width: 'auto' }} />
        )}
        {can(P.poReceive) && o.status === 'Released' && (
          <button className="btn primary" disabled={receive.isPending} onClick={() => receive.mutate()}>{receive.isPending ? 'Posting…' : 'Receive goods'}</button>
        )}
        {can(P.billsPost) && o.status === 'Received' && (
          <>
            <input value={vendorReference} onChange={(e) => setVendorReference(e.target.value)} placeholder="Vendor invoice no." aria-label="Vendor invoice number" style={{ width: 200 }} />
            <button className="btn primary" disabled={bill.isPending} onClick={() => bill.mutate()}>{bill.isPending ? 'Posting…' : 'Enter vendor bill'}</button>
          </>
        )}
        {manage && ['Draft', 'Open', 'Released'].includes(o.status) && (
          <button className="btn danger" disabled={act.isPending} onClick={() => { if (confirm('Cancel this purchase order?')) act.mutate('cancel') }}>Cancel order</button>
        )}
        {o.billId && <Link className="btn" to={`/bills/${o.billId}`}>View bill</Link>}
      </div>
      {o.status === 'Released' && <p className="hint" style={{ marginTop: 12 }}>Receiving adds the stock at the order cost and accrues the liability: Dr Inventory / Cr Goods Received Not Invoiced.</p>}
      {o.status === 'Received' && <p className="hint" style={{ marginTop: 12 }}>Entering the vendor bill clears the accrual into payables: Dr Goods Received Not Invoiced / Cr Accounts Payable.</p>}
    </>
  )
}
