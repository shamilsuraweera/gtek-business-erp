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
import { PurchaseOrders } from './pages/PurchaseOrders'
import { NewPurchaseOrder } from './pages/NewPurchaseOrder'
import { PurchaseOrderDetail } from './pages/PurchaseOrderDetail'
import { Bills } from './pages/Bills'
import { BillDetail } from './pages/BillDetail'
import { Finance } from './pages/Finance'
import { Admin } from './pages/Admin'

function Layout() {
  const { user, company, companies, can, logout, selectCompany } = useAuth()
  const link = (to: string, label: string, allowed: boolean) =>
    allowed ? <NavLink to={to} end={to === '/'}>{label}</NavLink> : null

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">GTEK <span>ERP</span></div>
        <nav className="nav" aria-label="Main">
          {link('/', 'Dashboard', true)}
          <div className="nav-group">Sales</div>
          {link('/orders', 'Sales orders', can(P.ordersRead))}
          {link('/invoices', 'Invoices & payments', can(P.invoicesRead))}
          {link('/customers', 'Customers', can(P.customersRead))}
          <div className="nav-group">Purchasing</div>
          {link('/purchase-orders', 'Purchase orders', can(P.poRead))}
          {link('/bills', 'Bills & payments', can(P.billsRead))}
          {link('/vendors', 'Vendors', can(P.vendorsRead))}
          <div className="nav-group">Inventory</div>
          {link('/items', 'Items & stock', can(P.itemsRead))}
          <div className="nav-group">Finance</div>
          {link('/finance', 'Ledger & reports', can(P.reportsRead) || can(P.journalsRead) || can(P.accountsRead))}
          <div className="nav-group">System</div>
          {link('/admin', 'Administration', can(P.periodsRead) || can(P.usersRead) || can(P.auditRead))}
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
