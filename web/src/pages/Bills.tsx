import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useApiQuery } from '../hooks'
import { formatDate, formatMoney } from '../format'
import { Badge, Empty, ErrorNote, Kpi, Loading, PageHeader, Tabs } from '../ui'
import type { Bill, VendorPayment } from '../api/types'

type View = 'All' | 'Open' | 'Overdue' | 'Paid' | 'Payments'

export function Bills() {
  const navigate = useNavigate()
  const bills = useApiQuery<Bill[]>('/purchasing/bills?take=500')
  const payments = useApiQuery<VendorPayment[]>('/purchasing/payments?take=500')
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
      </div>
      <Tabs value={view} onChange={setView} tabs={[
        { id: 'All', label: `All (${all.length})` }, { id: 'Open', label: `Open (${open.length})` },
        { id: 'Overdue', label: `Overdue (${overdue.length})` }, { id: 'Paid', label: 'Paid' },
        { id: 'Payments', label: `Payments (${payments.data?.length ?? 0})` },
      ]} />
      <ErrorNote error={bills.error ?? payments.error} />
      {bills.isLoading ? <Loading /> : view === 'Payments' ? (
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
                  <td>{b.status === 'Paid' ? <Badge value="Paid" /> : b.isOverdue ? <Badge value="Overdue" /> : <Badge value="Open" />}</td>
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
