# ADR 0013: Purchasing partial receipts and three-way match

## Status

Accepted

## Context

Purchase orders were received and billed in full, in one step. Real deliveries arrive in parts, vendors
under-deliver, and vendor invoices rarely match the order price exactly. Paying a bill that does not agree with what
was ordered and what arrived is the classic accounts-payable control failure.

## Decision

A purchase order is matched against **goods receipts** (what arrived) and **vendor bills** (what the vendor charged).

- Each order line tracks `QuantityReceived` and `QuantityBilled`. The order status follows from them:
  `Released → PartiallyReceived ⇄ Received → Billed`. Receiving more than is outstanding is rejected.
- Every delivery is its own **GoodsReceipt** with its own journal. Stock is added at the **order price**:
  `Dr Inventory / Cr Goods Received Not Invoiced (GRNI)`.
- **Close short** accepts a partial delivery as final. An order with received goods cannot be cancelled.
- A **vendor bill** may cover any part of the received-and-unbilled quantity, and an order may have several bills.
  - *Quantity leg:* a bill cannot exceed received minus already billed.
  - *Price leg:* the billed unit cost may differ from the order price by at most the line's tolerance: the item's own, else the
    vendor's, else **2%** (`ThreeWayMatch.PriceTolerancePercent`). See ADR 0018.
    Beyond that the bill is rejected unless the caller passes `overrideVariance` **and** holds
    `purchasing.bills.override-variance`. An approved override is recorded on the bill and in the audit trail.
  - *Posting:* `Dr GRNI` at the order price (clearing exactly what receipts accrued), `Dr/Cr Purchase Price Variance (5100)`
    for the difference, `Cr Accounts Payable` at the billed amount.
- A vendor invoice number can be entered once per vendor (compared case-insensitively).
- Order lines are keyed by item: an item may appear once per order, so receipts and bills refer to items, not line positions.

## Consequences

- GRNI always equals received-not-billed quantity at order prices; payables equal unpaid bills; the variance account equals
  the sum of bill variances. These invariants are verified against a live database in the integration checks.
- Inventory is valued from its own ledger (receipts at order price, issues at standard cost), so it reconciles with the GL.
- Modules still own separate databases (ADR 0002), so a failed multi-module step compensates earlier steps
  (stock returned, journal reversed) instead of using one transaction.
- Over-delivery tolerance arrived in ADR 0019. Per-vendor and per-item price tolerance arrived in ADR 0018; returns to vendor and tax followed in ADR 0015 and ADR 0017.
- Migration `PartialReceiptsThreeWayMatch` backfills a single receipt for each previously received order. The original journal
  *number* is preserved; the journal *id* was never stored and the migration must not read the Finance schema, so legacy
  receipts carry an empty journal id.
