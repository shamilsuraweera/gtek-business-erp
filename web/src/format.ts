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
