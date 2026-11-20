# ADR 0015: Purchasing returns to vendor, debit notes and vendor refunds

## Status

Accepted

## Context

Received goods are sometimes faulty or wrong and go back to the vendor, before or after the vendor's bill was entered (ADR 0013).
Posted history is never edited (ADR 0003), and the goods-received accrual (GRNI), payables and stock must stay consistent.

## Decision

Two documents with different jobs, so the accounting is the same however far the order had progressed:

1. **Return to vendor** (`RTV`) is the physical movement. Goods leave stock **at the cost they came in at** (the order price) and the
   receipt accrual is reversed: `Dr GRNI / Cr Inventory`. It is limited to goods still held (received − returned); a return does not
   re-open the order (replacements need a new order), and goods that are no longer in stock cannot be returned.
2. **Debit note** (`DN`) is the vendor's credit for **billed** goods that were returned. It references a specific bill and credits the
   vendor's **billed** price: `Dr Accounts Payable (billed price) / Cr GRNI (order price)`, with the difference to Purchase Price Variance.

Each order line tracks `QuantityReturned` and `QuantityDebited`. Derived rules:

- held = received − returned; owed-for = billed − debited
- *billable* = max(0, held − owed-for)  (so goods returned before being billed are simply not billed)
- *debitable* = max(0, owed-for − held)  (billed goods that were returned and await the vendor's credit)

A debit note can never exceed the debitable quantity, nor what the bill charged (less earlier debit notes against it).

The credit first reduces the bill's open balance (`AmountDebited`); any excess means **the vendor owes us**
(`RefundDue = Total − AmountApplied − AmountRefunded`) and is settled with a **vendor refund**: `Dr Cash or Bank / Cr Accounts Payable`.
A bill cleared by debit notes alone has status `Debited`.

Concurrency: a debit note changes both the order's quantities and the bill's balance, so it takes the order lock **and** the bill lock
(acquired in sorted order, so concurrent callers cannot deadlock); returns take the order lock, payments the bill lock, vendor refunds a per-note lock.
As elsewhere (ADR 0002), failed steps are compensated (journal reversed, stock restored).

## Consequences

- Invariants checked against a live database: GRNI = goods held at order price − billed at order price + debited at order price;
  payables = bill balances − refunds owed by vendors; price variance = bill variances − debit-note variances; inventory = stock ledger.
- Returns and debit notes mirror sales credit notes and refunds (ADR 0014) so users see one pattern on both sides.
- Not covered: partial price credits (a debit is always for returned goods at the billed price), returns routed through a separate
  quarantine location, and vendor-side restocking fees.
