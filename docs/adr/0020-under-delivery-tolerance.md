# ADR 0020: Under-delivery tolerance

## Status

Accepted

## Context

An order with any shortfall stayed `PartiallyReceived` until someone pressed *Close short* (ADR 0013), even when the vendor delivered
99% of a bulk or weighed item and nobody expected the rest. Those orders piled up as "open" and needed a manual step each time.

## Decision

- A **vendor** and an **item** each carry an optional `UnderDeliveryTolerancePercent`, 0–100: **the item's, else the vendor's, else 0%**
  (`ThreeWayMatch.EffectiveUnderDeliveryTolerance`), with the same "0 is a real setting, blank defers" rule as ADR 0018 and 0019.
  The default is exact quantities, so existing behaviour is unchanged.
- A line is **complete** once received ≥ ordered × (1 − tolerance) (`PurchaseOrderLine.IsComplete`). When every line is complete after
  a receipt, the order becomes `Received` on its own, exactly as if it had been closed short; no further receipts are accepted.
- The shortfall is not accrued or billed: receipts, accruals (Dr Inventory / Cr GRNI) and bills follow the goods that arrived.
  The order line keeps its ordered quantity, so the shortfall stays visible (for example 9.5 of 10).
- The tolerance is resolved at receipt time and stored on each receipt line (`UnderDeliveryTolerancePercent`); existing lines were backfilled with 0.
  It is evaluated on every receipt, so a later delivery can complete a line that was short beyond tolerance.
- It is independent of the over-delivery tolerance (ADR 0019): a line can accept both, and the over-delivery ceiling is unaffected.
- *Close short* remains for shortfalls beyond the tolerance. The receive dialog shows "complete from N" for each line.
- Setting a tolerance needs the existing permissions to manage vendors or items; no new permission.

## Consequences

- Changing a tolerance does not re-evaluate orders already partially received; their status updates at the next receipt or *Close short*.
- Whoever can edit a vendor or item can decide how short a delivery may be before an order closes, so treat vendor and item management
  as a control over receiving.
- Not covered: tolerance by absolute quantity, per-order overrides, and automatically cancelling an unreceived shortfall.
