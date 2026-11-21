import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, formatNumber, today } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import { formatRate, round2, taxPortion } from '../tax'
import type { Bill, DebitNote, PurchaseOrder, VendorPayment } from '../api/types'

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

function DebitNoteDialog({ bill, order, notes, onClose }: { bill: Bill; order: PurchaseOrder; notes: DebitNote[]; onClose: () => void }) {
  // Per item: no more than this bill charged (less earlier debits) and no more than the billed goods that were returned.
  const rows = bill.lines.map((l) => {
    const orderLine = order.lines.find((x) => x.itemId === l.itemId)
    const cap = Math.max(0, Math.min(l.quantity - l.quantityDebited, orderLine?.debitableQuantity ?? 0))
    return { l, cap }
  }).filter((r) => r.cap > 0)
  const [qty, setQty] = useState<Record<string, string>>(Object.fromEntries(rows.map((r) => [r.l.itemId, String(r.cap)])))
  const [reason, setReason] = useState('')
  const [date, setDate] = useState(today())

  // The input tax given back is this quantity's share of what the bill charged on the line (the API does the same sum).
  const taxBefore = (itemId: string) => notes.flatMap((n) => n.lines).filter((x) => x.itemId === itemId).reduce((s, x) => s + x.taxAmount, 0)
  const chosen = rows.map((r) => {
    const quantity = Number(qty[r.l.itemId] || 0)
    const net = round2(quantity * r.l.unitCost)
    return { ...r, quantity, net, tax: quantity > 0 ? round2(taxPortion(r.l.taxAmount, r.l.quantity, r.l.quantityDebited, taxBefore(r.l.itemId), quantity)) : 0 }
  }).filter((r) => r.quantity > 0)
  const subtotal = chosen.reduce((s, r) => s + r.net, 0)
  const tax = chosen.reduce((s, r) => s + r.tax, 0)
  const total = subtotal + tax
  const applied = Math.min(total, bill.balance)
  const refundDue = total - applied
  const save = useApiMutation<void>(
    () => ({ path: `/purchasing/bills/${bill.id}/debit-notes`, body: { date, reason, lines: chosen.map((r) => ({ itemId: r.l.itemId, quantity: r.quantity })) } }),
    onClose,
  )

  return (
    <Modal title={`Debit note — ${bill.number}`} onClose={onClose} wide>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Reason"><input value={reason} onChange={(e) => setReason(e.target.value)} required autoFocus placeholder="e.g. Faulty goods returned" /></Field>
          <Field label="Debit note date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
        </div>
        <div className="scroll-x"><table className="data">
          <thead><tr><th>Item</th><th className="num">Billed</th><th className="num">Returned, awaiting debit</th><th style={{ width: 120 }}>Debit qty</th><th className="num">Credit at billed price</th><th className="num">Tax</th></tr></thead>
          <tbody>{rows.map((r) => {
            const q = Number(qty[r.l.itemId] || 0)
            return (
              <tr key={r.l.itemId}>
                <td>{r.l.sku}<div className="muted">{formatMoney(r.l.unitCost)} billed</div></td>
                <td className="num">{formatNumber(r.l.quantity)}</td><td className="num">{formatNumber(r.cap)}</td>
                <td><input type="number" min="0" max={r.cap} step="any" value={qty[r.l.itemId] ?? ''} aria-label={`Quantity to debit for ${r.l.sku}`}
                  onChange={(e) => setQty({ ...qty, [r.l.itemId]: e.target.value })} /></td>
                <td className="num">{q > 0 ? formatMoney(round2(q * r.l.unitCost)) : '—'}</td>
                <td className="num">{q > 0 && r.l.taxCode ? formatMoney(chosen.find((c) => c.l.itemId === r.l.itemId)?.tax ?? 0) : '—'}</td>
              </tr>
            )
          })}</tbody>
          <tfoot>
            {tax > 0 && <tr><td colSpan={4}>Subtotal</td><td className="num" colSpan={2}>{formatMoney(subtotal)}</td></tr>}
            {tax > 0 && <tr><td colSpan={4}>Input tax returned</td><td className="num" colSpan={2}>{formatMoney(tax)}</td></tr>}
            <tr><td colSpan={4}>Debit total</td><td className="num" colSpan={2}>{formatMoney(total)}</td></tr>
            <tr><td colSpan={4}>Reduces the bill balance</td><td className="num" colSpan={2}>{formatMoney(applied)}</td></tr>
            {refundDue > 0 && <tr><td colSpan={4}>Owed back by the vendor (refund)</td><td className="num" colSpan={2}>{formatMoney(refundDue)}</td></tr>}
          </tfoot>
        </table></div>
        <p className="hint">The vendor credits what it billed: Dr Accounts Payable / Cr Goods Received Not Invoiced (at the order price), with any price difference reversed from Purchase Price Variance.</p>
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={chosen.length === 0 || !reason.trim() || save.isPending}>{save.isPending ? 'Posting…' : 'Post debit note'}</button>
        </div>
      </form>
    </Modal>
  )
}

function VendorRefundDialog({ note, onClose }: { note: DebitNote; onClose: () => void }) {
  const [amount, setAmount] = useState(String(note.refundDue))
  const [method, setMethod] = useState<'Cash' | 'Bank'>('Bank')
  const [date, setDate] = useState(today())
  const [reference, setReference] = useState('')
  const save = useApiMutation<void>(
    () => ({ path: `/purchasing/debit-notes/${note.id}/refunds`, body: { amount: Number(amount), method, date, reference: reference || null } }),
    onClose,
  )
  return (
    <Modal title={`Vendor refund — ${note.number}`} onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Amount received" hint={`${note.vendorName} owes ${formatMoney(note.refundDue)}`}>
            <input type="number" min="0.01" max={note.refundDue} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required autoFocus />
          </Field>
          <Field label="Received into">
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
          <button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Posting…' : 'Record refund'}</button>
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
  const notes = useApiQuery<DebitNote[]>('/purchasing/debit-notes?take=500')
  const order = useApiQuery<PurchaseOrder>(`/purchasing/orders/${bill.data?.purchaseOrderId}`, { enabled: !!bill.data })
  const [dialog, setDialog] = useState<'pay' | 'debit' | { refund: DebitNote } | null>(null)

  if (bill.isLoading) return <Loading />
  const i = bill.data
  if (!i) return <ErrorNote error={bill.error ?? new Error('Bill not found.')} />
  const mine = (payments.data ?? []).filter((p) => p.billId === i.id)
  const myNotes = (notes.data ?? []).filter((n) => n.billId === i.id)
  const debitable = order.data
    ? i.lines.some((l) => Math.min(l.quantity - l.quantityDebited, order.data!.lines.find((x) => x.itemId === l.itemId)?.debitableQuantity ?? 0) > 0)
    : false
  const close = () => setDialog(null)

  return (
    <>
      <PageHeader title={i.number} subtitle={`${i.vendorName} · billed ${formatDate(i.billDate)} · due ${formatDate(i.dueDate)}`}
        actions={<>
          {i.status === 'Paid' ? <Badge value="Paid" /> : i.status === 'Debited' ? <Badge value="Debited" /> : i.isOverdue ? <Badge value="Overdue" /> : <Badge value="Open" />}
          {can(P.vendorPay) && i.balance > 0 && <button className="btn primary" onClick={() => setDialog('pay')}>Pay bill</button>}
          {can(P.debitNotesPost) && debitable && order.data && <button className="btn" onClick={() => setDialog('debit')}>Debit note</button>}
          <Link className="btn" to="/bills">Back</Link>
        </>} />
      <section className="card flush scroll-x">
        <table className="data">
          <thead><tr><th>SKU</th><th>Description</th><th className="num">Qty</th><th className="num">Debited</th><th className="num">Order cost</th><th className="num">Billed cost</th><th>Match</th><th className="num">Amount</th><th className="num">Tax</th></tr></thead>
          <tbody>{i.lines.map((l, index) => (
            <tr key={index}><td>{l.sku}</td><td>{l.description}</td><td className="num">{l.quantity}</td>
              <td className="num">{l.quantityDebited > 0 ? <Badge value={formatNumber(l.quantityDebited)} tone="info" /> : '—'}</td>
              <td className="num">{formatMoney(l.orderUnitCost)}</td><td className="num">{formatMoney(l.unitCost)}</td>
              <td><Badge value={l.matchStatus} tone={l.matchStatus === 'Matched' ? 'ok' : l.matchStatus === 'Variance approved' ? 'bad' : 'warn'} /></td>
              <td className="num">{formatMoney(l.lineTotal)}</td>
              <td className="num">{l.taxCode ? <>{formatMoney(l.taxAmount)}<div className="muted">{l.taxCode} {formatRate(l.taxRate)}</div></> : '—'}</td></tr>
          ))}</tbody>
          <tfoot>
            {i.taxTotal > 0 && <tr><td colSpan={7}>Subtotal</td><td className="num" colSpan={2}>{formatMoney(i.subtotal)}</td></tr>}
            {i.taxTotal > 0 && <tr><td colSpan={7}>Input tax</td><td className="num" colSpan={2}>{formatMoney(i.taxTotal)}</td></tr>}
            <tr><td colSpan={7}>Total</td><td className="num" colSpan={2}>{formatMoney(i.total)}</td></tr>
            {i.amountDebited > 0 && <tr><td colSpan={7}>Debited</td><td className="num" colSpan={2}>−{formatMoney(i.amountDebited)}</td></tr>}
            <tr><td colSpan={7}>Paid</td><td className="num" colSpan={2}>{formatMoney(i.amountPaid)}</td></tr>
            <tr><td colSpan={7}>Balance payable</td><td className="num" colSpan={2}>{formatMoney(i.balance)}</td></tr>
          </tfoot>
        </table>
      </section>
      <p className="muted" style={{ margin: '12px 0' }}>
        Posted to the ledger as journal {i.journalNumber}.{i.vendorReference ? ` Vendor invoice ${i.vendorReference}.` : ''}
        {i.priceVariance ? ` Includes a price variance of ${formatMoney(i.priceVariance)}${i.varianceOverridden ? ' (approved override)' : ''}.` : ''}{' '}
        <Link to={`/purchase-orders/${i.purchaseOrderId}`}>View purchase order</Link>
      </p>

      <h3>Debit notes</h3>
      <div className="card flush scroll-x" style={{ marginBottom: 16 }}>
        {myNotes.length === 0 ? <Empty>No debit notes.</Empty> : (
          <table className="data">
            <thead><tr><th>Debit note</th><th>Date</th><th>Reason</th><th className="num">Total</th><th className="num">Tax</th><th className="num">Applied</th><th className="num">Refunded</th><th className="num">Vendor owes</th><th /></tr></thead>
            <tbody>{myNotes.map((n) => (
              <tr key={n.id}>
                <td>{n.number}<div className="muted">{n.journalNumber}</div></td><td>{formatDate(n.debitDate)}</td><td>{n.reason}</td>
                <td className="num">{formatMoney(n.total)}</td><td className="num">{n.taxTotal ? formatMoney(n.taxTotal) : '—'}</td><td className="num">{formatMoney(n.amountApplied)}</td>
                <td className="num">{n.amountRefunded ? formatMoney(n.amountRefunded) : '—'}</td>
                <td className="num">{n.refundDue > 0 ? <Badge value={formatMoney(n.refundDue)} tone="warn" /> : '—'}</td>
                <td className="right">{can(P.vendorRefundsRecord) && n.refundDue > 0 && <button className="btn small" onClick={() => setDialog({ refund: n })}>Record refund</button>}</td>
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
      {dialog === 'pay' && <BillPaymentForm bill={i} onClose={close} />}
      {dialog === 'debit' && order.data && <DebitNoteDialog bill={i} order={order.data} notes={myNotes} onClose={close} />}
      {typeof dialog === 'object' && dialog && <VendorRefundDialog note={dialog.refund} onClose={close} />}
    </>
  )
}
