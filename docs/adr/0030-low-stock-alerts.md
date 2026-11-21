# ADR 0030: Low-stock alerts (scheduled email)

## Status

Accepted

## Context

ADR 0029 added scheduled reminders for things that happen with time. Running out of stock is another: it is noticed late, and
the dashboard's "Low stock" card used a fixed rule (8 or fewer of anything), which is wrong for a laptop and for a box of cable alike.

## Decision

- An item gets an optional **reorder level** (`inventory.Items.ReorderLevel`, `numeric(19,4)`, null by default). It is the on-hand
  quantity **at or below which** the item counts as low. **Null means the item is not monitored**; `0` is a real level
  ("tell me when it runs out"). It cannot be negative. It is set on the item form (create and edit, audited) and returned on items and on stock levels.
- Only **active, monitored** items count. On-hand is the total across locations, from the same stock ledger as everything else.
- `LowStockReminder` (an `IReminderJob`, Inventory) uses the machinery of ADR 0029 unchanged: one summary per company per period, the same
  `Reminders` configuration (`HourUtc`, `RepeatDays`, ...), a newer summary replaces the older one, and it clears itself when nothing is low.
  The category and subject are `low-stock`; the message is **emailed**, goes to holders of `inventory.stock.manage` (who receive and adjust stock),
  and links to `/items`. It gives the count, how many are out of stock, and the most urgent item (furthest below its level) with its quantity and level.
- Being a category, `low-stock` gets the per-category email opt-out (ADR 0028) and per-category retention (ADR 0027) without extra work.
- The stock list reports `reorderLevel` and `isLow`, and the web app uses them: the Items page shows a "Reorder at" column and a warning badge,
  and the dashboard's Low stock card lists items at or below their level. **An item with no reorder level keeps the old rule (8 or fewer)
  on screen only**, so the dashboard does not change for installations that set no levels; those items are never emailed about.

## Consequences

- Nothing is emailed until someone sets reorder levels; an installation that does not want alerts sets none.
- Existing items default to not monitored; the demo seeder sets levels on a few items so the demo shows an alert.
- With the default `RepeatDays = 1`, a low item is emailed about daily until restocked; a user can opt out of the category.
- The people told are those who manage stock, not purchasers: there is no link to a vendor or an automatic purchase order.
- The level is per item, not per location, and does not account for stock already on order.
- Not covered: per-location levels, suggested order quantities, alerts when stock is about to run out (forecasting), and alerts for stock going negative.
