# ADR 0014: Sales credit notes, returns and refunds

## Status

Accepted

## Context

Invoices could not be corrected. Customers return goods, receive damaged stock, or cancel after delivery, and an invoice
that has already been paid cannot simply be edited (ADR 0003: posted history is never changed, only reversed or adjusted).

## Decision

A **credit note** is issued against one invoice. Its lines reference items on that invoice.

- *Limits:* the quantity credited per item can never exceed the quantity invoiced minus quantities on earlier credit notes.
  Items are unique per sales order, so an item identifies exactly one invoice line.
- *Posting:* `Dr Sales Returns & Allowances (4100) / Cr Accounts Receivable` for the credited amount at the invoiced price.
  Revenue stays gross; 4100 is a contra-revenue account and the dashboard reports net revenue.
- *Returns vs allowances:* each line says whether the goods came back. A returned line re-enters stock **at the cost it left at**
  (the invoice line's issue cost) and posts `Dr Inventory / Cr Cost of Goods Sold`, so inventory and COGS reverse exactly.
  An allowance (customer keeps the goods) credits the customer only.
- *Applying:* the credit first reduces the invoice's open balance (`AmountCredited`). Any excess is **refundable**:
  `RefundDue = Total − AmountApplied − AmountRefunded`.
- A **refund** pays that excess back: `Dr Accounts Receivable / Cr Cash or Bank`, limited to `RefundDue`, in one or more parts.
- Invoice status: `Open → Paid`, or `Credited` when credit alone cleared the whole invoice. A credited invoice cannot be paid or credited again.
- Credits and payments on the same invoice share one lock, so they cannot race; refunds lock per credit note.
- As elsewhere (ADR 0002), a failed step compensates earlier ones: the journal is reversed and returned stock is issued back.

## Consequences

- Receivable = Σ invoice balances − refunds still owed; Sales Returns = Σ credit notes; inventory still equals the stock ledger.
  These invariants are verified against a live database.
- Credits are always at the invoiced price. Partial price adjustments (credit less than full price) and credits that are not tied to
  an invoice are not supported yet.
- Returned goods are not inspected or quarantined; they go straight to the main location's available stock.
