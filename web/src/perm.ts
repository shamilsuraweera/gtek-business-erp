/** Permission codes mirrored from the API's Permissions catalogue. */
export const P = {
  accountsRead: 'finance.accounts.read', accountsManage: 'finance.accounts.manage', mappingManage: 'finance.account-mapping.manage', taxCodesManage: 'finance.tax-codes.manage',
  journalsRead: 'finance.journals.read', journalsPost: 'finance.journals.post', reportsRead: 'finance.reports.read',
  itemsRead: 'inventory.items.read', itemsManage: 'inventory.items.manage', stockRead: 'inventory.stock.read', stockManage: 'inventory.stock.manage',
  customersRead: 'sales.customers.read', customersManage: 'sales.customers.manage',
  ordersRead: 'sales.orders.read', ordersManage: 'sales.orders.manage',
  invoicesRead: 'sales.invoices.read', creditNotesPost: 'sales.credit-notes.post', refundsRecord: 'sales.refunds.record', invoicesPost: 'sales.invoices.post', paymentsRecord: 'sales.payments.record',
  vendorsRead: 'purchasing.vendors.read', vendorsManage: 'purchasing.vendors.manage',
  poRead: 'purchasing.orders.read', poManage: 'purchasing.orders.manage', poReceive: 'purchasing.orders.receive', poOverrideTolerance: 'purchasing.orders.override-tolerance',
  billsRead: 'purchasing.bills.read', billsPost: 'purchasing.bills.post', billsOverride: 'purchasing.bills.override-variance', returnsPost: 'purchasing.returns.post', debitNotesPost: 'purchasing.debit-notes.post', vendorRefundsRecord: 'purchasing.vendor-refunds.record', vendorPay: 'purchasing.payments.record',
  periodsRead: 'platform.accounting-periods.read', periodsManage: 'platform.accounting-periods.manage',
  sequencesRead: 'platform.number-sequences.read', usersRead: 'platform.users.read', auditRead: 'platform.audit.read',
} as const
