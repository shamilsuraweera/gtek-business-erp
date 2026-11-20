# ADR 0016: Configurable account mapping

## Status

Accepted

## Context

Sales and Purchasing posted to hard-coded account codes (1100, 4000, 2100, …). A company with a different chart of accounts could not use
the system without renumbering its ledger, and nothing checked that a code was a sensible account for what was being posted.

## Decision

Business modules post by **role**, not by code. `AccountRole` (Cash, Bank, AccountsReceivable, Inventory, AccountsPayable,
GoodsReceivedNotInvoiced, SalesRevenue, SalesReturns, CostOfGoodsSold, PurchasePriceVariance) lives in `Finance.Contracts`;
`PostJournalLine.ForRole(...)` carries it. Finance resolves a role to a ledger account **per company**:

1. the company's `AccountMapping` row for the role, if any; otherwise
2. the role's **default code** (the codes the system always used), so nothing needs configuring and existing data keeps working.

Rules:

- Each role accepts only suitable account types (e.g. Accounts payable → liability; Sales returns → revenue or expense, being contra-revenue).
  Inactive accounts cannot be mapped, and a mapped or default-in-use account cannot be deactivated.
- A role with no usable account fails the posting with a message naming the role and where to fix it; the multi-module compensation of
  ADR 0002 undoes anything already done (stock is returned), so a half-configured company never gets half-posted documents.
- Mappings affect **new postings only**. Posted entries are immutable (ADR 0003) and stay on the account they were posted to; the UI warns
  when a role being remapped already has postings.
- Mapping is per company and audited (`account-mapping.changed` / `.reset`); manual journals still use explicit account codes.
- Managing the mapping needs `finance.account-mapping.manage`; viewing it needs `finance.accounts.read`.

Also in this change: the API now syncs the permission catalogue to the `SYSTEM_ADMIN` role **at startup**, so a release that adds permissions
never locks the system administrator out of the new features. It is data only (never schema), and a failure is logged, not fatal.

## Consequences

- Dashboard figures sum the currently mapped account and the role's default code (deduplicated), so a single remap does not make
  history disappear from the totals. Repeated remaps of the same role leave older accounts out of those dashboard totals; the trial
  balance is always complete.
- The chart of accounts is still seeded with the default codes; a company with its own chart maps each role once.
- Not covered: effective-dated mappings (switching accounts mid-period with automatic reclassification), per-item or per-customer
  accounts (e.g. revenue by product line), and tax accounts.
