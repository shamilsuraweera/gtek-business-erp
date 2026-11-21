import { NavLink, Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from './auth'
import { P } from './perm'
import { Loading } from './ui'
import { Login } from './pages/Login'
import { Dashboard } from './pages/Dashboard'
import { Customers } from './pages/Customers'
import { Items } from './pages/Items'
import { Orders } from './pages/Orders'
import { NewOrder } from './pages/NewOrder'
import { OrderDetail } from './pages/OrderDetail'
import { Invoices } from './pages/Invoices'
import { InvoiceDetail } from './pages/InvoiceDetail'
import { Vendors } from './pages/Vendors'
import { ToleranceRequests } from './pages/ToleranceRequests'
import { PurchaseOrders } from './pages/PurchaseOrders'
import { NewPurchaseOrder } from './pages/NewPurchaseOrder'
import { PurchaseOrderDetail } from './pages/PurchaseOrderDetail'
import { Bills } from './pages/Bills'
import { BillDetail } from './pages/BillDetail'
import { Finance } from './pages/Finance'
import { Admin } from './pages/Admin'
import { NotificationBell } from './Notifications'

function Layout() {
  const { user, company, companies, can, logout, selectCompany } = useAuth()
  // A group heading is only shown when the user may open at least one of its pages.
  const groups: { title?: string; links: { to: string; label: string; allowed: boolean }[] }[] = [
    { links: [{ to: '/', label: 'Dashboard', allowed: true }] },
    { title: 'Sales', links: [
      { to: '/orders', label: 'Sales orders', allowed: can(P.ordersRead) },
      { to: '/invoices', label: 'Invoices & payments', allowed: can(P.invoicesRead) },
      { to: '/customers', label: 'Customers', allowed: can(P.customersRead) },
    ] },
    { title: 'Purchasing', links: [
      { to: '/purchase-orders', label: 'Purchase orders', allowed: can(P.poRead) },
      { to: '/tolerance-requests', label: 'Tolerance requests', allowed: can(P.poRead) },
      { to: '/bills', label: 'Bills & payments', allowed: can(P.billsRead) },
      { to: '/vendors', label: 'Vendors', allowed: can(P.vendorsRead) },
    ] },
    { title: 'Inventory', links: [{ to: '/items', label: 'Items & stock', allowed: can(P.itemsRead) }] },
    { title: 'Finance', links: [{ to: '/finance', label: 'Ledger & reports', allowed: can(P.reportsRead) || can(P.journalsRead) || can(P.accountsRead) }] },
    { title: 'System', links: [{ to: '/admin', label: 'Administration', allowed: can(P.periodsRead) || can(P.usersRead) || can(P.auditRead) || can(P.retentionRead) }] },
  ]

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">GTEK <span>ERP</span></div>
        <nav className="nav" aria-label="Main">
          {groups.map((group) => {
            const visible = group.links.filter((l) => l.allowed)
            if (visible.length === 0) return null
            return (
              <div key={group.title ?? 'top'}>
                {group.title && <div className="nav-group">{group.title}</div>}
                {visible.map((l) => <NavLink key={l.to} to={l.to} end={l.to === '/'}>{l.label}</NavLink>)}
              </div>
            )
          })}
        </nav>
      </aside>
      <div className="main">
        <div className="topbar">
          <label className="row">
            <span className="muted">Company</span>
            <select value={company?.id ?? ''} onChange={(event) => selectCompany(event.target.value)} aria-label="Active company" style={{ width: 'auto' }}>
              {companies.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </label>
          {company && <NotificationBell />}
          <span className="muted">{user?.userName}</span>
          <button className="btn small" onClick={logout}>Sign out</button>
        </div>
        <main className="content">
          {!company ? (
            <div className="note error">Your account has no company access. Ask an administrator to grant it.</div>
          ) : (
            <Routes>
              <Route path="/" element={<Dashboard />} />
              <Route path="/customers" element={<Customers />} />
              <Route path="/items" element={<Items />} />
              <Route path="/orders" element={<Orders />} />
              <Route path="/orders/new" element={<NewOrder />} />
              <Route path="/orders/:id" element={<OrderDetail />} />
              <Route path="/invoices" element={<Invoices />} />
              <Route path="/invoices/:id" element={<InvoiceDetail />} />
              <Route path="/vendors" element={<Vendors />} />
              <Route path="/tolerance-requests" element={<ToleranceRequests />} />
              <Route path="/purchase-orders" element={<PurchaseOrders />} />
              <Route path="/purchase-orders/new" element={<NewPurchaseOrder />} />
              <Route path="/purchase-orders/:id" element={<PurchaseOrderDetail />} />
              <Route path="/bills" element={<Bills />} />
              <Route path="/bills/:id" element={<BillDetail />} />
              <Route path="/finance" element={<Finance />} />
              <Route path="/admin" element={<Admin />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          )}
        </main>
      </div>
    </div>
  )
}

export function App() {
  const { ready, user } = useAuth()
  if (!ready) return <div className="login"><Loading /></div>
  return (
    <Routes>
      <Route path="/login" element={user ? <Navigate to="/" replace /> : <Login />} />
      <Route path="/*" element={user ? <Layout /> : <Navigate to="/login" replace />} />
    </Routes>
  )
}
