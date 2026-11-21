import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, today } from '../format'
import { Badge, ErrorNote, Loading, PageHeader } from '../ui'
import type { Invoice, Order } from '../api/types'

export function OrderDetail() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { can } = useAuth()
  const order = useApiQuery<Order>(`/sales/orders/${id}`)
  const [invoiceDate, setInvoiceDate] = useState(today())
  const act = useApiMutation<'open' | 'release' | 'cancel'>((action) => ({ path: `/sales/orders/${id}/${action}` }))
  const invoice = useApiMutation<void, Invoice>(
    () => ({ path: `/sales/orders/${id}/invoice`, body: { date: invoiceDate } }),
    (created) => navigate(`/invoices/${created.id}`),
  )

  if (order.isLoading) return <Loading />
  const o = order.data
  if (!o) return <ErrorNote error={order.error ?? new Error('Order not found.')} />
  const manage = can(P.ordersManage)

  return (
    <>
      <PageHeader title={o.number} subtitle={`${o.customerName} · ${formatDate(o.orderDate)}`}
        actions={<><Badge value={o.status} tone={o.status === 'Open' ? 'info' : undefined} /><Link className="btn" to="/orders">Back</Link></>} />
      <ErrorNote error={act.error ?? invoice.error} />
      <section className="card flush scroll-x">
        <table className="data">
          <thead><tr><th>SKU</th><th>Description</th><th className="num">Qty</th><th className="num">Unit price</th><th className="num">Amount</th></tr></thead>
          <tbody>{o.lines.map((l, index) => (
            <tr key={index}><td>{l.sku}</td><td>{l.description}</td><td className="num">{l.quantity}</td><td className="num">{formatMoney(l.unitPrice)}</td><td className="num">{formatMoney(l.lineTotal)}</td></tr>
          ))}</tbody>
          <tfoot><tr><td colSpan={4}>Total <span className="muted">(excl. tax — added when invoiced)</span></td><td className="num">{formatMoney(o.total)}</td></tr></tfoot>
        </table>
      </section>
      {o.notes && <p className="muted" style={{ marginTop: 12 }}>Notes: {o.notes}</p>}

      <div className="row" style={{ marginTop: 16 }}>
        {manage && o.status === 'Draft' && <button className="btn primary" disabled={act.isPending} onClick={() => act.mutate('open')}>Confirm order</button>}
        {manage && o.status === 'Open' && <button className="btn primary" disabled={act.isPending} onClick={() => act.mutate('release')}>Release for invoicing</button>}
        {can(P.invoicesPost) && o.status === 'Released' && (
          <>
            <input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} aria-label="Invoice date" style={{ width: 'auto' }} />
            <button className="btn primary" disabled={invoice.isPending} onClick={() => invoice.mutate()}>
              {invoice.isPending ? 'Posting…' : 'Create invoice'}
            </button>
          </>
        )}
        {manage && ['Draft', 'Open', 'Released'].includes(o.status) && (
          <button className="btn danger" disabled={act.isPending} onClick={() => { if (confirm('Cancel this order?')) act.mutate('cancel') }}>Cancel order</button>
        )}
        {o.invoiceId && <Link className="btn" to={`/invoices/${o.invoiceId}`}>View invoice</Link>}
      </div>
      {o.status === 'Released' && <p className="hint" style={{ marginTop: 12 }}>Invoicing issues the stock at standard cost and posts Dr Receivable / Cr Revenue and Dr COGS / Cr Inventory to the ledger.</p>}
    </>
  )
}
