# GTEK Business ERP: user and operator guide

How to run, use and administer the application. For a quick start see the [README](../README.md); for what is and is not built see
[STATUS](STATUS.md); for the reasoning behind decisions see the [decision records](adr/README.md).

## Contents

1. [What the application is](#1-what-the-application-is)
2. [Running it](#2-running-it)
3. [Signing in and finding your way](#3-signing-in-and-finding-your-way)
4. [Working with the screens](#4-working-with-the-screens)
5. [Notifications, email and reminders](#5-notifications-email-and-reminders)
6. [Administration](#6-administration)
7. [Roles and permissions](#7-roles-and-permissions)
8. [Using the API directly](#8-using-the-api-directly)
9. [Databases and migrations](#9-databases-and-migrations)
10. [Troubleshooting](#10-troubleshooting)

## 1. What the application is

A multi-company ERP: a .NET 10 API over PostgreSQL and a React web client. It handles two business cycles end to end:

- **Order to cash:** customer, sales order, invoice (the stock leaves and the ledger is posted in one step), payment; credit notes, returns to stock and refunds.
- **Procure to pay:** vendor, purchase order, goods receipts, vendor bill matched to the order and receipts, payment; returns to vendor, debit notes and vendor refunds.

Around them: a chart of accounts, journals and a trial balance; tax codes; stock levels; multiple companies with per-user access; roles and
permissions; an audit trail; and notifications, email and scheduled reminders that tell people what needs them.

The ledger is **append-only**: posted entries are never edited, and a mistake is corrected with a reversal. Money is exact (`decimal`), and the trial
balance must always balance.

## 2. Running it

Follow the [README quick start](../README.md#quick-start): create the database, configure the API, run `bash scripts/dev-setup.sh --seed`, start the
API on port 5004 and the web app on port 5173, then open http://localhost:5173.

The API never changes the database schema by itself; migrations are applied by `scripts/dev-setup.sh` (see [section 9](#9-databases-and-migrations)).
To start over with fresh demo data, drop and recreate the database and run the script again.

## 3. Signing in and finding your way

Sign in with a user name and password. If you have access to more than one company, choose it from **Company** in the top bar; everything you see
afterwards belongs to that company. The demo has one company and two users:

| User | Password | Role |
|---|---|---|
| `admin` | `Admin#123456` | Administrator: everything, including approving tolerance requests |
| `apclerk` | `Clerk#1234567` | Accounts-payable clerk: purchasing day-to-day, cannot open Finance or Administration |

The left menu shows only the areas your role allows, and buttons you cannot use are hidden. If you can reach something by address but lack the
permission, the server refuses it (403). The bell in the top bar shows what needs your attention.

## 4. Working with the screens

### Dashboard

Cash and bank, receivables, overdue invoices, payables, inventory value, net revenue, tax payable and gross profit; the order pipeline; receivables
ageing; recent invoices; and a **Low stock** card. Items with a reorder level are listed when at or below it; items without one when they have been
stocked and have 8 or fewer. The card shows the six lowest and "+N more" with a link.

### Sales

- **Customers:** code, name, contact and payment terms.
- **Sales orders:** create a draft, add lines, *Confirm order*, *Release for invoicing*, then *Create invoice*. Invoicing issues the stock, posts the
  receivable, revenue, tax and cost of goods sold, and cannot oversell.
- **Invoices and payments:** record full or partial payments (overpaying is refused). Overdue invoices are flagged. A **credit note** returns goods to
  stock at their original cost and reverses revenue, tax and cost; a **refund** pays the customer back against a credit note.

### Purchasing

- **Vendors:** terms and an optional price, over-delivery and under-delivery tolerance.
- **Purchase orders:** draft, confirm, release, then **receive goods** (partial deliveries are fine; stock goes in at the order cost with an accrual
  for goods received not yet invoiced). *Close short* ends an order that will not be completed.
- **Bills:** enter the vendor's bill against an order. It is matched to the order and what was received: a billed price within the **price tolerance**
  (2% by default; per vendor, item or order line) is accepted and marked "within tolerance"; beyond it the bill is refused unless a user who may approve
  variances overrides it. The difference is posted to purchase price variance. Pay bills in full or in part.
- **Tolerances and approval:** over- and under-delivery follow tolerances too. Someone with the *approve order tolerance overrides* permission can set them
  on an order line directly. Anyone who manages orders can instead **Request tolerance change** with a reason; the request is listed under
  **Tolerance requests**, a **different** person approves or rejects it (nobody decides their own), and only one request can be open per order.
- **Returns, debit notes and vendor refunds** reverse received goods and billed amounts.

### Inventory

**Items and stock:** each item has a SKU, price, cost, unit, tax code, optional tolerances and an optional **reorder level** (the on-hand quantity at or
below which people who manage stock are told; blank means not monitored). *Receive* adds stock, *Adjust* corrects it, *Ledger* shows every movement.
Stock is valued from the ledger, so it always agrees with the general ledger.

### Finance (Ledger and reports)

The trial balance (with a "Balanced" check), the chart of accounts, journals and reversals, **Account mapping** (which ledger account each posting role uses) and
**Tax** (tax codes and the net payable for a period). Prices are tax-exclusive: an item's tax code adds output tax on invoices and input tax on bills,
and credit and debit notes return the tax that was charged.

## 5. Notifications, email and reminders

### The bell

Click the bell in the top bar. Unread messages are highlighted; clicking one opens the page it is about and marks it read; **Mark all read** clears
the count. The panel also holds your email switches and a line telling you how long messages are kept, including any categories that follow a different rule.

### What is sent, and to whom

| Event | Who is told | Category | Email |
|---|---|---|---|
| A tolerance change is requested | People who can approve order tolerance overrides, except the requester | approval | yes |
| A request is approved or rejected | The requester | decision | yes |
| Invoices are past their due date | People who can record customer payments | overdue-invoice | yes |
| Vendor bills are past their due date | People who can pay vendor bills | overdue-bill | yes |
| Items are at or below their reorder level (and not covered by released purchase orders) | People who manage stock | low-stock | yes |
| Accounting periods have ended but are still open | People who manage accounting periods | period-close | yes |
| Your own account is created or deactivated, or your company access or roles change | You, by email only | account | always |

The scheduled ones (overdue, low stock, period close) are **one summary per company per day**, sent from the configured hour, replacing the previous
summary, and clearing themselves when the cause is resolved.

### Email

Email is **off until configured** (README, Configuration). When on, messages are queued in an outbox and sent by a background worker with retries, so a
mail-server outage never makes an approval or a bill fail. **Your preferences** (bell panel): a master switch, and one checkbox per category. In-app
messages are unaffected by these. **Security notices** (the `account` row above) ignore your preferences; they say so in the email and are not listed as choices.
Administrators see what is waiting, sent and failed under *Administration → Notifications*.

### Retention

Read messages are deleted 30 days after being read and unread ones 180 days after being sent, by default. Under *Administration → Notifications* a
company can change both, and set its own rule for a single category (for example keep decisions longer than approval requests). Blank inherits; 0 unread days keeps
unread messages forever. Changes take effect at the next daily purge.

## 6. Administration

Open **Administration** (needs the relevant permissions).

- **Accounting periods:** postings are accepted only into an open period. *Close* a period when its postings are complete (a reminder tells you when one has ended
  but is still open); *Lock* a closed period to make it permanent; *Reopen* a closed one if you must.
- **Number sequences:** per-company document numbers (prefix, padded number, suffix); issuing is safe across several API instances; gaps may occur and numbers are never reused.
- **Users:** create users, activate and deactivate them, grant or revoke access to a company, and assign global roles. The person is emailed about each change to their own access.
- **Audit log:** an append-only history of administrative and security actions, filterable by date, actor, company, category, action and outcome.
- **Notifications:** retention for the company and per category, and the email delivery status.

## 7. Roles and permissions

Users receive **global roles**; roles hold **permissions** of the form `module.resource.action` (for example `purchasing.payments.record`). Permissions are looked up
on the server on every request and are not copied into the sign-in token, so changing a role takes effect immediately. **Company access** is separate:
a user can act in a company only if access was granted, and the administrator role has no implicit access to every company. Inactive users and roles authorise nothing.

The first (bootstrap) administrator holds `SYSTEM_ADMIN`, which receives every permission, including new ones introduced by later versions (granted at API start-up).
Useful endpoints: `GET /api/v1/permissions`, `GET|POST /api/v1/roles`, `POST|DELETE /api/v1/roles/{id}/permissions/{code}`, `POST|DELETE /api/v1/users/{id}/roles/{roleId}`,
`POST|DELETE /api/v1/users/{id}/companies/{companyId}`.

## 8. Using the API directly

Everything the web app does goes through the API under `/api/v1`. The OpenAPI document is at `/openapi/v1.json` and `src/Host/Erp.Api/Erp.Api.http` has ready-made requests.

```bash
# sign in
TOKEN=$(curl -s -X POST http://localhost:5004/api/v1/auth/login -H 'Content-Type: application/json' \
  -d '{"userName":"admin","password":"Admin#123456"}' | python3 -c 'import sys,json; print(json.load(sys.stdin)["accessToken"])')

# find your companies (no company header needed for this call)
curl -s http://localhost:5004/api/v1/auth/me/companies -H "Authorization: Bearer $TOKEN"

# company-scoped calls need the company id too
curl -s http://localhost:5004/api/v1/finance/trial-balance -H "Authorization: Bearer $TOKEN" -H "X-Company-Id: <company-id>"
```

`401` means not signed in, `403` means signed in without the permission, `400` carries a readable `detail` explaining a rule that was broken (for example "Payment exceeds the invoice balance").

The **first user** is created once with the bootstrap secret: `POST /api/v1/users` with `X-Bootstrap-Secret: <Authentication:BootstrapSecret>`. After that, users are managed by administrators.
`scripts/smoke/smoke.py` is a good worked example of driving the whole API.

## 9. Databases and migrations

EF Core migrations are authoritative and each module has its own schema and history table (`platform`, `finance`, `inventory`, `sales`, `purchasing`). They are applied with
`bash scripts/dev-setup.sh` (or `dotnet ef database update` per module), never automatically at start-up. A database created by the old SQL bootstrap needs a backup and the
adoption script in `scripts/database` (read-only check first, then `-Apply`); see [ADR 0012](adr/0012-database-migration-authority.md). To add a migration:

```bash
dotnet ef migrations add <Name> --startup-project src/Host/Erp.Api --project src/Modules/<Module>/Erp.Modules.<Module>.Infrastructure \
  --context <Module>DbContext --output-dir Persistence/Migrations      # with ERP_DATABASE_CONNECTION set
```

Docker alternative: `docker compose up -d postgres` (PostgreSQL 16, user `gtek`).

## 10. Troubleshooting

| Symptom | Check |
|---|---|
| API will not start and prints a list of problems | An invalid `Email`, `Reminders` or `Notifications:Retention` setting; the message names each one |
| "Connection refused" or "database does not exist" | PostgreSQL is running on 5432; the `gtek` role and `gtek_erp` database exist (`scripts/dev-db.sql`); the connection string matches |
| Pages fail after an update | New migrations: run `bash scripts/dev-setup.sh` |
| Port 5004 or 5173 already in use | Stop the other process, or run the API with `--urls http://localhost:5050` (and set `VITE_API_TARGET` for the web app) |
| Reminders do not appear | They run once about two minutes after start and then hourly, from the configured hour (`Reminders:Hour`, in `Reminders:TimeZone`); each is sent at most once a day |
| No emails | `Email:Enabled` is false by default; check *Administration → Notifications* for pending or failed messages and the last error; your own email switch and category checkbox |
| 403 on a screen or action | The user's role lacks the permission, or no access was granted to the company |
| "Insufficient stock" on invoicing | The ledger is the authority; receive stock first or adjust it (with a reason) |
| A bill is refused for price | It exceeds the price tolerance in effect for that line; request a tolerance change or have an approver override |

Run `scripts/smoke/run.sh` to check that everything works together on a throwaway database; if it passes, the problem is in your environment or data.
