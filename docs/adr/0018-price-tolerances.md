# ADR 0018: Price tolerances per vendor and per item

## Status

Accepted

## Context

The three-way match (ADR 0013) accepted a billed price within one global 2% of the purchase order price and blocked anything
further unless an approver overrode it. Vendors and items differ: a commodity item bought from a volatile supplier needs headroom,
while a contracted item must match exactly.

## Decision

- A **vendor** (Purchasing) and an **item** (Inventory) each carry an optional `PriceTolerancePercent`, 0–100 with 4 decimals.
  Blank means "no opinion"; **0 is a real setting** meaning exact prices only.
- The tolerance for a bill line is resolved when the bill is entered: **the item's, else the vendor's, else the 2% company default**
  (`ThreeWayMatch.EffectiveTolerance`). The more specific rule wins, in either direction: an item may be stricter or looser than its vendor.
- Purchasing reads the item's tolerance through `IInventoryCatalog` (`ItemInfo.PriceTolerancePercent`); no module reaches into another's tables.
- The tolerance applied is **stored on each bill line** (`TolerancePercent`), like the tax rate in ADR 0017. Editing a tolerance later never
  changes an issued bill's "Within tolerance" or "Variance approved" status. Existing bill lines were backfilled with 2%, the rule they were matched under.
- The override (`overrideVariance` plus `purchasing.bills.override-variance`) is unchanged and applies beyond whichever tolerance is effective.
- Purchase order detail responses include each line's effective tolerance, so the bill dialog shows the rule the server will apply
  without needing vendor or item read access. Rejections name the item and the tolerance that was applied.
- Setting a tolerance needs the existing permissions to manage vendors or items; no new permission was added.

## Consequences

- Tolerance can only be loosened by someone who can edit a vendor or item, which is a wider group than the bill approvers. Treat
  vendor and item management as a control over matching.
- Update requests replace the tolerance: omitting it clears it, as with an item's tax code.
- Not covered: tolerance by absolute amount and per-order overrides. Over-delivery tolerance is ADR 0019.
