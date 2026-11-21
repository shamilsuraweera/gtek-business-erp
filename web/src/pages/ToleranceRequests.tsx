import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import type { ToleranceRequest } from '../api/types'

const TONE: Record<ToleranceRequest['status'], string> = { Pending: 'warn', Approved: 'ok', Rejected: 'bad', Withdrawn: 'neutral' }
const pct = (v: number | null) => (v == null ? '—' : `${v}%`)

function RejectDialog({ request, onClose }: { request: ToleranceRequest; onClose: () => void }) {
  const [note, setNote] = useState('')
  const reject = useApiMutation<void>(() => ({ path: `/purchasing/tolerance-requests/${request.id}/reject`, body: { note } }), onClose)
  return (
    <Modal title={`Reject ${request.number}`} onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); reject.mutate() }}>
        <Field label="Reason for rejecting"><textarea value={note} onChange={(e) => setNote(e.target.value)} required maxLength={500} autoFocus rows={3} /></Field>
        <ErrorNote error={reject.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn danger" disabled={!note.trim() || reject.isPending}>Reject request</button>
        </div>
      </form>
    </Modal>
  )
}

/** One request, with what it would change and the actions the signed-in user may take. */
function RequestCard({ request, showOrder }: { request: ToleranceRequest; showOrder: boolean }) {
  const { can, user } = useAuth()
  const [rejecting, setRejecting] = useState(false)
  const approve = useApiMutation<void>(() => ({ path: `/purchasing/tolerance-requests/${request.id}/approve`, body: {} }))
  const withdraw = useApiMutation<void>(() => ({ path: `/purchasing/tolerance-requests/${request.id}/withdraw`, body: {} }))
  const pending = request.status === 'Pending'
  const own = user?.id === request.requestedById
  return (
    <div className="card" style={{ marginBottom: 12 }}>
      <div className="spread">
        <div>
          <strong>{request.number}</strong> <Badge value={request.status} tone={TONE[request.status]} />
          {showOrder && <> · <Link to={`/purchase-orders/${request.purchaseOrderId}`}>{request.orderNumber}</Link> <span className="muted">{request.vendorName}</span></>}
          <div className="muted">Requested by {request.requestedBy} on {formatDate(request.requestedAt)}</div>
        </div>
        {pending && (
          <div className="row">
            {can(P.poOverrideTolerance) && !own && <>
              <button className="btn primary small" disabled={approve.isPending} onClick={() => approve.mutate()}>Approve</button>
              <button className="btn small" onClick={() => setRejecting(true)}>Reject</button>
            </>}
            {own && <button className="btn small" disabled={withdraw.isPending} onClick={() => withdraw.mutate()}>Withdraw</button>}
          </div>
        )}
      </div>
      <p style={{ margin: '8px 0' }}>{request.reason}</p>
      <div className="scroll-x"><table className="data">
        <thead><tr><th>Item</th><th className="num">Price</th><th className="num">Over-delivery</th><th className="num">Under-delivery</th></tr></thead>
        <tbody>{request.lines.map((l) => (
          <tr key={l.itemId}>
            <td>{l.sku}<div className="muted">{l.description}</div></td>
            <td className="num">{pending && l.currentPriceOverride !== l.priceTolerancePercent && <span className="muted">{pct(l.currentPriceOverride)} → </span>}{pct(l.priceTolerancePercent)}</td>
            <td className="num">{pending && l.currentQuantityOverride !== l.quantityTolerancePercent && <span className="muted">{pct(l.currentQuantityOverride)} → </span>}{pct(l.quantityTolerancePercent)}</td>
            <td className="num">{pending && l.currentUnderDeliveryOverride !== l.underDeliveryTolerancePercent && <span className="muted">{pct(l.currentUnderDeliveryOverride)} → </span>}{pct(l.underDeliveryTolerancePercent)}</td>
          </tr>
        ))}</tbody>
      </table></div>
      {pending && <p className="hint">A dash means no override: the item, vendor or company default applies.</p>}
      {request.decidedBy && <p className="muted" style={{ marginTop: 8 }}>{request.status} by {request.decidedBy}{request.decidedAt ? ` on ${formatDate(request.decidedAt)}` : ''}{request.decisionNote ? ` — ${request.decisionNote}` : ''}</p>}
      <ErrorNote error={approve.error ?? withdraw.error} />
      {rejecting && <RejectDialog request={request} onClose={() => setRejecting(false)} />}
    </div>
  )
}

export function ToleranceRequestList({ requests, showOrder = true }: { requests: ToleranceRequest[]; showOrder?: boolean }) {
  return <>{requests.map((r) => <RequestCard key={r.id} request={r} showOrder={showOrder} />)}</>
}

export function ToleranceRequests() {
  const [status, setStatus] = useState<'Pending' | 'all'>('Pending')
  const requests = useApiQuery<ToleranceRequest[]>(`/purchasing/tolerance-requests${status === 'Pending' ? '?status=Pending' : ''}`)
  return (
    <>
      <PageHeader title="Tolerance requests" subtitle="Requests to override price and delivery tolerances on a purchase order. Approvers decide them; nobody decides their own." />
      <div className="row" style={{ marginBottom: 12 }}>
        <select value={status} onChange={(e) => setStatus(e.target.value as 'Pending' | 'all')} aria-label="Status filter">
          <option value="Pending">Pending</option>
          <option value="all">All requests</option>
        </select>
      </div>
      <ErrorNote error={requests.error} />
      {requests.isLoading ? <Loading /> : (requests.data ?? []).length === 0
        ? <div className="card"><Empty>{status === 'Pending' ? 'No requests are waiting for a decision.' : 'No tolerance requests yet.'}</Empty></div>
        : <ToleranceRequestList requests={requests.data ?? []} />}
    </>
  )
}
