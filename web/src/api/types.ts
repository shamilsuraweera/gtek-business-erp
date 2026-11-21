export interface Company { id: string; code: string; name: string; status: string }
export interface User { id: string; userName: string; email: string; status: string; createdAt: string }
export interface LoginResponse { accessToken: string; tokenType: string; expiresAt: string }

export interface Customer { id: string; code: string; name: string; email: string | null; phone: string | null; paymentTermsDays: number; isActive: boolean }
export interface Item { id: string; sku: string; name: string; unitOfMeasure: string; unitPrice: number; unitCost: number; isActive: boolean; taxCode: string | null; priceTolerancePercent: number | null; quantityTolerancePercent: number | null; underDeliveryTolerancePercent: number | null }
export interface TaxCode { id: string; code: string; name: string; ratePercent: number; isActive: boolean }
export interface TaxSummary { from: string | null; to: string | null; outputAccountCode: string | null; inputAccountCode: string | null; outputTax: number; inputTax: number; netPayable: number; notes: string[] }
export interface StockLevel { itemId: string; sku: string; itemName: string; unitOfMeasure: string; onHand: number; unitCost: number; value: number }
export interface StockLedgerEntry { id: string; itemId: string; sku: string; locationId: string; quantity: number; unitCost: number; postingDate: string; postedAt: string; sourceType: string; sourceReference: string }

export type OrderStatus = 'Draft' | 'Open' | 'Released' | 'Invoiced' | 'Cancelled'
export interface OrderLine { itemId: string; sku: string; description: string; quantity: number; unitPrice: number; lineTotal: number }
export interface Order { id: string; number: string; customerId: string; customerName: string; orderDate: string; notes: string | null; status: OrderStatus; total: number; invoiceId: string | null; lines: OrderLine[] }
export interface InvoiceLine extends OrderLine { quantityCredited: number; taxCode: string | null; taxRate: number; taxAmount: number }
export interface Invoice { id: string; number: string; salesOrderId: string; customerId: string; customerName: string; invoiceDate: string; dueDate: string; subtotal: number; taxTotal: number; total: number; amountPaid: number; amountCredited: number; balance: number; status: 'Open' | 'Paid' | 'Credited'; isOverdue: boolean; journalNumber: string; lines: InvoiceLine[] }
export interface Payment { id: string; number: string; invoiceId: string; invoiceNumber: string; customerId: string; customerName: string; paymentDate: string; amount: number; method: 'Cash' | 'Bank'; reference: string; journalNumber: string }

export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Revenue' | 'Expense'
export interface Account { id: string; code: string; name: string; type: AccountType; isActive: boolean }
export interface JournalLine { accountId: string; accountCode: string; accountName: string; debit: number; credit: number; description: string }
export interface Journal { id: string; number: string; date: string; description: string; source: string; sourceReference: string | null; currencyCode: string; totalDebit: number; totalCredit: number; isReversed: boolean; reversalOfJournalId: string | null; postedAt: string | null; lines: JournalLine[] }
export interface TrialBalanceRow { accountId: string; accountCode: string; accountName: string; type: AccountType; debit: number; credit: number }
export interface TrialBalance { asOf: string | null; rows: TrialBalanceRow[]; totalDebit: number; totalCredit: number; isBalanced: boolean }

export interface Period { id: string; code: string; name: string; startDate: string; endDate: string; status: 'Open' | 'Closed' | 'Locked' }
export interface Sequence { id: string; code: string; name: string; prefix: string; suffix: string | null; nextValue: number; padding: number; increment: number; status: string }
export interface AuditEntry { id: string; occurredAt: string; actorUserName: string | null; category: string; action: string; entityType: string; entityDisplay: string | null; outcome: string }
export interface AuditPage { items: AuditEntry[]; page: number; pageSize: number; totalCount: number }

export interface Vendor { id: string; code: string; name: string; email: string | null; phone: string | null; paymentTermsDays: number; isActive: boolean; priceTolerancePercent: number | null; quantityTolerancePercent: number | null; underDeliveryTolerancePercent: number | null }
export type PurchaseOrderStatus = 'Draft' | 'Open' | 'Released' | 'PartiallyReceived' | 'Received' | 'Billed' | 'Cancelled'
export interface PurchaseOrderLine { itemId: string; sku: string; description: string; quantity: number; unitCost: number; lineTotal: number; quantityReceived: number; quantityBilled: number; quantityReturned: number; quantityDebited: number; debitableQuantity: number; priceTolerancePercent: number | null; quantityTolerancePercent: number | null; underDeliveryTolerancePercent: number | null; priceToleranceOverride: number | null; quantityToleranceOverride: number | null; underDeliveryToleranceOverride: number | null }
export interface ReceiptSummary { id: string; number: string; receiptDate: string; reference: string; total: number; journalNumber: string }
export interface ReturnSummary { id: string; number: string; returnDate: string; reason: string; reference: string; total: number; journalNumber: string }
export interface BillSummary { id: string; number: string; billDate: string; total: number; priceVariance: number; status: 'Open' | 'Paid' }
export interface PurchaseOrder { id: string; number: string; vendorId: string; vendorName: string; orderDate: string; notes: string | null; status: PurchaseOrderStatus; total: number; lines: PurchaseOrderLine[]; receipts: ReceiptSummary[]; bills: BillSummary[]; returns: ReturnSummary[] }
export interface BillLine { itemId: string; sku: string; description: string; quantity: number; orderUnitCost: number; unitCost: number; lineTotal: number; priceVariance: number; matchStatus: 'Matched' | 'Within tolerance' | 'Variance approved'; quantityDebited: number; taxCode: string | null; taxRate: number; taxAmount: number; tolerancePercent: number }
export interface Bill { id: string; number: string; vendorReference: string | null; purchaseOrderId: string; vendorId: string; vendorName: string; billDate: string; dueDate: string; subtotal: number; taxTotal: number; total: number; amountPaid: number; amountDebited: number; balance: number; status: 'Open' | 'Paid' | 'Debited'; isOverdue: boolean; journalNumber: string; priceVariance: number; varianceOverridden: boolean; lines: BillLine[] }
export interface VendorPayment { id: string; number: string; billId: string; billNumber: string; vendorId: string; vendorName: string; paymentDate: string; amount: number; method: 'Cash' | 'Bank'; reference: string; journalNumber: string }

export interface CreditNoteLine { itemId: string; sku: string; description: string; quantity: number; unitPrice: number; lineTotal: number; returnedToStock: boolean; taxRate: number; taxAmount: number }
export interface Refund { id: string; number: string; creditNoteId: string; creditNoteNumber: string; customerId: string; customerName: string; refundDate: string; amount: number; method: 'Cash' | 'Bank'; reference: string; journalNumber: string }
export interface CreditNote { id: string; number: string; salesInvoiceId: string; invoiceNumber: string; customerId: string; customerName: string; creditDate: string; reason: string; subtotal: number; taxTotal: number; total: number; amountApplied: number; amountRefunded: number; refundDue: number; returnedCost: number; journalNumber: string; lines: CreditNoteLine[]; refunds: Refund[] }

export interface DebitNoteLine { itemId: string; sku: string; description: string; quantity: number; orderUnitCost: number; unitCost: number; lineTotal: number; priceVariance: number; taxRate: number; taxAmount: number }
export interface VendorRefund { id: string; number: string; debitNoteId: string; debitNoteNumber: string; vendorId: string; vendorName: string; refundDate: string; amount: number; method: 'Cash' | 'Bank'; reference: string; journalNumber: string }
export interface DebitNote { id: string; number: string; purchaseOrderId: string; billId: string; billNumber: string; vendorId: string; vendorName: string; debitDate: string; reason: string; subtotal: number; taxTotal: number; total: number; amountApplied: number; amountRefunded: number; refundDue: number; priceVariance: number; journalNumber: string; lines: DebitNoteLine[]; refunds: VendorRefund[] }

export interface AccountMapping { role: string; label: string; description: string; defaultCode: string; allowedTypes: AccountType[]; accountId: string | null; accountCode: string | null; accountName: string | null; isDefault: boolean; isMissing: boolean; hasPostings: boolean }

export interface ToleranceRequestLine { itemId: string; sku: string; description: string; priceTolerancePercent: number | null; quantityTolerancePercent: number | null; underDeliveryTolerancePercent: number | null; currentPriceOverride: number | null; currentQuantityOverride: number | null; currentUnderDeliveryOverride: number | null }
export interface ToleranceRequest { id: string; number: string; purchaseOrderId: string; orderNumber: string; vendorName: string; status: 'Pending' | 'Approved' | 'Rejected' | 'Withdrawn'; reason: string; requestedById: string; requestedBy: string; requestedAt: string; decidedBy: string | null; decidedAt: string | null; decisionNote: string | null; lines: ToleranceRequestLine[] }

export interface AppNotification { id: string; category: string; title: string; body: string | null; link: string | null; createdAt: string; isRead: boolean }
