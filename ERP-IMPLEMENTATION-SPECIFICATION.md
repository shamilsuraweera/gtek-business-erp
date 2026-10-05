# GTEK Business ERP

## Structured Product and Implementation Specification

**Status:** Baseline implementation specification  
**Version:** 1.0  
**Date:** 2026-10-05

## 1. Purpose and product boundaries

This document defines the staged delivery plan for a modular business ERP. It is
intended to guide product decisions, domain modeling, implementation sequencing,
testing, and release acceptance. Each phase produces a usable capability and
must meet its exit criteria before the next phase starts.

The ERP is a multi-company system for operational, commercial, accounting,
inventory, project, manufacturing, service, CRM, and HR workflows. It must
preserve an auditable history of business events and accounting entries.

### 1.1 Product principles

- A posted financial or inventory transaction is immutable. Corrections use
  documented reversal, adjustment, or credit/debit mechanisms.
- Money uses fixed-precision decimal types and explicit currency and rounding
  rules. Floating-point types are prohibited for monetary values.
- Business rules live in application/domain services and use-case handlers, not
  controllers or EF Core entities merely for convenience.
- Modules expose contracts and application services rather than coupling directly
  to another module's tables.
- Every business record is scoped to a company/tenant; company identifiers are
  resolved from authenticated context, never hard-coded.
- Audit history records who performed an action, when, under which company, and
  the before/after business state where appropriate.
- A modular monolith is the default deployment architecture. Microservices,
  distributed messaging, Kubernetes, and other operational complexity are
  deferred until a demonstrated requirement exists.
- Generic CRUD is appropriate for simple reference data, not for financial,
  inventory, or other invariant-heavy transactions.

### 1.2 Common lifecycle vocabulary

Documents generally move through `Draft`, `Submitted`, `Approved`, `Posted`,
`Partially fulfilled`, `Completed`, `Cancelled`, or `Reversed`, as applicable.
The exact state machine is owned by each module. Transitions are explicit,
authorized, validated, and audited.

## 2. Delivery and architecture standards

### 2.1 Standard module boundaries

The initial modular monolith should contain bounded modules similar to:

- Platform and identity
- Organization and master data
- General ledger
- Inventory
- Sales
- Purchasing
- Warehouse
- Fixed assets
- Cash and banking
- Projects
- Manufacturing
- Service management
- CRM
- HR
- Reporting and integrations

Each module owns its schema/tables and publishes application-level contracts.
Cross-module reads use query services, projections, or explicit integration
contracts. No module may update another module's tables directly.

### 2.2 Standard transaction controls

For every posting-capable aggregate:

1. Validate company, period, currency, permissions, and required dimensions.
2. Validate the current state and optimistic concurrency token.
3. Create the business transaction and its immutable posting/event record in one
   database transaction.
4. Derive ledger, inventory, tax, or commitment effects from the transaction.
5. Write an audit entry and an idempotency key where an external retry is
   possible.
6. Return a durable transaction identifier and resulting status.

Failures must be visible to the caller and logged with a correlation identifier;
there must be no silent success-shaped fallback.

### 2.3 Cross-cutting quality gates

Every phase must include:

- Unit tests for invariants and calculations.
- Application/service tests for authorization and state transitions.
- Persistence tests for constraints, transactions, and concurrency.
- API/contract tests for public endpoints and validation errors.
- At least one representative end-to-end workflow.
- Security tests for tenant isolation and role enforcement.
- Migration review, seed-data review, and rollback/recovery notes.
- Auditability verification for create, modify, approve, post, reverse, and
  cancel operations where those actions exist.

## 3. Phase roadmap

## Phase 1 — Platform, organization, and master data

### Objectives

- Establish the secure application foundation and modular monolith structure.
- Support users, companies, branches, fiscal calendars, currencies, tax basics,
  units of measure, numbering, and configurable permissions.
- Provide the shared master-data services required by all later modules.

### Dependencies

- Product decisions for company/tenant isolation, authentication provider,
  supported currencies, time zones, and fiscal-year policy.
- Database, migration, deployment, logging, and secret-management strategy.
- No functional ERP phase is a prerequisite.

### Major domain objects

- `Company`, `Branch`, `Address`, `Contact`
- `User`, `Role`, `Permission`, `UserCompanyAccess`
- `FiscalYear`, `AccountingPeriod`, `Currency`, `ExchangeRate`
- `TaxCode`, `TaxRate`, `UnitOfMeasure`, `UomConversion`
- `NumberingSeries`, `DocumentSequence`
- `AuditEntry`, `IdempotencyRecord`, `Attachment`

### Key use cases

- Create and configure a company and its branches.
- Invite users and assign company-scoped roles.
- Open, close, and lock accounting periods.
- Configure currencies, exchange rates, taxes, units, and numbering series.
- Resolve authenticated company context and reject cross-company access.
- Record and search auditable changes.

### Tests required

- Authentication, authorization, role inheritance, and tenant-isolation tests.
- Period open/close/lock state tests, including concurrent requests.
- Currency precision, exchange-rate effective-date, tax, and UOM conversion tests.
- Number-sequence uniqueness and retry/idempotency tests.
- Migration tests from an empty database and a representative seeded database.
- API validation and audit-log contract tests.

### Explicit exit criteria

- A user can sign in, select an authorized company, and access only that
  company's data.
- Fiscal periods, currencies, taxes, UOMs, and document numbering can be
  configured through supported application workflows.
- A closed period rejects postings and records the rejection.
- All public write operations have authorization, validation, audit, and
  correlation identifiers.
- Automated tests pass for tenant isolation and the shared platform invariants.

## Phase 2 — General ledger and accounting foundation

### Objectives

- Deliver a reliable double-entry general ledger.
- Establish the chart of accounts, journals, dimensions, posting rules, and
  period controls that all financial modules will use.
- Make reporting balances reproducible from immutable posted entries.

### Dependencies

- Phase 1 company, currency, fiscal-period, permission, numbering, and audit
  capabilities.
- Approved accounting policy, chart-of-accounts template, tax treatment, and
  financial dimensions.

### Major domain objects

- `Account`, `AccountGroup`, `AccountType`
- `Journal`, `JournalEntry`, `JournalLine`
- `AccountingDimension`, `DimensionValue`, `AccountCombination`
- `PostingProfile`, `PostingRule`
- `LedgerTransaction`, `LedgerTransactionLine`
- `CurrencyRevaluation`, `TrialBalanceSnapshot`

### Key use cases

- Configure and version a chart of accounts.
- Enter, validate, approve, post, and reverse journal entries.
- Enforce balanced debits and credits in transaction currency and base currency.
- Require dimensions and validate account combinations.
- Query account activity, trial balance, and period balances.
- Revalue foreign-currency balances and record resulting entries.
- Close and reopen periods according to an authorized policy.

### Tests required

- Double-entry balancing, decimal precision, rounding, and exchange-rate tests.
- Account-type and posting-profile validation tests.
- Journal state-machine and approval segregation-of-duties tests.
- Period lock, reversal, duplicate-submission, and concurrency tests.
- Trial-balance and account-balance reconciliation tests against known fixtures.
- Tenant, role, audit, and immutable-posting tests.

### Explicit exit criteria

- A balanced journal entry can be posted and appears in account activity and
  trial balance reports.
- An unbalanced, invalid, duplicate, unauthorized, or period-locked posting is
  rejected with a specific error.
- Posted ledger entries cannot be edited or silently deleted.
- Reversal creates a new linked transaction and preserves the original.
- Trial balance balances for all seeded and end-to-end test scenarios.

## Phase 3 — Products, services, pricing, and inventory foundation

### Objectives

- Establish item and service master data and the initial inventory model.
- Support warehouses, locations, stock policies, standard costs, and basic
  pricing needed by sales and purchasing.
- Define the boundaries between operational stock movements and accounting
  postings.

### Dependencies

- Phases 1 and 2.
- Decisions on costing method, stock valuation timing, negative-stock policy,
  item tracking, and warehouse structure.

### Major domain objects

- `Item`, `ItemVariant`, `Service`
- `ItemCategory`, `Brand`, `Barcode`
- `Warehouse`, `Location`, `Bin`
- `InventoryPolicy`, `ReorderPolicy`
- `PriceList`, `PriceListLine`, `DiscountRule`
- `CostProfile`, `StandardCost`, `ItemTaxProfile`
- `StockBalance`, `StockReservation`

### Key use cases

- Create sellable, purchasable, stocked, non-stocked, and service items.
- Configure item units, tax treatment, price lists, warehouses, and locations.
- Set or approve a standard cost and effective date.
- View on-hand, reserved, available, and reorder quantities.
- Reserve and release stock without bypassing availability rules.
- Validate product, customer, vendor, and tax references for later documents.

### Tests required

- Item classification, UOM, barcode, tax, and price-list validation tests.
- Warehouse/location hierarchy and company-isolation tests.
- Reservation, release, availability, and concurrent-reservation tests.
- Cost effective-date and currency/rounding tests.
- Read-model reconciliation tests for balances and reservations.
- Authorization, audit, and API contract tests.

### Explicit exit criteria

- Master data required by sales, purchasing, and inventory can be configured.
- Stock and reservation balances are queryable by company, warehouse, location,
  and item.
- Cost and pricing rules produce deterministic results for approved fixtures.
- No later module needs to write directly to another module's tables.

## Phase 4 — Sales

### Objectives

- Support the quote-to-cash process from customer setup through invoicing and
  customer ledger integration.
- Provide traceability from quote to order, shipment, invoice, receipt, and
  accounting effects.

### Dependencies

- Phases 1–3.
- Phase 2 ledger posting contracts.
- Tax, credit-limit, payment-term, and document-approval policies.

### Major domain objects

- `Customer`, `CustomerGroup`, `CustomerAddress`, `CustomerContact`
- `SalesQuote`, `SalesQuoteLine`
- `SalesOrder`, `SalesOrderLine`
- `Shipment`, `ShipmentLine`, `DeliveryConfirmation`
- `SalesInvoice`, `SalesInvoiceLine`, `CreditNote`
- `CustomerLedgerAccount`, `CustomerPayment`, `CustomerAllocation`
- `SalesPrice`, `SalesDiscount`, `SalesTaxDetail`

### Key use cases

- Create and approve quotes with prices, discounts, taxes, and validity dates.
- Convert an accepted quote to an order while preserving source links.
- Check credit limits and reserve stock on an order.
- Fulfil an order fully or partially through shipment confirmation.
- Invoice shipped, ordered, or service lines according to policy.
- Post receivable, tax, revenue, cost-of-goods, and inventory effects.
- Record customer payments and allocate them to invoices.
- Issue credit notes and reverse or adjust invoice effects without editing
  posted invoices.
- View customer balance, ageing, order status, and fulfilment status.

### Tests required

- Quote-to-order conversion and source-traceability tests.
- Price, discount, tax, currency, UOM, and rounding tests.
- Credit-limit and approval segregation tests.
- Partial shipment, backorder, cancellation, and reservation tests.
- Invoice posting, credit-note, customer-payment, and allocation tests.
- Ledger and inventory reconciliation tests for end-to-end sales scenarios.
- Duplicate webhook/import/idempotency and tenant-isolation tests.

### Explicit exit criteria

- An approved quote can become an order without losing pricing or audit history.
- An order can be partially shipped and invoiced with accurate remaining
  quantities.
- A posted invoice creates the expected customer receivable and ledger entries.
- Customer payments can be allocated and customer balances reconcile to the
  general ledger.
- Posted sales documents are immutable; corrections use supported adjustment
  documents.
- At least one complete quote-to-cash workflow passes in automated end-to-end
  tests.

## Phase 5 — Purchasing

### Objectives

- Support the procure-to-pay process from vendor setup through receipts,
  invoices, payments, and vendor ledger integration.
- Provide three-way matching and clear ownership of purchasing, inventory, and
  accounting effects.

### Dependencies

- Phases 1–3 and Phase 2 ledger contracts.
- Vendor, approval, purchasing tolerance, tax, and payment policies.

### Major domain objects

- `Vendor`, `VendorGroup`, `VendorAddress`, `VendorContact`
- `PurchaseRequest`, `PurchaseRequestLine`
- `PurchaseOrder`, `PurchaseOrderLine`
- `Receipt`, `ReceiptLine`, `ReturnToVendor`
- `VendorInvoice`, `VendorInvoiceLine`, `DebitNote`
- `VendorLedgerAccount`, `VendorPayment`, `VendorAllocation`
- `MatchResult`, `PurchasePrice`, `PurchaseTaxDetail`

### Key use cases

- Create and approve purchase requests and purchase orders.
- Enforce vendor, price, tax, currency, approval, and tolerance rules.
- Receive goods fully or partially and record damaged/short quantities.
- Match vendor invoices to purchase orders and receipts.
- Post inventory, expense, tax, accrual, and payable effects.
- Record vendor payments and allocate them to invoices.
- Return goods and issue debit/adjustment documents without editing posted
  records.
- View vendor balance, ageing, open orders, and receipt variances.

### Tests required

- Approval, price, tax, currency, and tolerance tests.
- Partial receipt, over-receipt, under-receipt, return, and damaged-stock tests.
- Two-way and three-way matching tests, including deliberate mismatch cases.
- Vendor invoice posting, payment, allocation, and debit-note tests.
- Payables-to-general-ledger and inventory reconciliation tests.
- Idempotency, authorization, audit, and tenant-isolation tests.

### Explicit exit criteria

- An approved purchase order can be received partially or fully with traceable
  quantities and variances.
- Vendor invoices can be matched and either accepted or rejected with explicit
  reasons.
- A posted vendor invoice creates the expected payable and ledger effects.
- Vendor payments reconcile to open payable balances and the general ledger.
- Posted purchasing documents remain immutable and corrections are auditable.
- A complete procure-to-pay workflow passes in automated end-to-end tests.

## Phase 6 — Advanced inventory and warehouse

### Objectives

- Extend basic stock management into operational warehouse control.
- Support multiple locations, transfers, picking, packing, lot/serial tracking,
  cycle counts, replenishment, and configurable costing.
- Make every stock quantity change traceable to a source transaction.

### Dependencies

- Phases 1–5, especially item master data, inventory foundation, sales
  shipments, purchase receipts, and ledger posting contracts.
- Decisions on lot/serial policy, costing method, count tolerances, and
  warehouse execution workflow.

### Major domain objects

- `StockMovement`, `StockMovementLine`
- `Lot`, `SerialNumber`, `Expiry`
- `PickList`, `PickTask`, `PackOperation`
- `WarehouseTransfer`, `TransferLine`
- `CycleCount`, `CountLine`, `Adjustment`
- `ReplenishmentRule`, `ReplenishmentProposal`
- `InventoryCostLayer`, `CostCalculation`
- `StockLedgerEntry`

### Key use cases

- Pick and pack sales orders using allocation and tracking rules.
- Receive and issue lot- or serial-controlled inventory.
- Transfer stock between warehouses, locations, and bins.
- Perform cycle counts and approve variance adjustments.
- Calculate moving-average, FIFO, or approved costing policy results.
- Generate replenishment proposals and convert them into purchase or transfer
  requests.
- Trace a stock balance back to source documents and cost layers.

### Tests required

- Stock movement invariants, reservation, allocation, and concurrency tests.
- Lot/serial uniqueness, expiry, quarantine, and traceability tests.
- Pick/pack/ship and receive/put-away workflow tests.
- Transfer, count, adjustment, and approval tests.
- Cost-layer, landed-cost, valuation, and period-closing tests.
- Inventory subledger-to-general-ledger reconciliation tests.
- Performance tests for high-volume movement and balance queries.

### Explicit exit criteria

- All quantity changes create immutable, traceable stock-ledger entries.
- On-hand, available, allocated, and valuation views reconcile to the stock
  ledger for representative scenarios.
- Lot/serial traceability works from receipt to shipment and from shipment back
  to source receipt.
- Approved costing and adjustment policies are implemented and tested.
- Warehouse users can complete a pick/pack/transfer/count workflow without
  direct database intervention.

## Phase 7 — Fixed assets and cash/bank management

### Objectives

- Manage the asset lifecycle and integrate depreciation with the general ledger.
- Manage bank accounts, cash transactions, statements, reconciliation, and
  payment controls.

### Dependencies

- Phases 1–6 and stable ledger posting/reversal contracts.
- Approved depreciation methods, asset classes, capitalization thresholds,
  bank-import formats, reconciliation policy, and payment approval policy.

### Major domain objects

- `Asset`, `AssetCategory`, `AssetLocation`
- `AssetAcquisition`, `AssetDisposal`, `AssetTransfer`
- `DepreciationBook`, `DepreciationSchedule`, `DepreciationRun`
- `Impairment`, `AssetRevaluation`
- `BankAccount`, `CashAccount`, `BankTransaction`
- `BankStatement`, `BankStatementLine`
- `PaymentBatch`, `PaymentInstruction`, `BankReconciliation`

### Key use cases

- Capitalize an acquisition from purchasing or a manual approved source.
- Calculate, review, post, and reverse depreciation.
- Transfer, impair, revalue, and dispose of assets.
- Record cash receipts, payments, transfers, and bank charges.
- Import bank statements and match transactions to ledger entries.
- Reconcile bank statements and produce unresolved-item queues.
- Enforce payment approvals and prevent duplicate payment instructions.

### Tests required

- Asset capitalization, component, useful-life, residual-value, and
  depreciation-method tests.
- Depreciation period, rounding, posting, reversal, disposal, and impairment
  tests.
- Bank transaction, statement import, duplicate detection, and matching tests.
- Reconciliation and outstanding-item tests.
- Payment approval, authorization, idempotency, and segregation tests.
- Asset subledger, cash subledger, and general-ledger reconciliation tests.

### Explicit exit criteria

- Asset acquisition through disposal produces a complete auditable lifecycle.
- Depreciation runs are deterministic, reviewable, postable, and reversible.
- Bank statements can be imported and reconciled without mutating source
  statement data.
- Payment batches enforce approval and duplicate-prevention rules.
- Asset and bank balances reconcile to the general ledger for acceptance data.

## Phase 8 — Projects

### Objectives

- Support project planning, budgets, tasks, time, expenses, procurement,
  billing, revenue recognition, and project profitability.

### Dependencies

- Phases 1–5 for users, customers, vendors, items, invoices, expenses, and
  ledger integration.
- Decisions on project accounting method, timesheet approval, billing models,
  revenue recognition, and resource calendars.

### Major domain objects

- `Project`, `ProjectTemplate`, `ProjectMember`
- `ProjectPhase`, `Task`, `TaskDependency`
- `ProjectBudget`, `BudgetLine`, `Forecast`
- `Timesheet`, `TimeEntry`, `TimesheetApproval`
- `ProjectExpense`, `ExpenseClaim`
- `ProjectPurchaseCommitment`
- `ProjectMilestone`, `ProgressEvent`
- `ProjectInvoice`, `RevenueRecognitionEntry`

### Key use cases

- Create a project with budget, scope, milestones, tasks, and roles.
- Plan and track work with dependencies and percent complete.
- Submit, approve, and cost timesheets and expenses.
- Track commitments from purchase orders and subcontractors.
- Bill fixed-price, time-and-materials, milestone, or recurring work.
- Recognize revenue and cost according to the configured policy.
- Report project cost, margin, budget variance, forecast, and profitability.

### Tests required

- Task dependency, schedule, assignment, and access tests.
- Timesheet/expense validation, approval, rate, and period-lock tests.
- Budget, commitment, actual-cost, and forecast calculations.
- Billing, milestone, WIP, revenue-recognition, and reversal tests.
- Project-to-ledger, sales, purchasing, and payroll integration tests.
- End-to-end project profitability reconciliation tests.

### Explicit exit criteria

- A project can be planned, executed, costed, billed, and closed using approved
  workflows.
- Actuals and commitments reconcile to project subledgers and the general
  ledger.
- Billing and revenue recognition follow configured contract rules and are
  reversible through explicit transactions.
- Project managers can see budget, actual, committed, forecast, and margin
  measures with documented calculation definitions.

## Phase 9 — Manufacturing/MRP

### Objectives

- Support bills of material, routings, work centers, material planning,
  production orders, subcontracting, quality checkpoints, and manufacturing
  costing.

### Dependencies

- Phases 1–6, especially inventory tracking, costing, warehouses, and ledger
  integration.
- Decisions on make-to-stock/make-to-order policy, planning horizons,
  capacity assumptions, scrap, subcontracting, and quality requirements.

### Major domain objects

- `BillOfMaterial`, `BomVersion`, `BomComponent`
- `Routing`, `RoutingOperation`, `WorkCenter`, `CapacityCalendar`
- `ManufacturingOrder`, `MaterialIssue`, `ProductionReceipt`
- `MaterialRequirement`, `PlannedOrder`, `FirmPlannedOrder`
- `WorkOrder`, `OperationConfirmation`
- `ScrapRecord`, `ByProduct`
- `QualityPlan`, `Inspection`, `Nonconformance`
- `ManufacturingCost`, `Variance`

### Key use cases

- Version and approve bills of material and routings.
- Run material requirements planning from demand, stock, lead times, and
  open supply.
- Create, release, schedule, issue, execute, and complete production orders.
- Record labor, machine time, material consumption, scrap, and by-products.
- Receive finished goods and calculate actual versus standard cost.
- Handle subcontracting and quality holds.
- Report shortages, capacity load, production variance, and traceability.

### Tests required

- BOM/routing version, effective-date, substitute, scrap, and UOM tests.
- MRP netting, lead-time, lot-size, safety-stock, and rescheduling tests.
- Production-order state, material issue, completion, reversal, and concurrency
  tests.
- Capacity, operation confirmation, labor/machine cost, and variance tests.
- Lot/serial genealogy and quality-hold/release tests.
- Inventory and ledger valuation reconciliation tests.

### Explicit exit criteria

- Approved demand can generate explainable planned supply and material
  requirements.
- A production order can consume components and produce finished goods with
  traceable quantities and costs.
- BOM/routing changes are versioned and do not rewrite historical production.
- Actual manufacturing cost and variance reconcile to inventory and the ledger.
- A representative make-to-stock and make-to-order workflow passes end to end.

## Phase 10 — Service management

### Objectives

- Support service contracts, cases, work orders, scheduling, field execution,
  parts/labor consumption, service billing, and service-level commitments.

### Dependencies

- Phases 1–6 for customers, items, inventory, users, billing, and accounting.
- Decisions on SLA calendars, dispatch model, warranty rules, technician
  qualifications, and service pricing.

### Major domain objects

- `ServiceContract`, `ContractLine`, `Warranty`
- `ServiceCase`, `CaseActivity`, `CaseStatusHistory`
- `ServiceRequest`, `WorkOrder`, `WorkOrderTask`
- `Technician`, `Skill`, `Territory`, `ScheduleSlot`
- `ServiceVisit`, `TimeEntry`, `PartsUsage`
- `SlaPolicy`, `SlaClock`, `Escalation`
- `ServiceInvoice`, `ServiceEntitlement`

### Key use cases

- Register a customer case and classify entitlement, priority, and SLA.
- Schedule and dispatch work to qualified technicians.
- Execute a visit with time, notes, signatures, parts, photos, and outcomes.
- Track warranty and contract-covered versus billable work.
- Escalate breached or at-risk SLAs.
- Invoice approved labor, parts, travel, subscriptions, or milestones.
- Analyze first-time-fix rate, response time, utilization, and service margin.

### Tests required

- Entitlement, warranty, contract, and SLA-clock tests including calendars.
- Assignment, qualification, territory, scheduling, and conflict tests.
- Offline/retry-safe field completion and attachment tests where applicable.
- Parts reservation/consumption and labor-rate calculation tests.
- Service billing, credit, tax, and ledger integration tests.
- Escalation, notification, authorization, and audit tests.

### Explicit exit criteria

- A service case can progress from intake through scheduled execution,
  completion, and billing.
- SLA timers pause/resume according to documented calendars and statuses.
- Technician work records are approved, auditable, and safe to retry.
- Covered and billable charges are calculated correctly and reconcile to
  invoices and the ledger.
- Service operational and financial acceptance reports reconcile to source data.

## Phase 11 — CRM and HR

### Objectives

- Add lead, opportunity, campaign, activity, and customer relationship
  workflows.
- Add employee, organization, leave, attendance, skills, and HR administration
  foundations without prematurely building a full payroll engine.

### Dependencies

- Phases 1, 4, 5, 8, and 10 for identity, customers, sales, projects, service,
  and shared activities.
- Legal, privacy, retention, employment, leave, and payroll integration policy.

### Major domain objects

- `Lead`, `Prospect`, `Opportunity`, `OpportunityStage`
- `Campaign`, `CampaignMember`, `MarketingActivity`
- `Interaction`, `Call`, `Meeting`, `Task`, `Relationship`
- `Employee`, `EmploymentRecord`, `OrganizationUnit`, `Position`
- `Skill`, `Certification`, `EmployeeSkill`
- `LeaveType`, `LeaveRequest`, `LeaveBalance`
- `AttendanceRecord`, `WorkSchedule`, `PerformanceReview`
- `Document`, `Consent`, `PrivacyRequest`

### Key use cases

- Capture, qualify, assign, and convert leads.
- Manage opportunities, activities, pipeline stages, forecasts, and win/loss
  reasons.
- Associate interactions with customers, contacts, opportunities, projects,
  and service cases.
- Maintain employee records with effective-dated employment history.
- Manage leave requests, approvals, balances, attendance, skills, and
  certifications.
- Restrict sensitive HR data by role and purpose.
- Export approved data to a payroll or external HR system through a controlled
  integration.

### Tests required

- Lead conversion, duplicate detection, pipeline, forecast, and activity tests.
- Effective-dated employment and organization-history tests.
- Leave accrual/balance, approval, holiday-calendar, and overlap tests.
- HR privacy, field-level access, retention, consent, and audit tests.
- Integration export idempotency and reconciliation tests.
- Tenant, role, and data-segregation tests for sensitive records.

### Explicit exit criteria

- CRM users can manage a lead through opportunity and handoff to sales.
- Customer interactions are searchable without duplicating system-of-record
  financial data.
- HR users can maintain effective-dated employee records and approved leave.
- Sensitive HR fields are inaccessible to unauthorized operational users.
- External payroll/HR exchange, if enabled, is idempotent, auditable, and
  reconciled.

## Phase 12 — Reporting, integrations, and advanced workflow

### Objectives

- Provide governed reporting, dashboards, exports, integration contracts,
  workflow automation, notifications, and operational observability.
- Improve decision support without changing the source-of-truth ownership of
  transactional modules.

### Dependencies

- All required operational phases and stable public application contracts.
- Defined reporting glossary, data-retention policy, integration partners,
  workflow approval matrix, and operational SLOs.

### Major domain objects

- `ReportDefinition`, `ReportVersion`, `ReportFilter`
- `Dashboard`, `Widget`, `KpiDefinition`
- `DataExport`, `ScheduledReport`, `ReportExecution`
- `IntegrationEndpoint`, `IntegrationCredentialReference`
- `IntegrationMessage`, `ImportBatch`, `ImportRow`, `ReconciliationResult`
- `WorkflowDefinition`, `WorkflowVersion`, `WorkflowInstance`
- `ApprovalTask`, `Notification`, `WebhookSubscription`
- `DataQualityIssue`, `OperationalMetric`, `CorrelationTrace`

### Key use cases

- Run standard financial, inventory, sales, purchasing, project, service, and
  operational reports.
- Build governed dashboards using documented measures and dimensions.
- Export data with company and role filters and an audit trail.
- Import external data with validation, idempotency, error queues, and
  reconciliation.
- Configure approval workflows and escalation timers without embedding
  arbitrary code in controllers.
- Publish documented webhooks or API contracts for supported integrations.
- Monitor failures, latency, queue/backlog metrics, and data-quality issues.

### Tests required

- Report totals reconciled to source subledgers and the general ledger.
- Filter, company-scope, permission, export, and scheduling tests.
- Import validation, duplicate, retry, partial-failure, and reconciliation tests.
- Workflow versioning, approval, timeout, escalation, and authorization tests.
- Webhook signature, replay/idempotency, rate-limit, and contract tests.
- Performance, availability, observability, backup/restore, and disaster
  recovery exercises.

### Explicit exit criteria

- Published reports have owners, definitions, source lineage, and reconciliation
  evidence.
- Imports and integrations are retry-safe, observable, auditable, and fail
  visibly with actionable error information.
- Workflow definitions are versioned and cannot change the meaning of an
  in-flight approval unexpectedly.
- Security, performance, backup/restore, and operational runbooks are approved.
- Product leadership accepts the release-readiness review for the selected
  production scope.

## 4. Release governance

### 4.1 Phase entry review

Before starting a phase, the team must approve:

- Scope and exclusions.
- Domain glossary and ownership boundaries.
- Dependencies and required decisions.
- Data migration and compatibility impact.
- Security, privacy, and audit implications.
- Test fixtures and measurable exit criteria.

An unresolved dependency must be recorded as a blocker rather than hidden in
implementation assumptions.

### 4.2 Phase exit review

The phase owner must provide:

- Completed use-case and acceptance-test evidence.
- Migration scripts and rollback/recovery instructions.
- Reconciliation results for all financial and inventory effects.
- Security and tenant-isolation test results.
- Updated API/module contracts and operator documentation.
- Known limitations and explicitly deferred work.

### 4.3 Change and posting policy

- Posted ledger, inventory, invoice, payment, receipt, asset, and production
  records are not edited in place.
- Corrections use reversals, returns, credit/debit notes, adjustments, or
  replacement transactions with links to the original.
- Deletion is limited to eligible draft/reference records and must obey
  retention and referential rules.
- Database migrations must not silently rewrite historical business meaning.

## 5. Explicit non-goals and prohibited shortcuts

The following are outside the baseline implementation unless a later,
documented decision changes the scope:

- Building every phase before validating earlier releases.
- Creating hundreds of empty classes or speculative abstractions.
- Implementing every module in parallel.
- Splitting the system into microservices without measured operational need.
- Adding Kubernetes or distributed messaging in the initial architecture.
- Putting business logic in controllers or convenience methods on EF Core
  entities.
- Allowing cross-module database coupling.
- Treating financial transactions as generic CRUD.
- Editing posted ledger or inventory transactions.
- Silently deleting accounting records.
- Using floats or doubles for money.
- Hard-coding company IDs, credentials, or environment-specific configuration.
- Bypassing domain invariants or suppressing compiler warnings without a
  documented justification.
- Adding libraries without a recorded need, security review, and ownership plan.

## 6. Initial implementation recommendation

Start with Phases 1–3 as a thin but production-quality foundation, then
implement Phase 4 or Phase 5 as the first complete commercial workflow based on
the organization's highest-value process. Do not begin Phase 6 or later until
the relevant posting, audit, inventory, and reconciliation contracts have
passed the earlier phase exit reviews.
