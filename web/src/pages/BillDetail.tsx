import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, today } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import type { Bill, VendorPayment } from '../api/types'

function BillPaymentForm({ bill, onClose }: { bill: Bill; onClose: () => void }) {
  const [amount, setAmount] = useState(String(bill.balance))
  const [method, setMethod] = useState<'Cash' | 'Bank'>('Bank')
  const [date, setDate] = useState(today())
  const [reference, setReference] = useState('')
  const save = useApiMutation<void>(
    () => ({ path: `/purchasing/bills/${bill.id}/payments`, body: { amount: Number(amount), method, date, reference: reference || null } }),
    onClose,
  )
  return (
    <Modal title={`Pay bill — ${bill.number}`} onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Amount" hint={`Balance ${formatMoney(bill.balance)}`}>
            <input type="number" min="0.01" max={bill.balance} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required autoFocus />
          </Field>
          <Field label="Paid from">
            <select value={method} onChange={(e) => setMethod(e.target.value as 'Cash' | 'Bank')}>
              <option value="Bank">Bank account</option><option value="Cash">Cash on hand</option>
            </select>
          </Field>
          <Field label="Date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
          <Field label="Reference"><input value={reference} onChange={(e) => setReference(e.target.value)} placeholder="Cheque / transfer no." /></Field>
        </div>
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Posting…' : 'Pay bill'}</button>
        </div>
      </form>
    </Modal>
  )
}

export function BillDetail() {
  const { id } = useParams()
  const { can } = useAuth()
  const bill = useApiQuery<Bill>(`/purchasing/bills/${id}`)
  const payments = useApiQuery<VendorPayment[]>('/purchasing/payments?take=500')
  const [paying, setPaying] = useState(false)

  if (bill.isLoading) return <Loading />
  const i = bill.data
  if (!i) return <ErrorNote error={bill.error ?? new Error('Bill not found.')} />
  const mine = (payments.data ?? []).filter((p) => p.billId === i.id)

  return (
    <>
      <PageHeader title={i.number} subtitle={`${i.vendorName} · billed ${formatDate(i.billDate)} · due ${formatDate(i.dueDate)}`}
        actions={<>
          {i.status === 'Paid' ? <Badge value="Paid" /> : i.isOverdue ? <Badge value="Overdue" /> : <Badge value="Open" />}
          {can(P.vendorPay) && i.balance > 0 && <button className="btn primary" onClick={() => setPaying(true)}>Pay bill</button>}
          <Link className="btn" to="/bills">Back</Link>
        </>} />
      <section className="card flush scroll-x">
        <table className="data">
          <thead><tr><th>SKU</th><th>Description</th><th className="num">Qty</th><th className="num">Order cost</th><th className="num">Billed cost</th><th>Match</th><th className="num">Total</th></tr></thead>
          <tbody>{i.lines.map((l, index) => (
            <tr key={index}><td>{l.sku}</td><td>{l.description}</td><td className="num">{l.quantity}</td><td className="num">{formatMoney(l.orderUnitCost)}</td><td className="num">{formatMoney(l.unitCost)}</td><td><Badge value={l.matchStatus} tone={l.matchStatus === 'Matched' ? 'ok' : l.matchStatus === 'Variance approved' ? 'bad' : 'warn'} /></td><td className="num">{formatMoney(l.lineTotal)}</td></tr>
          ))}</tbody>
          <tfoot>
            <tr><td colSpan={6}>Total</td><td className="num">{formatMoney(i.total)}</td></tr>
            <tr><td colSpan={6}>Paid</td><td className="num">{formatMoney(i.amountPaid)}</td></tr>
            <tr><td colSpan={6}>Balance payable</td><td className="num">{formatMoney(i.balance)}</td></tr>
          </tfoot>
        </table>
      </section>
      <p className="muted" style={{ margin: '12px 0' }}>Posted to the ledger as journal {i.journalNumber}.{i.priceVariance ? ` Includes a price variance of ${formatMoney(i.priceVariance)}${i.varianceOverridden ? ' (approved override)' : ''}.` : ''}{i.vendorReference ? ` Vendor invoice ${i.vendorReference}.` : ''} <Link to={`/purchase-orders/${i.purchaseOrderId}`}>View purchase order</Link></p>

      <h3>Payments</h3>
      <div className="card flush scroll-x">
        {mine.length === 0 ? <Empty>No payments yet.</Empty> : (
          <table className="data">
            <thead><tr><th>VendorPayment</th><th>Date</th><th>Method</th><th>Reference</th><th className="num">Amount</th></tr></thead>
            <tbody>{mine.map((p) => (
              <tr key={p.id}><td>{p.number}</td><td>{formatDate(p.paymentDate)}</td><td>{p.method}</td><td>{p.reference || '—'}</td><td className="num">{formatMoney(p.amount)}</td></tr>
            ))}</tbody>
          </table>
        )}
      </div>
      {paying && <BillPaymentForm bill={i} onClose={() => setPaying(false)} />}
    </>
  )
}
