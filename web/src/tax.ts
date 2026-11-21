/** Share of a line's tax for `qty` of its `lineQty` units, given earlier credits; the completing credit takes the remainder. Mirrors the API's TaxMath.Portion. */
export function taxPortion(lineTax: number, lineQty: number, qtyBefore: number, taxBefore: number, qty: number): number {
  const remaining = lineTax - taxBefore
  if (qtyBefore + qty >= lineQty) return remaining
  const share = Math.round((lineTax * qty) / lineQty * 100) / 100
  return Math.min(Math.max(share, 0), remaining)
}

export const round2 = (value: number) => Math.round(value * 100) / 100

export const formatRate = (rate: number) => `${Number(rate.toFixed(4))}%`
