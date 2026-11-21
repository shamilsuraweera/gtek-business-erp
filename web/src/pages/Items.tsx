import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth'
import { useApiMutation, useApiQuery } from '../hooks'
import { P } from '../perm'
import { formatDate, formatMoney, formatNumber, today } from '../format'
import { Badge, Empty, ErrorNote, Field, Loading, Modal, PageHeader } from '../ui'
import { formatRate } from '../tax'
import type { Item, StockLedgerEntry, StockLevel, TaxCode } from '../api/types'

type Dialog = { kind: 'new' } | { kind: 'edit' | 'receive' | 'adjust' | 'ledger'; item: Item } | null

function ItemForm({ item, onClose }: { item?: Item; onClose: () => void }) {
  const [sku, setSku] = useState(item?.sku ?? '')
  const [name, setName] = useState(item?.name ?? '')
  const [uom, setUom] = useState(item?.unitOfMeasure ?? 'EA')
  const [price, setPrice] = useState(String(item?.unitPrice ?? ''))
  const [cost, setCost] = useState(String(item?.unitCost ?? ''))
  const [taxCode, setTaxCode] = useState(item?.taxCode ?? '')
  const [tolerance, setTolerance] = useState(item?.priceTolerancePercent == null ? '' : String(item.priceTolerancePercent))
  const toleranceValue = tolerance.trim() === '' ? null : Number(tolerance)
  const [qtyTolerance, setQtyTolerance] = useState(item?.quantityTolerancePercent == null ? '' : String(item.quantityTolerancePercent))
  const qtyToleranceValue = qtyTolerance.trim() === '' ? null : Number(qtyTolerance)
  const [underTolerance, setUnderTolerance] = useState(item?.underDeliveryTolerancePercent == null ? '' : String(item.underDeliveryTolerancePercent))
  const underToleranceValue = underTolerance.trim() === '' ? null : Number(underTolerance)
  const { can } = useAuth()
  const taxCodes = useApiQuery<TaxCode[]>('/finance/tax-codes', { enabled: can(P.accountsRead) })
  // Keep the item's current code selectable even if it was deactivated since.
  const options = (taxCodes.data ?? []).filter((t) => t.isActive || t.code === item?.taxCode)
  const save = useApiMutation<void>(
    () => item
      ? { path: `/inventory/items/${item.id}`, method: 'PUT', body: { name, unitOfMeasure: uom, unitPrice: Number(price), unitCost: Number(cost), taxCode: taxCode || null, priceTolerancePercent: toleranceValue, quantityTolerancePercent: qtyToleranceValue, underDeliveryTolerancePercent: underToleranceValue } }
      : { path: '/inventory/items', body: { sku, name, unitOfMeasure: uom, unitPrice: Number(price), unitCost: Number(cost), taxCode: taxCode || null, priceTolerancePercent: toleranceValue, quantityTolerancePercent: qtyToleranceValue, underDeliveryTolerancePercent: underToleranceValue } },
    onClose,
  )
  return (
    <Modal title={item ? `Edit ${item.sku}` : 'New item'} onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          {!item && <Field label="SKU"><input value={sku} onChange={(e) => setSku(e.target.value)} required autoFocus /></Field>}
          <Field label="Name"><input value={name} onChange={(e) => setName(e.target.value)} required /></Field>
          <Field label="Unit of measure"><input value={uom} onChange={(e) => setUom(e.target.value)} required maxLength={20} /></Field>
          <Field label="Tax code" hint="Prices exclude tax; this code adds it when the item is invoiced or billed.">
            <select value={taxCode} onChange={(e) => setTaxCode(e.target.value)} disabled={!can(P.accountsRead)}>
              <option value="">No tax</option>
              {options.map((t) => <option key={t.code} value={t.code}>{t.code} — {t.name} ({formatRate(t.ratePercent)}){t.isActive ? '' : ' — inactive'}</option>)}
            </select>
          </Field>
          <Field label="Selling price"><input type="number" min={0} step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} required /></Field>
          <Field label="Standard cost" hint="Used to value stock issues (COGS)."><input type="number" min={0} step="0.01" value={cost} onChange={(e) => setCost(e.target.value)} required /></Field>
          <Field label="Price tolerance (%)" hint="How far a vendor's billed price may differ from the order for this item. Blank defers to the vendor's tolerance, then the company default (2%); 0 means exact prices only.">
            <input type="number" min={0} max={100} step="0.01" value={tolerance} placeholder="Default" onChange={(e) => setTolerance(e.target.value)} />
          </Field>
          <Field label="Over-delivery tolerance (%)" hint="How far above the ordered quantity a delivery of this item may go. Blank defers to the vendor's tolerance, then none; 0 means no over-delivery at all.">
            <input type="number" min={0} max={100} step="0.01" value={qtyTolerance} placeholder="Default" onChange={(e) => setQtyTolerance(e.target.value)} />
          </Field>
          <Field label="Under-delivery tolerance (%)" hint="How far below the ordered quantity a delivery of this item still counts as complete. Blank defers to the vendor's tolerance, then exact quantities; 0 means the full quantity is always required.">
            <input type="number" min={0} max={100} step="0.01" value={underTolerance} placeholder="Default" onChange={(e) => setUnderTolerance(e.target.value)} />
          </Field>
        </div>
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={save.isPending}>Save</button>
        </div>
      </form>
    </Modal>
  )
}

function StockForm({ item, mode, onClose }: { item: Item; mode: 'receive' | 'adjust'; onClose: () => void }) {
  const [quantity, setQuantity] = useState('')
  const [unitCost, setUnitCost] = useState(String(item.unitCost))
  const [text, setText] = useState('')
  const [date, setDate] = useState(today())
  const save = useApiMutation<void>(
    () => mode === 'receive'
      ? { path: '/inventory/stock/receipts', body: { itemId: item.id, quantity: Number(quantity), unitCost: Number(unitCost), date, reference: text || null } }
      : { path: '/inventory/stock/adjustments', body: { itemId: item.id, quantity: Number(quantity), reason: text, date } },
    onClose,
  )
  return (
    <Modal title={`${mode === 'receive' ? 'Receive stock' : 'Adjust stock'} — ${item.sku}`} onClose={onClose}>
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label={mode === 'receive' ? `Quantity (${item.unitOfMeasure})` : 'Quantity change (+/−)'} hint={mode === 'adjust' ? 'Use a negative number to remove stock.' : undefined}>
            <input type="number" step="any" value={quantity} onChange={(e) => setQuantity(e.target.value)} required autoFocus />
          </Field>
          {mode === 'receive' && <Field label="Unit cost"><input type="number" min={0} step="0.01" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} required /></Field>}
          <Field label="Date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
          <Field label={mode === 'receive' ? 'Reference (optional)' : 'Reason'}>
            <input value={text} onChange={(e) => setText(e.target.value)} required={mode === 'adjust'} />
          </Field>
        </div>
        <p className="hint">Stock ledger only. Post a matching journal in Finance if the receipt should change the general ledger.</p>
        <ErrorNote error={save.error} />
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn primary" disabled={save.isPending}>Post</button>
        </div>
      </form>
    </Modal>
  )
}

function LedgerDialog({ item, onClose }: { item: Item; onClose: () => void }) {
  const ledger = useApiQuery<StockLedgerEntry[]>(`/inventory/stock/ledger?itemId=${item.id}&take=100`)
  return (
    <Modal title={`Stock ledger — ${item.sku}`} onClose={onClose} wide>
      {ledger.isLoading ? <Loading /> : (ledger.data ?? []).length === 0 ? <Empty>No movements yet.</Empty> : (
        <div className="scroll-x"><table className="data">
          <thead><tr><th>Date</th><th>Type</th><th>Reference</th><th className="num">Qty</th><th className="num">Unit cost</th></tr></thead>
          <tbody>{ledger.data!.map((e) => (
            <tr key={e.id}><td>{formatDate(e.postingDate)}</td><td>{e.sourceType}</td><td>{e.sourceReference}</td>
              <td className="num" style={{ color: e.quantity < 0 ? 'var(--bad)' : undefined }}>{e.quantity > 0 ? '+' : ''}{formatNumber(e.quantity)}</td>
              <td className="num">{formatMoney(e.unitCost)}</td></tr>
          ))}</tbody>
        </table></div>
      )}
    </Modal>
  )
}

export function Items() {
  const { can } = useAuth()
  const items = useApiQuery<Item[]>('/inventory/items')
  const stock = useApiQuery<StockLevel[]>('/inventory/stock', { enabled: can(P.stockRead) })
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const toggle = useApiMutation<Item>((i) => ({ path: `/inventory/items/${i.id}/${i.isActive ? 'deactivate' : 'activate'}` }))
  const close = () => setDialog(null)

  const level = (id: string) => stock.data?.find((s) => s.itemId === id)
  const rows = (items.data ?? []).filter((i) => `${i.sku} ${i.name}`.toLowerCase().includes(search.toLowerCase()))
  const totalValue = (stock.data ?? []).reduce((sum, s) => sum + s.value, 0)

  return (
    <>
      <PageHeader title="Items & stock" subtitle={stock.data ? `Stock value ${formatMoney(totalValue)}` : undefined}
        actions={can(P.itemsManage) && <button className="btn primary" onClick={() => setDialog({ kind: 'new' })}>New item</button>} />
      <div className="row" style={{ marginBottom: 12 }}>
        <input className="search" placeholder="Search SKU or name…" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search items" />
      </div>
      <ErrorNote error={items.error ?? toggle.error} />
      {items.isLoading ? <Loading /> : (
        <div className="card flush scroll-x">
          {rows.length === 0 ? <Empty>No items match.</Empty> : (
            <table className="data">
              <thead><tr><th>SKU</th><th>Name</th><th className="num">Price</th><th className="num">Cost</th><th>Tax</th><th className="num">Price tol.</th><th className="num">Over-delivery</th><th className="num">Under-delivery</th><th className="num">On hand</th><th className="num">Value</th><th>Status</th><th /></tr></thead>
              <tbody>
                {rows.map((item) => {
                  const s = level(item.id)
                  return (
                    <tr key={item.id}>
                      <td>{item.sku}</td><td>{item.name}</td>
                      <td className="num">{formatMoney(item.unitPrice)}</td><td className="num">{formatMoney(item.unitCost)}</td>
                      <td>{item.taxCode ?? <span className="muted">—</span>}</td>
                      <td className="num">{item.priceTolerancePercent == null ? <span className="muted">—</span> : `${item.priceTolerancePercent}%`}</td>
                      <td className="num">{item.quantityTolerancePercent == null ? <span className="muted">—</span> : `${item.quantityTolerancePercent}%`}</td>
                      <td className="num">{item.underDeliveryTolerancePercent == null ? <span className="muted">—</span> : `${item.underDeliveryTolerancePercent}%`}</td>
                      <td className="num">{s ? <Badge value={`${formatNumber(s.onHand)} ${item.unitOfMeasure}`} tone={s.onHand <= 0 ? 'bad' : s.onHand <= 8 ? 'warn' : 'neutral'} /> : '—'}</td>
                      <td className="num">{s ? formatMoney(s.value) : '—'}</td>
                      <td><Badge value={item.isActive ? 'Active' : 'Inactive'} /></td>
                      <td><div className="row" style={{ justifyContent: 'flex-end' }}>
                        {can(P.stockRead) && <button className="btn small" onClick={() => setDialog({ kind: 'ledger', item })}>Ledger</button>}
                        {can(P.stockManage) && <button className="btn small" onClick={() => setDialog({ kind: 'receive', item })}>Receive</button>}
                        {can(P.stockManage) && <button className="btn small" onClick={() => setDialog({ kind: 'adjust', item })}>Adjust</button>}
                        {can(P.itemsManage) && <button className="btn small" onClick={() => setDialog({ kind: 'edit', item })}>Edit</button>}
                        {can(P.itemsManage) && <button className="btn small" onClick={() => toggle.mutate(item)}>{item.isActive ? 'Deactivate' : 'Activate'}</button>}
                      </div></td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>
      )}
      {dialog?.kind === 'new' && <ItemForm onClose={close} />}
      {dialog?.kind === 'edit' && <ItemForm item={dialog.item} onClose={close} />}
      {dialog?.kind === 'receive' && <StockForm item={dialog.item} mode="receive" onClose={close} />}
      {dialog?.kind === 'adjust' && <StockForm item={dialog.item} mode="adjust" onClose={close} />}
      {dialog?.kind === 'ledger' && <LedgerDialog item={dialog.item} onClose={close} />}
    </>
  )
}
