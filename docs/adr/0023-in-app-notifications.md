# ADR 0023: In-app notifications

## Status

Accepted

## Context

The tolerance request workflow (ADR 0022) left approvers to discover pending requests by opening the inbox, and requesters to
come back and look for a decision. There was no way for any module to tell a user that something needs their attention.

## Decision

- Platform owns a small notification store: `Notification` (company, user, category, title, body, link, optional **subject**, created and
  read times), table `platform.Notifications`. Notifications are per user and per company; a user only ever sees their own.
- Other modules send them through a contract in `Erp.Application.Abstractions`, **`INotifier`**, implemented by Platform, in the same style as
  `IAuditRecorder` and `IUserDirectory`:
  - `NotifyUsersAsync(userIds, message)`: the named users, if they have active access to the active company.
  - `NotifyPermissionHoldersAsync(permission, message, exceptUserId)`: every active user of the active company who holds the permission,
    except the actor. Roles are global in this system, so the recipients are the company's users filtered by permission.
  - `ResolveSubjectAsync(subject)`: marks every notification about a subject read for everyone.
- Tolerance requests use it: a new request notifies the approvers ("needs a decision", linking to the inbox); approval or rejection
  notifies the requester with the decider and note (linking to the order) and **resolves** the request's subject, so the approvers' "needs a
  decision" messages are marked read the moment anyone decides, and withdrawal resolves them too. Nobody is notified of their own action.
  A decision that fails (for example an approval on a cancelled order) sends nothing.
- Notifications are sent **after** the business operation commits and are **best effort**: a delivery failure is logged and never fails or
  undoes the request or the decision. The permission code Purchasing notifies is a constant there that a unit test keeps equal to Platform's.
- API: `GET /api/v1/notifications` (`unread`, `take`), `GET /unread-count`, `POST /{id}/read`, `POST /read-all`. Any signed-in user, company
  context required, scoped to the caller. Marking someone else's notification returns 404.
- UI: a bell in the top bar with an unread badge (polled every 30 seconds), a panel of the latest 20 with "Mark all read", unread items
  highlighted, and a click that marks the item read and opens its link. Escape and clicking outside close the panel.

## Consequences

- Delivery is pull-based (polling), not pushed: a new message can take up to 30 seconds to show, and nothing reaches a user who is not signed in.
  There is no email or mobile push.
- The panel shows the newest 20. Old notifications are purged by the retention policy (ADR 0024).
- Recipients are decided when the message is sent: a user granted the permission later does not receive earlier requests as notifications,
  although they still see them in the inbox.
- Per-user preferences (muting categories) are not covered.
