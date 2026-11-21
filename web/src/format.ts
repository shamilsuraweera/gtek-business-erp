const money = new Intl.NumberFormat('en-LK', { style: 'currency', currency: 'LKR', maximumFractionDigits: 2 })
const number = new Intl.NumberFormat('en-LK', { maximumFractionDigits: 4 })

export const formatMoney = (value: number) => money.format(value)
export const formatNumber = (value: number) => number.format(value)

export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value.length === 10 ? `${value}T00:00:00` : value)
  return date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })
}

export const today = () => new Date().toISOString().slice(0, 10)

/**
 * Low stock: at or below the item's reorder level; for an item without one, running low on an item that has actually been stocked
 * (8 or fewer, the dashboard's long-standing rule). Items never stocked, such as services or newly created ones, are not "low".
 */
export const isLowStock = (s: { onHand: number; reorderLevel: number | null; isLow: boolean; everStocked: boolean }) =>
  (s.reorderLevel == null ? s.everStocked && s.onHand <= 8 : s.isLow)
