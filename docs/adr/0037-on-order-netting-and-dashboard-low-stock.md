# ADR 0037: Low-stock alerts net out what is on order; the dashboard stops counting never-stocked items

## Status

Accepted. Refines ADR 0030.

## Context

A low-stock alert for an item whose replenishment is already on its way is noise, and people stop reading noise. Separately, the dashboard's
Low stock card treated any item without a reorder level and with 8 or fewer on hand as low, which includes items that were created but never
stocked (services, new products), and the card shows only six items, so these crowded out real ones.

## Decision

- **On-order netting.** `IOnOrderQuantities` (in `Erp.Application.Abstractions`, implemented by Purchasing) gives, per item, the quantity still to
  arrive on **released** and **part-received** purchase orders (ordered less received). Drafts and confirmed-but-unreleased orders do not count
  (the vendor has not been asked), nor do cancelled or fully received ones. `LowStockReminder` includes an item only if it is low now **and still
  low when what is on order is added**; the summary says so for the worst item ("has 2 EA left (+4 on order), reorder level 10").
- If everything low is covered by orders, nothing is sent and the old summary clears.
- The stock list (Items page badge) still reflects **on-hand only**: the screen shows the situation now, the alert asks whether anyone needs to act.
- **Dashboard.** Stock levels carry `everStocked` (whether the item has any ledger movement). An item **with** a reorder level is low when on hand is
  at or below it (unchanged). An item **without** one is low only if it has been stocked and has 8 or fewer. The card shows "+N more" with a link when
  more than six items are low.

## Consequences

- A sold-out item that was never given a reorder level and was never stocked does not appear; give an item a level to be told about it.
- On-order counts the full outstanding quantity of released orders, regardless of the expected delivery date (the orders have none).
- Not covered: per-location reorder levels (a level per warehouse changes how items are configured), expected delivery dates, and suggested order quantities.
