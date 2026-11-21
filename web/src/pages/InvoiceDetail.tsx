import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, formatNumber, today } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import { formatRate, round2, taxPortion } from '../tax'
import type { CreditNote, Invoice, Payment } from '../api/types'

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

function CreditNoteDialog({ invoice, notes, onClose }: { invoice: Invoice; notes: CreditNote[]; onClose: () => void }) {
  const creditable = invoice.lines.filter((l) => l.quantity - l.quantityCredited > 0)
  const [qty, setQty] = useState<Record<string, string>>({})
  const [returned, setReturned] = useState<Record<string, boolean>>(Object.fromEntries(creditable.map((l) => [l.itemId, true])))
  const [reason, setReason] = useState('')
  const [date, setDate] = useState(today())

  // The tax credited is this quantity's share of what the invoice charged on the line (the API does the same sum).
  const taxBefore = (itemId: string) => notes.flatMap((n) => n.lines).filter((x) => x.itemId === itemId).reduce((s, x) => s + x.taxAmount, 0)
  const rows = creditable.map((l) => {
    const quantity = Number(qty[l.itemId] || 0)
    const net = round2(quantity * l.unitPrice)
    return { l, quantity, net, tax: quantity > 0 ? round2(taxPortion(l.taxAmount, l.quantity, l.quantityCredited, taxBefore(l.itemId), quantity)) : 0 }
  }).filter((r) => r.quantity > 0)
  const subtotal = rows.reduce((s, r) => s + r.net, 0)
  const tax = rows.reduce((s, r) => s + r.tax, 0)
  const total = subtotal + tax
  const applied = Math.min(total, invoice.balance)
  const refundDue = total - applied

  const save = useApiMutation<void>(
    () => ({
      path: `/sales/invoices/${invoice.id}/credit-notes`,
      body: { date, reason, lines: rows.map((r) => ({ itemId: r.l.itemId, quantity: r.quantity, returnToStock: !!returned[r.l.itemId] })) },
    }),
    onClose,
  )

  return (
    <Modal title={`Credit note — ${invoice.number}`} onClose={onClose} wide>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Reason"><input value={reason} onChange={(e) => setReason(e.target.value)} required autoFocus placeholder="e.g. Faulty goods returned" /></Field>
          <Field label="Credit date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
        </div>
        <div className="scroll-x"><table className="data">
          <thead><tr><th>Item</th><th className="num">Invoiced</th><th className="num">Creditable</th><th style={{ width: 120 }}>Credit qty</th><th>Goods</th><th className="num">Credit (excl. tax)</th><th className="num">Tax</th></tr></thead>
          <tbody>{creditable.map((l) => {
            const remaining = l.quantity - l.quantityCredited
            const q = Number(qty[l.itemId] || 0)
            return (
              <tr key={l.itemId}>
                <td>{l.sku}<div className="muted">{formatMoney(l.unitPrice)} each</div></td>
                <td className="num">{formatNumber(l.quantity)}</td><td className="num">{formatNumber(remaining)}</td>
                <td><input type="number" min="0" max={remaining} step="any" value={qty[l.itemId] ?? ''} placeholder="0" aria-label={`Quantity to credit for ${l.sku}`}
                  onChange={(e) => setQty({ ...qty, [l.itemId]: e.target.value })} /></td>
                <td><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!returned[l.itemId]} disabled={q <= 0}
                  onChange={(e) => setReturned({ ...returned, [l.itemId]: e.target.checked })} /> Returned to stock</label></td>
                <td className="num">{q > 0 ? formatMoney(round2(q * l.unitPrice)) : '—'}</td>
                <td className="num">{q > 0 && l.taxCode ? formatMoney(rows.find((r) => r.l.itemId === l.itemId)?.tax ?? 0) : '—'}</td>
              </tr>
            )
          })}</tbody>
          <tfoot>
            {tax > 0 && <tr><td colSpan={5}>Subtotal</td><td className="num" colSpan={2}>{formatMoney(subtotal)}</td></tr>}
            {tax > 0 && <tr><td colSpan={5}>Tax credited</td><td className="num" colSpan={2}>{formatMoney(tax)}</td></tr>}
            <tr><td colSpan={5}>Credit total</td><td className="num" colSpan={2}>{formatMoney(total)}</td></tr>
            <tr><td colSpan={5}>Reduces the invoice balance</td><td className="num" colSpan={2}>{formatMoney(applied)}</td></tr>
            {refundDue > 0 && <tr><td colSpan={5}>Owed back to the customer (refund)</td><td className="num" colSpan={2}>{formatMoney(refundDue)}</td></tr>}
          </tfoot>
        </table></div>
        <p className="hint">Revenue is reversed to Sales Returns. Goods marked as returned go back into stock at the cost they left at (Dr Inventory / Cr COGS); untick it for a price allowance where the customer keeps the goods.</p>
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={rows.length === 0 || !reason.trim() || save.isPending}>{save.isPending ? 'Posting…' : 'Issue credit note'}</button>
        </div>
      </form>
    </Modal>
  )
}

function RefundDialog({ note, onClose }: { note: CreditNote; onClose: () => void }) {
  const [amount, setAmount] = useState(String(note.refundDue))
  const [method, setMethod] = useState<'Cash' | 'Bank'>('Bank')
  const [date, setDate] = useState(today())
  const [reference, setReference] = useState('')
  const save = useApiMutation<void>(
    () => ({ path: `/sales/credit-notes/${note.id}/refunds`, body: { amount: Number(amount), method, date, reference: reference || null } }),
    onClose,
  )
  return (
    <Modal title={`Refund customer — ${note.number}`} onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Amount" hint={`Owed to ${note.customerName}: ${formatMoney(note.refundDue)}`}>
            <input type="number" min="0.01" max={note.refundDue} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required autoFocus />
          </Field>
          <Field label="Paid from">
            <select value={method} onChange={(e) => setMethod(e.target.value as 'Cash' | 'Bank')}>
              <option value="Bank">Bank account</option><option value="Cash">Cash on hand</option>
            </select>
          </Field>
          <Field label="Date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
          <Field label="Reference"><input value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
        </div>
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Posting…' : 'Pay refund'}</button>
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
  const notes = useApiQuery<CreditNote[]>('/sales/credit-notes?take=500')
  const [dialog, setDialog] = useState<'pay' | 'credit' | { refund: CreditNote } | null>(null)

  if (invoice.isLoading) return <Loading />
  const i = invoice.data
  if (!i) return <ErrorNote error={invoice.error ?? new Error('Invoice not found.')} />
  const mine = (payments.data ?? []).filter((p) => p.invoiceId === i.id)
  const myNotes = (notes.data ?? []).filter((n) => n.salesInvoiceId === i.id)
  const canCredit = can(P.creditNotesPost) && i.lines.some((l) => l.quantity > l.quantityCredited)
  const close = () => setDialog(null)

  return (
    <>
      <PageHeader title={i.number} subtitle={`${i.customerName} · issued ${formatDate(i.invoiceDate)} · due ${formatDate(i.dueDate)}`}
        actions={<>
          {i.status === 'Paid' ? <Badge value="Paid" /> : i.status === 'Credited' ? <Badge value="Credited" /> : i.isOverdue ? <Badge value="Overdue" /> : <Badge value="Open" />}
          {can(P.paymentsRecord) && i.balance > 0 && <button className="btn primary" onClick={() => setDialog('pay')}>Record payment</button>}
          {canCredit && <button className="btn" onClick={() => setDialog('credit')}>Credit note / return</button>}
          <Link className="btn" to="/invoices">Back</Link>
        </>} />
      <section className="card flush scroll-x">
        <table className="data">
          <thead><tr><th>SKU</th><th>Description</th><th className="num">Qty</th><th className="num">Credited</th><th className="num">Unit price</th><th className="num">Amount</th><th className="num">Tax</th></tr></thead>
          <tbody>{i.lines.map((l, index) => (
            <tr key={index}><td>{l.sku}</td><td>{l.description}</td><td className="num">{l.quantity}</td>
              <td className="num">{l.quantityCredited > 0 ? <Badge value={formatNumber(l.quantityCredited)} tone="info" /> : '—'}</td>
              <td className="num">{formatMoney(l.unitPrice)}</td><td className="num">{formatMoney(l.lineTotal)}</td>
              <td className="num">{l.taxCode ? <>{formatMoney(l.taxAmount)}<div className="muted">{l.taxCode} {formatRate(l.taxRate)}</div></> : '—'}</td></tr>
          ))}</tbody>
          <tfoot>
            {i.taxTotal > 0 && <tr><td colSpan={5}>Subtotal</td><td className="num" colSpan={2}>{formatMoney(i.subtotal)}</td></tr>}
            {i.taxTotal > 0 && <tr><td colSpan={5}>Tax</td><td className="num" colSpan={2}>{formatMoney(i.taxTotal)}</td></tr>}
            <tr><td colSpan={5}>Total</td><td className="num" colSpan={2}>{formatMoney(i.total)}</td></tr>
            {i.amountCredited > 0 && <tr><td colSpan={5}>Credited</td><td className="num" colSpan={2}>−{formatMoney(i.amountCredited)}</td></tr>}
            <tr><td colSpan={5}>Paid</td><td className="num" colSpan={2}>{formatMoney(i.amountPaid)}</td></tr>
            <tr><td colSpan={5}>Balance due</td><td className="num" colSpan={2}>{formatMoney(i.balance)}</td></tr>
          </tfoot>
        </table>
      </section>
      <p className="muted" style={{ margin: '12px 0' }}>Posted to the ledger as journal {i.journalNumber}. <Link to={`/orders/${i.salesOrderId}`}>View order</Link></p>

      <h3>Credit notes</h3>
      <div className="card flush scroll-x" style={{ marginBottom: 16 }}>
        {myNotes.length === 0 ? <Empty>No credit notes.</Empty> : (
          <table className="data">
            <thead><tr><th>Credit note</th><th>Date</th><th>Reason</th><th className="num">Total</th><th className="num">Tax</th><th className="num">Applied</th><th className="num">Refunded</th><th className="num">Refund due</th><th /></tr></thead>
            <tbody>{myNotes.map((n) => (
              <tr key={n.id}>
                <td>{n.number}<div className="muted">{n.returnedCost > 0 ? 'Goods returned' : 'Allowance'}</div></td>
                <td>{formatDate(n.creditDate)}</td><td>{n.reason}</td>
                <td className="num">{formatMoney(n.total)}</td><td className="num">{n.taxTotal ? formatMoney(n.taxTotal) : '—'}</td><td className="num">{formatMoney(n.amountApplied)}</td>
                <td className="num">{n.amountRefunded ? formatMoney(n.amountRefunded) : '—'}</td>
                <td className="num">{n.refundDue > 0 ? <Badge value={formatMoney(n.refundDue)} tone="warn" /> : '—'}</td>
                <td className="right">{can(P.refundsRecord) && n.refundDue > 0 && <button className="btn small" onClick={() => setDialog({ refund: n })}>Refund</button>}</td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </div>

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
      {dialog === 'pay' && <PaymentForm invoice={i} onClose={close} />}
      {dialog === 'credit' && <CreditNoteDialog invoice={i} notes={myNotes} onClose={close} />}
      {typeof dialog === 'object' && dialog && <RefundDialog note={dialog.refund} onClose={close} />}
    </>
  )
}
