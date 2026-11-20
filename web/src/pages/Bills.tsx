import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useApiQuery } from '../hooks'
import { formatDate, formatMoney } from '../format'
import { Badge, Empty, ErrorNote, Kpi, Loading, PageHeader, Tabs } from '../ui'
import type { Bill, DebitNote, VendorPayment } from '../api/types'

type View = 'All' | 'Open' | 'Overdue' | 'Paid' | 'Payments' | 'Debits'

export function Bills() {
  const navigate = useNavigate()
  const bills = useApiQuery<Bill[]>('/purchasing/bills?take=500')
  const payments = useApiQuery<VendorPayment[]>('/purchasing/payments?take=500')
  const notes = useApiQuery<DebitNote[]>('/purchasing/debit-notes?take=500')
  const [view, setView] = useState<View>('All')
  const all = bills.data ?? []
  const open = all.filter((b) => b.balance > 0)
  const overdue = all.filter((b) => b.isOverdue)
  const rows = view === 'Open' ? open : view === 'Overdue' ? overdue : view === 'Paid' ? all.filter((b) => b.status === 'Paid') : all

  return (
    <>
      <PageHeader title="Bills & payments" />
      <div className="grid kpis" style={{ marginBottom: 16 }}>
        <Kpi label="Payable" value={formatMoney(open.reduce((s, b) => s + b.balance, 0))} hint={`${open.length} open bills`} />
        <Kpi label="Overdue" value={formatMoney(overdue.reduce((s, b) => s + b.balance, 0))} hint={`${overdue.length} bills`} tone={overdue.length ? 'bad' : ''} />
        <Kpi label="Paid to date" value={formatMoney(all.reduce((s, b) => s + b.amountPaid, 0))} tone="ok" />
        <Kpi label="Vendor refunds due" value={formatMoney((notes.data ?? []).reduce((s, n) => s + n.refundDue, 0))} hint={`${(notes.data ?? []).filter((n) => n.refundDue > 0).length} debit notes`} tone={(notes.data ?? []).some((n) => n.refundDue > 0) ? 'bad' : ''} />
      </div>
      <Tabs value={view} onChange={setView} tabs={[
        { id: 'All', label: `All (${all.length})` }, { id: 'Open', label: `Open (${open.length})` },
        { id: 'Overdue', label: `Overdue (${overdue.length})` }, { id: 'Paid', label: 'Paid' },
        { id: 'Payments', label: `Payments (${payments.data?.length ?? 0})` },
        { id: 'Debits', label: `Debit notes (${notes.data?.length ?? 0})` },
      ]} />
      <ErrorNote error={bills.error ?? payments.error ?? notes.error} />
      {bills.isLoading ? <Loading /> : view === 'Debits' ? (
        <div className="card flush scroll-x">
          {(notes.data ?? []).length === 0 ? <Empty>No debit notes entered.</Empty> : (
            <table className="data">
              <thead><tr><th>Debit note</th><th>Bill</th><th>Vendor</th><th>Date</th><th>Reason</th><th className="num">Total</th><th className="num">Vendor owes</th></tr></thead>
              <tbody>{notes.data!.map((n) => (
                <tr key={n.id}><td>{n.number}</td><td><Link to={`/bills/${n.billId}`}>{n.billNumber}</Link></td><td>{n.vendorName}</td>
                  <td>{formatDate(n.debitDate)}</td><td>{n.reason}</td><td className="num">{formatMoney(n.total)}</td>
                  <td className="num">{n.refundDue > 0 ? <Badge value={formatMoney(n.refundDue)} tone="warn" /> : '—'}</td></tr>
              ))}</tbody>
            </table>
          )}
        </div>
      ) : view === 'Payments' ? (
        <div className="card flush scroll-x">
          {(payments.data ?? []).length === 0 ? <Empty>No payments recorded.</Empty> : (
            <table className="data">
              <thead><tr><th>Payment</th><th>Bill</th><th>Vendor</th><th>Date</th><th>Method</th><th className="num">Amount</th></tr></thead>
              <tbody>{payments.data!.map((p) => (
                <tr key={p.id}><td>{p.number}</td><td><Link to={`/bills/${p.billId}`}>{p.billNumber}</Link></td>
                  <td>{p.vendorName}</td><td>{formatDate(p.paymentDate)}</td><td>{p.method}</td><td className="num">{formatMoney(p.amount)}</td></tr>
              ))}</tbody>
            </table>
          )}
        </div>
      ) : (
        <div className="card flush scroll-x">
          {rows.length === 0 ? <Empty>No bills in this view.</Empty> : (
            <table className="data">
              <thead><tr><th>Bill</th><th>Vendor</th><th>Vendor ref</th><th>Date</th><th>Due</th><th>Status</th><th className="num">Total</th><th className="num">Balance</th></tr></thead>
              <tbody>{rows.map((b) => (
                <tr key={b.id} className="click" onClick={() => navigate(`/bills/${b.id}`)}>
                  <td><Link to={`/bills/${b.id}`} onClick={(e) => e.stopPropagation()}>{b.number}</Link></td>
                  <td>{b.vendorName}</td><td>{b.vendorReference ?? '—'}</td><td>{formatDate(b.billDate)}</td><td>{formatDate(b.dueDate)}</td>
                  <td>{b.status === 'Paid' ? <Badge value="Paid" /> : b.status === 'Debited' ? <Badge value="Debited" /> : b.isOverdue ? <Badge value="Overdue" /> : <Badge value="Open" />}</td>
                  <td className="num">{formatMoney(b.total)}</td><td className="num">{formatMoney(b.balance)}</td>
                </tr>
              ))}</tbody>
            </table>
          )}
        </div>
      )}
    </>
  )
}
