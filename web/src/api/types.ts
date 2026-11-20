export interface Company { id: string; code: string; name: string; status: string }
export interface User { id: string; userName: string; email: string; status: string; createdAt: string }
export interface LoginResponse { accessToken: string; tokenType: string; expiresAt: string }

export interface Customer { id: string; code: string; name: string; email: string | null; phone: string | null; paymentTermsDays: number; isActive: boolean }
export interface Item { id: string; sku: string; name: string; unitOfMeasure: string; unitPrice: number; unitCost: number; isActive: boolean }
export interface StockLevel { itemId: string; sku: string; itemName: string; unitOfMeasure: string; onHand: number; unitCost: number; value: number }
export interface StockLedgerEntry { id: string; itemId: string; sku: string; locationId: string; quantity: number; unitCost: number; postingDate: string; postedAt: string; sourceType: string; sourceReference: string }

export type OrderStatus = 'Draft' | 'Open' | 'Released' | 'Invoiced' | 'Cancelled'
export interface OrderLine { itemId: string; sku: string; description: string; quantity: number; unitPrice: number; lineTotal: number }
export interface Order { id: string; number: string; customerId: string; customerName: string; orderDate: string; notes: string | null; status: OrderStatus; total: number; invoiceId: string | null; lines: OrderLine[] }
export interface Invoice { id: string; number: string; salesOrderId: string; customerId: string; customerName: string; invoiceDate: string; dueDate: string; total: number; amountPaid: number; balance: number; status: 'Open' | 'Paid'; isOverdue: boolean; journalNumber: string; lines: OrderLine[] }
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
