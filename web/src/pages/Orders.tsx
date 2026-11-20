import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney } from '../format'
import { Badge, Empty, ErrorNote, Loading, PageHeader, Tabs } from '../ui'
import type { Order, OrderStatus } from '../api/types'

type Filter = 'All' | OrderStatus

export function Orders() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const orders = useApiQuery<Order[]>('/sales/orders?take=500')
  const [filter, setFilter] = useState<Filter>('All')
  const all = orders.data ?? []
  const rows = filter === 'All' ? all : all.filter((o) => o.status === filter)
  const tabs = (['All', 'Draft', 'Open', 'Released', 'Invoiced', 'Cancelled'] as const)
    .map((id) => ({ id, label: `${id} (${id === 'All' ? all.length : all.filter((o) => o.status === id).length})` }))

  return (
    <>
      <PageHeader title="Sales orders" subtitle="Draft → Confirmed → Released → Invoiced"
        actions={can(P.ordersManage) && <Link className="btn primary" to="/orders/new">New order</Link>} />
      <Tabs value={filter} onChange={setFilter} tabs={tabs} />
      <ErrorNote error={orders.error} />
      {orders.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          {rows.length === 0 ? <Empty>No orders in this view.</Empty> : (
            <table className="data">
              <thead><tr><th>Number</th><th>Customer</th><th>Date</th><th>Status</th><th className="num">Total</th></tr></thead>
              <tbody>{rows.map((o) => (
                <tr key={o.id} className="click" onClick={() => navigate(`/orders/${o.id}`)}>
                  <td><Link to={`/orders/${o.id}`} onClick={(e) => e.stopPropagation()}>{o.number}</Link></td>
                  <td>{o.customerName}</td><td>{formatDate(o.orderDate)}</td>
                  <td><Badge value={o.status === 'Open' ? 'Open' : o.status} tone={o.status === 'Open' ? 'info' : undefined} /></td>
                  <td className="num">{formatMoney(o.total)}</td>
                </tr>
              ))}</tbody>
            </table>
          )}
        </div>
      )}
    </>
  )
}
