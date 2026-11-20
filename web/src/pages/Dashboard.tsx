import { Link } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney } from '../format'
import { Badge, Empty, Kpi, Loading, PageHeader } from '../ui'
import type { Invoice, Order, StockLevel, TrialBalance } from '../api/types'

const DAY = 86_400_000

export function Dashboard() {
  const { can, company } = useAuth()
  const tb = useApiQuery<TrialBalance>('/finance/trial-balance', { enabled: can(P.reportsRead) })
  const invoices = useApiQuery<Invoice[]>('/sales/invoices?take=500', { enabled: can(P.invoicesRead) })
  const orders = useApiQuery<Order[]>('/sales/orders?take=500', { enabled: can(P.ordersRead) })
  const stock = useApiQuery<StockLevel[]>('/inventory/stock', { enabled: can(P.stockRead) })

  const net = (code: string) => {
    const row = tb.data?.rows.find((r) => r.accountCode === code)
    return row ? row.debit - row.credit : 0
  }
  const open = (invoices.data ?? []).filter((i) => i.balance > 0)
  const overdue = open.filter((i) => i.isOverdue)
  const receivables = open.reduce((sum, i) => sum + i.balance, 0)
  const stockValue = (stock.data ?? []).reduce((sum, s) => sum + s.value, 0)
  const revenue = -net('4000')
  const grossProfit = revenue - net('5000')

  const now = Date.now()
  const ageing = [
    { label: 'Not yet due', test: (d: number) => d <= 0, tone: 'ok' },
    { label: '1–30 days', test: (d: number) => d >= 1 && d <= 30, tone: 'warn' },
    { label: '31–60 days', test: (d: number) => d >= 31 && d <= 60, tone: 'warn' },
    { label: '60+ days', test: (d: number) => d > 60, tone: 'bad' },
  ].map((bucket) => ({
    ...bucket,
    amount: open
      .filter((i) => bucket.test(Math.floor((now - new Date(`${i.dueDate}T00:00:00`).getTime()) / DAY)))
      .reduce((sum, i) => sum + i.balance, 0),
  }))
  const maxAgeing = Math.max(...ageing.map((b) => b.amount), 1)

  const statuses = ['Draft', 'Open', 'Released', 'Invoiced', 'Cancelled'] as const
  const counts = statuses.map((s) => ({ status: s, count: (orders.data ?? []).filter((o) => o.status === s).length }))
  const maxCount = Math.max(...counts.map((c) => c.count), 1)
  const lowStock = (stock.data ?? []).filter((s) => s.onHand <= 8).sort((a, b) => a.onHand - b.onHand).slice(0, 6)
  const recent = (invoices.data ?? []).slice(0, 6)
  const loading = tb.isLoading || invoices.isLoading || orders.isLoading || stock.isLoading

  return (
    <>
      <PageHeader title="Dashboard" subtitle={company?.name} />
      {loading && <Loading />}
      <div className="grid kpis">
        {can(P.reportsRead) && <Kpi label="Cash & bank" value={formatMoney(net('1000') + net('1010'))} />}
        {can(P.invoicesRead) && <Kpi label="Receivables" value={formatMoney(receivables)} hint={`${open.length} open invoices`} />}
        {can(P.invoicesRead) && <Kpi label="Overdue" value={formatMoney(overdue.reduce((s, i) => s + i.balance, 0))} hint={`${overdue.length} invoices`} tone={overdue.length ? 'bad' : ''} />}
        {can(P.stockRead) && <Kpi label="Inventory value" value={formatMoney(stockValue)} />}
        {can(P.reportsRead) && <Kpi label="Revenue" value={formatMoney(revenue)} />}
        {can(P.reportsRead) && <Kpi label="Gross profit" value={formatMoney(grossProfit)} hint={revenue ? `${((grossProfit / revenue) * 100).toFixed(1)}% margin` : undefined} tone="ok" />}
      </div>

      <div className="grid two" style={{ marginTop: 16 }}>
        {can(P.ordersRead) && (
          <section className="card">
            <h3>Order pipeline</h3>
            {counts.map((c) => (
              <div className="bar-row" key={c.status}>
                <span>{c.status}</span>
                <div className="bar"><i style={{ width: `${(c.count / maxCount) * 100}%` }} /></div>
                <span className="right">{c.count}</span>
              </div>
            ))}
          </section>
        )}
        {can(P.invoicesRead) && (
          <section className="card">
            <h3>Receivables ageing</h3>
            {ageing.map((b) => (
              <div className="bar-row" key={b.label}>
                <span>{b.label}</span>
                <div className={`bar ${b.tone}`}><i style={{ width: `${(b.amount / maxAgeing) * 100}%` }} /></div>
                <span className="right">{formatMoney(b.amount)}</span>
              </div>
            ))}
          </section>
        )}
        {can(P.invoicesRead) && (
          <section className="card flush">
            <div className="spread" style={{ padding: 16 }}><h3 style={{ margin: 0 }}>Recent invoices</h3><Link to="/invoices">View all</Link></div>
            {recent.length === 0 ? <Empty>No invoices yet.</Empty> : (
              <table className="data"><tbody>
                {recent.map((i) => (
                  <tr key={i.id}>
                    <td><Link to={`/invoices/${i.id}`}>{i.number}</Link><div className="muted">{i.customerName}</div></td>
                    <td className="num">{formatMoney(i.total)}<div className="muted">due {formatDate(i.dueDate)}</div></td>
                    <td>{i.status === 'Paid' ? <Badge value="Paid" /> : i.isOverdue ? <Badge value="Overdue" /> : <Badge value="Open" />}</td>
                  </tr>
                ))}
              </tbody></table>
            )}
          </section>
        )}
        {can(P.stockRead) && (
          <section className="card flush">
            <div className="spread" style={{ padding: 16 }}><h3 style={{ margin: 0 }}>Low stock</h3><Link to="/items">Manage</Link></div>
            {lowStock.length === 0 ? <Empty>All items are well stocked.</Empty> : (
              <table className="data"><tbody>
                {lowStock.map((s) => (
                  <tr key={s.itemId}>
                    <td>{s.sku}<div className="muted">{s.itemName}</div></td>
                    <td className="num"><Badge value={`${s.onHand} ${s.unitOfMeasure}`} tone={s.onHand === 0 ? 'bad' : 'warn'} /></td>
                  </tr>
                ))}
              </tbody></table>
            )}
          </section>
        )}
      </div>
    </>
  )
}
