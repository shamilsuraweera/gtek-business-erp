import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, today } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import type { Invoice, Payment } from '../api/types'

function PaymentForm({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const [amount, setAmount] = useState(String(invoice.balance))
  const [method, setMethod] = useState<'Cash' | 'Bank'>('Bank')
  const [date, setDate] = useState(today())
  const [reference, setReference] = useState('')
  const save = useApiMutation<void>(
    () => ({ path: `/sales/invoices/${invoice.id}/payments`, body: { amount: Number(amount), method, date, reference: reference || null } }),
    onClose,
  )
  return (
    <Modal title={`Record payment — ${invoice.number}`} onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Amount" hint={`Balance ${formatMoney(invoice.balance)}`}>
            <input type="number" min="0.01" max={invoice.balance} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required autoFocus />
          </Field>
          <Field label="Received into">
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
          <button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Posting…' : 'Record payment'}</button>
        </div>
      </form>
    </Modal>
  )
}

export function InvoiceDetail() {
  const { id } = useParams()
  const { can } = useAuth()
  const invoice = useApiQuery<Invoice>(`/sales/invoices/${id}`)
  const payments = useApiQuery<Payment[]>('/sales/payments?take=500')
  const [paying, setPaying] = useState(false)

  if (invoice.isLoading) return <Loading />
  const i = invoice.data
  if (!i) return <ErrorNote error={invoice.error ?? new Error('Invoice not found.')} />
  const mine = (payments.data ?? []).filter((p) => p.invoiceId === i.id)

  return (
    <>
      <PageHeader title={i.number} subtitle={`${i.customerName} · issued ${formatDate(i.invoiceDate)} · due ${formatDate(i.dueDate)}`}
        actions={<>
          {i.status === 'Paid' ? <Badge value="Paid" /> : i.isOverdue ? <Badge value="Overdue" /> : <Badge value="Open" />}
          {can(P.paymentsRecord) && i.balance > 0 && <button className="btn primary" onClick={() => setPaying(true)}>Record payment</button>}
          <Link className="btn" to="/invoices">Back</Link>
        </>} />
      <section className="card flush scroll-x">
        <table className="data">
          <thead><tr><th>SKU</th><th>Description</th><th className="num">Qty</th><th className="num">Unit price</th><th className="num">Total</th></tr></thead>
          <tbody>{i.lines.map((l, index) => (
            <tr key={index}><td>{l.sku}</td><td>{l.description}</td><td className="num">{l.quantity}</td><td className="num">{formatMoney(l.unitPrice)}</td><td className="num">{formatMoney(l.lineTotal)}</td></tr>
          ))}</tbody>
          <tfoot>
            <tr><td colSpan={4}>Total</td><td className="num">{formatMoney(i.total)}</td></tr>
            <tr><td colSpan={4}>Paid</td><td className="num">{formatMoney(i.amountPaid)}</td></tr>
            <tr><td colSpan={4}>Balance due</td><td className="num">{formatMoney(i.balance)}</td></tr>
          </tfoot>
        </table>
      </section>
      <p className="muted" style={{ margin: '12px 0' }}>Posted to the ledger as journal {i.journalNumber}. <Link to={`/orders/${i.salesOrderId}`}>View order</Link></p>

      <h3>Payments</h3>
      <div className="card flush scroll-x">
        {mine.length === 0 ? <Empty>No payments yet.</Empty> : (
          <table className="data">
            <thead><tr><th>Payment</th><th>Date</th><th>Method</th><th>Reference</th><th className="num">Amount</th></tr></thead>
            <tbody>{mine.map((p) => (
              <tr key={p.id}><td>{p.number}</td><td>{formatDate(p.paymentDate)}</td><td>{p.method}</td><td>{p.reference || '—'}</td><td className="num">{formatMoney(p.amount)}</td></tr>
            ))}</tbody>
          </table>
        )}
      </div>
      {paying && <PaymentForm invoice={i} onClose={() => setPaying(false)} />}
    </>
  )
}
