import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useApiQuery } from '../hooks'
import { formatDate, formatMoney } from '../format'
import { Badge, Empty, ErrorNote, Kpi, Loading, PageHeader, Tabs } from '../ui'
import type { CreditNote, Invoice, Payment } from '../api/types'

type View = 'All' | 'Open' | 'Overdue' | 'Paid' | 'Payments' | 'Credits'

export function Invoices() {
  const navigate = useNavigate()
  const invoices = useApiQuery<Invoice[]>('/sales/invoices?take=500')
  const payments = useApiQuery<Payment[]>('/sales/payments?take=500')
  const notes = useApiQuery<CreditNote[]>('/sales/credit-notes?take=500')
  const [view, setView] = useState<View>('All')
  const all = invoices.data ?? []
  const open = all.filter((i) => i.balance > 0)
  const overdue = all.filter((i) => i.isOverdue)
  const rows = view === 'Open' ? open : view === 'Overdue' ? overdue : view === 'Paid' ? all.filter((i) => i.status === 'Paid') : all

  return (
    <>
      <PageHeader title="Invoices & payments" />
      <div className="grid kpis" style={{ marginBottom: 16 }}>
        <Kpi label="Outstanding" value={formatMoney(open.reduce((s, i) => s + i.balance, 0))} hint={`${open.length} open invoices`} />
        <Kpi label="Overdue" value={formatMoney(overdue.reduce((s, i) => s + i.balance, 0))} hint={`${overdue.length} invoices`} tone={overdue.length ? 'bad' : ''} />
        <Kpi label="Collected" value={formatMoney(all.reduce((s, i) => s + i.amountPaid, 0))} tone="ok" />
        <Kpi label="Refunds owed" value={formatMoney((notes.data ?? []).reduce((s, n) => s + n.refundDue, 0))} hint={`${(notes.data ?? []).filter((n) => n.refundDue > 0).length} credit notes`} tone={(notes.data ?? []).some((n) => n.refundDue > 0) ? 'bad' : ''} />
      </div>
      <Tabs value={view} onChange={setView} tabs={[
        { id: 'All', label: `All (${all.length})` }, { id: 'Open', label: `Open (${open.length})` },
        { id: 'Overdue', label: `Overdue (${overdue.length})` }, { id: 'Paid', label: 'Paid' },
        { id: 'Payments', label: `Payments (${payments.data?.length ?? 0})` },
        { id: 'Credits', label: `Credit notes (${notes.data?.length ?? 0})` },
      ]} />
      <ErrorNote error={invoices.error ?? payments.error ?? notes.error} />
      {invoices.isLoading ? <Loading /> : view === 'Credits' ? (
        <div className="card flush scroll-x">
          {(notes.data ?? []).length === 0 ? <Empty>No credit notes issued.</Empty> : (
            <table className="data">
              <thead><tr><th>Credit note</th><th>Invoice</th><th>Customer</th><th>Date</th><th>Reason</th><th className="num">Total</th><th className="num">Refund due</th></tr></thead>
              <tbody>{notes.data!.map((n) => (
                <tr key={n.id}><td>{n.number}</td><td><Link to={`/invoices/${n.salesInvoiceId}`}>{n.invoiceNumber}</Link></td><td>{n.customerName}</td>
                  <td>{formatDate(n.creditDate)}</td><td>{n.reason}</td><td className="num">{formatMoney(n.total)}</td>
                  <td className="num">{n.refundDue > 0 ? <Badge value={formatMoney(n.refundDue)} tone="warn" /> : '—'}</td></tr>
              ))}</tbody>
            </table>
          )}
        </div>
      ) : view === 'Payments' ? (
        <div className="card flush scroll-x">
          {(payments.data ?? []).length === 0 ? <Empty>No payments recorded.</Empty> : (
            <table className="data">
              <thead><tr><th>Payment</th><th>Invoice</th><th>Customer</th><th>Date</th><th>Method</th><th className="num">Amount</th></tr></thead>
              <tbody>{payments.data!.map((p) => (
                <tr key={p.id}><td>{p.number}</td><td><Link to={`/invoices/${p.invoiceId}`}>{p.invoiceNumber}</Link></td>
                  <td>{p.customerName}</td><td>{formatDate(p.paymentDate)}</td><td>{p.method}</td><td className="num">{formatMoney(p.amount)}</td></tr>
              ))}</tbody>
            </table>
          )}
        </div>
      ) : (
        <div className="card flush scroll-x">
          {rows.length === 0 ? <Empty>No invoices in this view.</Empty> : (
            <table className="data">
              <thead><tr><th>Invoice</th><th>Customer</th><th>Date</th><th>Due</th><th>Status</th><th className="num">Total</th><th className="num">Balance</th></tr></thead>
              <tbody>{rows.map((i) => (
                <tr key={i.id} className="click" onClick={() => navigate(`/invoices/${i.id}`)}>
                  <td><Link to={`/invoices/${i.id}`} onClick={(e) => e.stopPropagation()}>{i.number}</Link></td>
                  <td>{i.customerName}</td><td>{formatDate(i.invoiceDate)}</td><td>{formatDate(i.dueDate)}</td>
                  <td>{i.status === 'Paid' ? <Badge value="Paid" /> : i.status === 'Credited' ? <Badge value="Credited" /> : i.isOverdue ? <Badge value="Overdue" /> : <Badge value="Open" />}</td>
                  <td className="num">{formatMoney(i.total)}</td><td className="num">{formatMoney(i.balance)}</td>
                </tr>
              ))}</tbody>
            </table>
          )}
        </div>
      )}
    </>
  )
}
