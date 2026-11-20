import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney } from '../format'
import { Badge, Empty, ErrorNote, Loading, PageHeader, Tabs } from '../ui'
import type { PurchaseOrder, PurchaseOrderStatus } from '../api/types'

type Filter = 'All' | PurchaseOrderStatus

export function PurchaseOrders() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const orders = useApiQuery<PurchaseOrder[]>('/purchasing/orders?take=500')
  const [filter, setFilter] = useState<Filter>('All')
  const all = orders.data ?? []
  const rows = filter === 'All' ? all : all.filter((o) => o.status === filter)
  const tabs = (['All', 'Draft', 'Open', 'Released', 'PartiallyReceived', 'Received', 'Billed', 'Cancelled'] as const)
    .map((id) => ({ id, label: `${id === 'PartiallyReceived' ? 'Part-received' : id} (${id === 'All' ? all.length : all.filter((o) => o.status === id).length})` }))

  return (
    <>
      <PageHeader title="Purchase orders" subtitle="Draft → Confirmed → Sent → Received (in one or more deliveries) → Billed"
        actions={can(P.poManage) && <Link className="btn primary" to="/purchase-orders/new">New purchase order</Link>} />
      <Tabs value={filter} onChange={setFilter} tabs={tabs} />
      <ErrorNote error={orders.error} />
      {orders.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          {rows.length === 0 ? <Empty>No purchase orders in this view.</Empty> : (
            <table className="data">
              <thead><tr><th>Number</th><th>Vendor</th><th>Date</th><th>Status</th><th className="num">Total</th></tr></thead>
              <tbody>{rows.map((o) => (
                <tr key={o.id} className="click" onClick={() => navigate(`/purchase-orders/${o.id}`)}>
                  <td><Link to={`/purchase-orders/${o.id}`} onClick={(e) => e.stopPropagation()}>{o.number}</Link></td>
                  <td>{o.vendorName}</td><td>{formatDate(o.orderDate)}</td><td><Badge value={o.status} /></td>
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
