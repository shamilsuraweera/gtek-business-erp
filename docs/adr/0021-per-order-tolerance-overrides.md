# ADR 0021: Per-order tolerance overrides

## Status

Accepted

## Context

Price, over-delivery and under-delivery tolerances (ADR 0018, 0019, 0020) are set per vendor and per item. A one-off purchase often
needs different rules: a spot buy with a volatile price, a bulk delivery that will certainly run over, a contract that must match
exactly. Changing the vendor or item for that would affect every other order.

## Decision

- Each **purchase order line** carries three optional overrides: `PriceTolerancePercent`, `QuantityTolerancePercent` (over-delivery) and
  `UnderDeliveryTolerancePercent`, 0–100. The tolerance that applies is now
  **order line, else item, else vendor, else company default** (`ThreeWayMatch.Effective*Tolerance(item, vendor, orderLine)`).
  0 is a real setting; blank defers to the next level.
- Overrides can be set when the order is created and changed or cleared later with `PUT /api/v1/purchasing/orders/{id}/tolerances`,
  which replaces the overrides of the lines it lists (a null clears that override; unlisted lines are untouched). They can change
  until the order is cancelled or fully billed, since a released order is exactly when a supplier's delivery turns out different.
- Because overrides loosen the matching controls, they need a **new permission**, `purchasing.orders.override-tolerance`
  ("Override order tolerances"). Without it, creating an order with overrides is rejected and the endpoint returns 403.
  Managing orders alone is not enough; the demo AP clerk does not have it.
- The price tolerance is still **stored on each bill line** and the over/under tolerances on each receipt line, so changing an override
  never rewrites issued documents (ADR 0018, 0019, 0020).
- Changing an under-delivery override **re-evaluates the order at once**: a part-received order whose lines are all complete under the
  new tolerances becomes `Received`, as if closed short. Changing a vendor or item tolerance does not (ADR 0020).
- Every change is audited as `order.tolerances.overridden`, with each line's old and new values and the resulting status.
- Order responses carry both the effective tolerance and the override itself per line, so the UI can mark "order override".

## Consequences

- The permission is the control point: anyone holding it can loosen matching for one order without touching vendor or item master data.
  Grant it to approvers, not to clerks. The audit trail records who changed what.
- Overrides are per line, not per order: setting the same value on every line is done line by line in the UI.
- Lowering an over-delivery override below what has already been received simply blocks further receipts; nothing is reversed.
- Users without the permission can ask for an override instead (ADR 0022).
- Not covered: overrides by order header and expiry of an override.
