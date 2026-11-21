# ADR 0022: Approval workflow for tolerance overrides

## Status

Accepted

## Context

ADR 0021 let anyone holding `purchasing.orders.override-tolerance` set per-order tolerance overrides directly. Everyone else was simply
refused, so a buyer or AP clerk who needed a looser tolerance for one order had to find an approver and ask outside the system. There was
no record of the ask, the reason, or who agreed.

## Decision

- Anyone who can manage orders (`purchasing.orders.manage`) can **request** an override on an order: one or more lines with the
  overrides they should end up with (a null clears that override) and a **mandatory reason**. A request is a new aggregate,
  `ToleranceRequest` (number `TOL-nnnnn`), with status `Pending`, `Approved`, `Rejected` or `Withdrawn`.
- An approver (the holder of `purchasing.orders.override-tolerance`, now described as "Approve order tolerance overrides") **approves
  or rejects**. A rejection needs a reason; an approval may carry a note. Nothing about the order changes until approval.
- **Approval applies the overrides** through the same step as the direct path (`OrderTolerances.ApplyAsync`), including the immediate
  re-check that can complete a part-received order (ADR 0021). It runs under the order's lock with the order re-read at that moment, so
  it cannot race a receipt, a bill or another decision. If the order can no longer take overrides (cancelled or fully billed since the
  request), approval fails, nothing changes and the request stays pending so it can be rejected.
- **Four eyes:** nobody approves or rejects their own request. An approver who wants a change for themselves uses the direct path
  (which is audited) or asks another approver. Only the requester can **withdraw** a pending request.
- **One open request per order.** A second request is refused until the first is decided or withdrawn, enforced in the service and by
  a partial unique index on pending requests.
- Requests are not allowed on cancelled or fully billed orders.
- Pending requests show the current override next to the requested value, so the approver sees exactly what would change. In the UI
  a request carries only the lines that differ from the current overrides.
- Every step is audited (`order.tolerance-request.created / approved / rejected / withdrawn`) with the order, reason and note.
  Names for the requester and decider come from a new `IUserDirectory` contract implemented by Platform, so Purchasing never reads user tables.
- The order page lists the order's requests and offers the right action to each user; a **Tolerance requests** page lists pending
  requests across orders for approvers (and everyone's own history).

## Consequences

- The direct path remains for approvers, so the workflow controls the people who lack the permission, not the approvers themselves.
  Both paths are audited, but only requests record a separate reason.
- Pending requests are not time-limited and nobody is notified; approvers find them in the inbox.
- Not covered: multi-level or amount-based approval chains, delegation, expiry, notifications, and requesting overrides at order creation.
