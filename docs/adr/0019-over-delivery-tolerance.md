# ADR 0019: Over-delivery quantity tolerance

## Status

Accepted

## Context

Goods receipts (ADR 0013) were limited to the outstanding quantity, so a vendor who delivered slightly more than ordered (bulk goods,
pack sizes) could not be received without amending the order. Warehouses then either refused good stock or edited orders to fit.

## Decision

- A **vendor** and an **item** each carry an optional `QuantityTolerancePercent`, 0–100. It works like the price tolerance (ADR 0018):
  **the item's, else the vendor's, else 0%** (`ThreeWayMatch.EffectiveQuantityTolerance`). 0 is a real setting; blank defers.
  The company default is **no over-delivery**, so existing behaviour is unchanged.
- The ceiling for a line is `ordered × (1 + tolerance)`, checked against everything received so far, so several receipts
  cannot add up to more than the ceiling (`ThreeWayMatch.MaxReceivable`, `PurchaseOrderLine.ReceivableWithTolerance`).
- The tolerance is resolved **at receipt time** and stored on each receipt line (`QuantityTolerancePercent`), as a record of the rule
  the delivery was accepted under. Existing receipt lines were backfilled with 0.
- Over-delivered goods are received at the order price: stock and the GRNI accrual (Dr Inventory / Cr GRNI) cover the full delivered quantity.
- Billing already followed received goods, so over-delivered units are billable and the price tolerance (ADR 0018) applies to them as usual.
  An order that has received at least the ordered quantity on every line is `Received`.
- "Receive everything" with no lines still receives only the outstanding quantity; accepting extra is always an explicit choice of quantity.
- `OutstandingToReceive` never goes negative; the order detail response carries each line's tolerance so the receive dialog can show the maximum.
- Rejections state the limit, for example "only 10.5 outstanding including the 5% over-delivery tolerance".
- Setting a tolerance needs the existing permissions to manage vendors or items; no new permission.

## Consequences

- Whoever can edit a vendor or item can allow over-receipt, so treat vendor and item management as a control over receiving.
- Only the receiving side is tolerant: there is no under-delivery tolerance (use *Close short*), and no per-order override.
- Update requests replace the tolerance: omitting it clears it.
