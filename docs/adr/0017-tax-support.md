# ADR 0017: Tax support

## Status

Accepted

## Context

Invoices and vendor bills carried no tax: every price was the amount owed. A real company charges output tax on sales and pays
input tax on purchases, must report the net to a tax authority, and must keep credit notes and debit notes consistent with the tax
originally charged.

## Decision

**Prices stay tax-exclusive.** Tax is added when a document that posts to the ledger is issued; orders and purchase orders show
amounts before tax.

- **Tax codes** (`TaxCode`: code, name, rate 0–100%, active) belong to Finance and are per company. Other modules read them through
  `ITaxCatalog` in `Finance.Contracts`; they never see Finance tables (ADR 0002).
- **Items** carry an optional tax code (Inventory). No code means no tax; a code with rate 0 is zero-rated. A code must exist and be
  active when assigned.
- **Tax point is the invoice or bill.** The item's code and rate are read when the invoice or bill is created and stored on each line
  (`TaxCode`, `TaxRate`, `TaxAmount`). Changing a rate later affects only new documents; posted documents keep what they were issued
  at. An item whose code has since gone missing or inactive blocks invoicing/billing with a message naming the item.
- **Tax is rounded per line** (`TaxMath.Tax`, half away from zero). The document's tax is the sum of its line taxes, so what is
  printed equals what is posted. `Total = Subtotal + TaxTotal` and the customer/vendor balance is the gross total.
- **Two new posting roles** (ADR 0016): `TaxPayable` (liability, default 2200 "Output tax payable") and `TaxReceivable` (asset, default
  1300 "Input tax recoverable"). They are mapped like any other role; a company that has not created those accounts gets an actionable
  error only when it first posts a taxed document, and the existing compensation leaves nothing half-posted.

Postings (tax lines appear only when tax > 0):

| Document | Entries |
|---|---|
| Sales invoice | Dr Receivable (gross) · Cr Revenue (net) · Cr Output tax · and, unchanged, Dr COGS / Cr Inventory |
| Credit note | Dr Sales returns (net) · Dr Output tax · Cr Receivable (gross) · and, unchanged, Dr Inventory / Cr COGS for returned goods |
| Vendor bill | Dr GRNI (order price) · Dr/Cr Price variance · Dr Input tax · Cr Payable (gross) |
| Debit note | Dr Payable (gross) · Cr GRNI (order price) · Cr Input tax · Dr/Cr Price variance reversal |

Price variance and the three-way match tolerance work on **net** amounts; tax never counts as variance.

**Credits give back exactly the tax that was charged.** A credit or debit line takes its share of the original line's tax
(`TaxMath.Portion`): proportional to the quantity, and the credit that completes the line takes whatever remains. Three single-unit
credits of a line taxed 0.80 therefore return 0.27 + 0.27 + 0.26, and a fully credited invoice always lands on a zero balance.

**Reporting.** `GET /api/v1/finance/tax-summary?from&to` reports output tax, input tax and the net payable for a period, read from the
ledger accounts the two roles map to (credit notes and debit notes netted off). Managing tax codes needs `finance.tax-codes.manage`;
reading them needs `finance.accounts.read`.

## Consequences

- Existing data is untouched: the migrations only add columns (defaulting to no tax), so every earlier document and balance is
  unchanged, and items without a code behave exactly as before.
- One output and one input account per company. A company that must report several tax types separately (e.g. VAT and a levy) would need
  per-code accounts; the code and rate are already stored per line, so adding that later does not change history.
- Prices are exclusive only; there is no tax-inclusive pricing, withholding tax, or tax by customer or vendor status (exemptions).
- A manual journal can still post to the tax accounts; the summary reflects whatever is in those accounts.
