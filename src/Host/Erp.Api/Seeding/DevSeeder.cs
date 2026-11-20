using Erp.Application.Abstractions;
using Erp.Modules.Finance.Application;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.Modules.Inventory.Application;
using Erp.Modules.Platform.Application;
using Erp.Modules.Purchasing.Application;
using Erp.Modules.Sales.Application;
using Erp.Modules.Sales.Domain;
using PurchasingDomain = Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;

namespace Erp.Api.Seeding;

/// <summary>
/// Development-only, idempotent demo data. Uses the real application services so audit,
/// permission, period and balance rules apply. Run with: dotnet run --project src/Host/Erp.Api -- --seed
/// </summary>
public static class DevSeeder
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Admin#123456";
    public const string CompanyCode = "GTEK-DEMO";

    public static async Task RunAsync(IServiceProvider root, CancellationToken ct = default)
    {
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevSeeder");

        var adminId = await SeedPlatformAsync(sp, log, ct);
        var company = await SeedCompanyAsync(sp, adminId, log, ct);
        sp.GetRequiredService<ActiveCompanyContext>().Set(new CompanyId(company.Id));

        await SeedPeriodsAsync(sp, log, ct);
        await SeedAccountsAsync(sp, log, ct);
        await SeedOpeningBalancesAsync(sp, log, ct);
        await SeedInventoryAsync(sp, log, ct);
        await SeedSalesAsync(sp, log, ct);
        await SeedPurchasingAsync(sp, log, ct);
        log.LogInformation("Seed complete. Sign in as '{User}' / '{Password}' and select company {Company}.",
            AdminUserName, AdminPassword, CompanyCode);
    }

    private static async Task<UserId> SeedPlatformAsync(IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        var users = sp.GetRequiredService<IUserService>();
        var admin = await users.GetByUserNameAsync(AdminUserName, ct);
        if (admin is null)
        {
            admin = await users.CreateAsync(
                new CreateUserRequest(AdminUserName, "admin@gtek.local", AdminPassword),
                !await users.HasUsersAsync(ct), ct);
            log.LogInformation("Created user {User}.", AdminUserName);
        }

        // Re-sync: permissions added after the first admin was created must still reach SYSTEM_ADMIN.
        var permissions = sp.GetRequiredService<IRolePermissionStore>();
        await permissions.EnsurePermissionCatalogueAsync(ct);
        await permissions.SaveChangesAsync(ct);
        await permissions.EnsureSystemAdministratorAsync(new UserId(admin.Id), ct);
        await permissions.SaveChangesAsync(ct);
        return new UserId(admin.Id);
    }

    private static async Task<CompanyResponse> SeedCompanyAsync(IServiceProvider sp, UserId adminId, ILogger log, CancellationToken ct)
    {
        var companies = sp.GetRequiredService<ICompanyService>();
        var company = await companies.GetByCodeAsync(CompanyCode, ct);
        if (company is null)
        {
            company = await companies.CreateAsync(new CreateCompanyRequest(CompanyCode, "GTEK Demo (Pvt) Ltd"), ct);
            log.LogInformation("Created company {Code}.", CompanyCode);
        }

        var access = sp.GetRequiredService<IUserCompanyAccessService>();
        var granted = (await access.ListCompaniesForUserAsync(adminId, ct)).Any(x => x.Id == company.Id);
        if (!granted)
            await access.GrantAsync(adminId, adminId, new CompanyId(company.Id), ct);
        return company;
    }

    private static async Task SeedPeriodsAsync(IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        var periods = sp.GetRequiredService<IAccountingPeriodService>();
        var existing = (await periods.ListAsync(ct)).Select(x => x.Code).ToHashSet();
        var created = 0;
        for (var i = 0; i < 15; i++)
        {
            var start = new DateOnly(2026, 1, 1).AddMonths(i);
            var code = $"{start:yyyy-MM}";
            if (existing.Contains(code)) continue;
            await periods.CreateAsync(
                new CreateAccountingPeriodRequest(code, $"{start:MMMM yyyy}", start, start.AddMonths(1).AddDays(-1)), ct);
            created++;
        }

        if (created > 0) log.LogInformation("Created {Count} accounting periods.", created);
    }

    public static readonly (string Code, string Name, AccountType Type)[] ChartOfAccounts =
    [
        ("1000", "Cash on Hand", AccountType.Asset),
        ("1010", "Bank - Current Account", AccountType.Asset),
        ("1100", "Accounts Receivable", AccountType.Asset),
        ("1200", "Inventory", AccountType.Asset),
        ("2000", "Accounts Payable", AccountType.Liability),
        ("2100", "Goods Received Not Invoiced", AccountType.Liability),
        ("3000", "Owner's Equity", AccountType.Equity),
        ("3100", "Retained Earnings", AccountType.Equity),
        ("4000", "Sales Revenue", AccountType.Revenue),
        ("5000", "Cost of Goods Sold", AccountType.Expense),
        ("6000", "Operating Expenses", AccountType.Expense),
        ("6100", "Rent Expense", AccountType.Expense),
        ("6200", "Salaries Expense", AccountType.Expense)
    ];

    private static async Task SeedAccountsAsync(IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        var accounts = sp.GetRequiredService<IAccountService>();
        var existing = (await accounts.ListAsync(ct)).Select(x => x.Code).ToHashSet();
        foreach (var (code, name, type) in ChartOfAccounts.Where(x => !existing.Contains(x.Code)))
            await accounts.CreateAsync(new CreateAccountRequest(code, name, type), ct);
        log.LogInformation("Chart of accounts ready ({Count} accounts).", ChartOfAccounts.Length);
    }

    private static async Task SeedOpeningBalancesAsync(IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        const string reference = "SEED-OPENING";
        var journals = sp.GetRequiredService<IJournalService>();
        if ((await journals.ListAsync(200, ct)).Any(x => x.SourceReference == reference)) return;

        await journals.PostJournalAsync(new PostJournalCommand(
            "Seed", reference, new DateOnly(2026, 1, 1), "Opening balances",
            [
                new PostJournalLine("1000", 500_000m, 0, "Opening cash"),
                new PostJournalLine("1010", 4_500_000m, 0, "Opening bank balance"),
                new PostJournalLine("3000", 0, 5_000_000m, "Owner's capital")
            ]), ct);
        log.LogInformation("Posted opening balances.");
    }

    public static readonly (string Sku, string Name, string Uom, decimal Price, decimal Cost, decimal OpeningQty)[] Items =
    [
        ("LAP-001", "Business Laptop 14\"", "EA", 185_000m, 142_000m, 12),
        ("LAP-002", "Ultrabook 13\"", "EA", 245_000m, 195_000m, 6),
        ("MON-024", "24\" LED Monitor", "EA", 42_000m, 31_500m, 25),
        ("MON-027", "27\" 4K Monitor", "EA", 95_000m, 74_000m, 10),
        ("KEY-001", "Wired Keyboard", "EA", 3_500m, 2_100m, 80),
        ("KEY-002", "Wireless Keyboard & Mouse", "SET", 7_800m, 5_200m, 45),
        ("MOU-001", "Optical Mouse", "EA", 1_800m, 950m, 120),
        ("PRN-100", "Laser Printer", "EA", 68_000m, 52_000m, 8),
        ("TON-100", "Printer Toner Cartridge", "EA", 14_500m, 9_800m, 40),
        ("NET-008", "8-Port Gigabit Switch", "EA", 12_500m, 8_600m, 30),
        ("NET-RTR", "Wi-Fi 6 Router", "EA", 21_000m, 14_900m, 22),
        ("CBL-CAT6", "Cat6 Cable 305m Box", "BOX", 38_000m, 27_500m, 9),
        ("SSD-512", "512GB NVMe SSD", "EA", 24_000m, 17_800m, 35),
        ("RAM-16", "16GB DDR4 Memory", "EA", 16_500m, 11_900m, 28),
        ("UPS-1K", "1kVA UPS", "EA", 52_000m, 38_500m, 7)
    ];

    private static async Task SeedInventoryAsync(IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        var itemService = sp.GetRequiredService<IItemService>();
        var stock = sp.GetRequiredService<IStockService>();
        var existing = (await itemService.ListAsync(ct)).ToDictionary(x => x.Sku);
        var received = 0m;

        foreach (var (sku, name, uom, price, cost, qty) in Items)
        {
            if (!existing.TryGetValue(sku, out var item))
            {
                item = await itemService.CreateAsync(new CreateItemRequest(sku, name, uom, price, cost), ct);
                existing[sku] = item;
            }

            if ((await stock.LedgerAsync(item.Id, 1, ct)).Count > 0) continue;
            await stock.ReceiveAsync(new ReceiveStockRequest(
                item.Id, qty, cost, new DateOnly(2026, 1, 1), "SEED-OPENING", null), ct);
            received += qty * cost;
        }

        if (received > 0)
        {
            // Keep the GL in step with the stock ledger: opening stock value funded by owner's equity.
            await sp.GetRequiredService<IFinancePosting>().PostJournalAsync(new PostJournalCommand(
                "Seed", "SEED-OPENING-STOCK", new DateOnly(2026, 1, 1), "Opening stock valuation",
                [
                    new PostJournalLine("1200", received, 0, "Opening inventory"),
                    new PostJournalLine("3000", 0, received, "Owner's capital - stock")
                ]), ct);
            log.LogInformation("Seeded opening stock worth {Value:N0}.", received);
        }

        log.LogInformation("Inventory ready ({Count} items).", Items.Length);
    }

    private static readonly (string Code, string Name, string Email, int Terms)[] Customers =
    [
        ("CUS-001", "Lanka Tech Solutions", "accounts@lankatech.example", 30),
        ("CUS-002", "Ceylon Traders (Pvt) Ltd", "finance@ceylontraders.example", 30),
        ("CUS-003", "Island Logistics", "ap@islandlogistics.example", 14),
        ("CUS-004", "Hill Country Hotels", "purchasing@hillcountry.example", 45),
        ("CUS-005", "Colombo Law Chambers", "office@colombolaw.example", 7),
        ("CUS-006", "Green Valley School", "bursar@greenvalley.example", 30),
        ("CUS-007", "Pearl Bay Resorts", "it@pearlbay.example", 30),
        ("CUS-008", "Walk-in Customer", "walkin@gtek.local", 0)
    ];

    private enum SeedState { Draft, Open, Released, Cancelled, Invoiced, Partial, Paid }

    private static readonly (int N, string Customer, SeedState State, DateOnly Date, (string Sku, decimal Qty)[] Lines)[] Orders =
    [
        (1, "CUS-001", SeedState.Draft, new DateOnly(2026, 11, 18), [("LAP-001", 2), ("MOU-001", 2)]),
        (2, "CUS-002", SeedState.Open, new DateOnly(2026, 11, 17), [("MON-024", 4), ("KEY-001", 4)]),
        (3, "CUS-004", SeedState.Released, new DateOnly(2026, 11, 16), [("NET-008", 3), ("NET-RTR", 2), ("CBL-CAT6", 1)]),
        (4, "CUS-003", SeedState.Paid, new DateOnly(2026, 10, 5), [("LAP-002", 1), ("SSD-512", 2)]),
        (5, "CUS-006", SeedState.Partial, new DateOnly(2026, 10, 20), [("MON-027", 3), ("KEY-002", 3)]),
        (6, "CUS-007", SeedState.Invoiced, new DateOnly(2026, 9, 15), [("PRN-100", 2), ("TON-100", 4)]),
        (7, "CUS-005", SeedState.Cancelled, new DateOnly(2026, 11, 2), [("RAM-16", 8)]),
        (8, "CUS-001", SeedState.Invoiced, new DateOnly(2026, 11, 12), [("LAP-001", 3), ("RAM-16", 3), ("SSD-512", 3)]),
        (9, "CUS-008", SeedState.Paid, new DateOnly(2026, 11, 14), [("MOU-001", 5), ("KEY-001", 2)])
    ];

    private static async Task SeedSalesAsync(IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        var customerService = sp.GetRequiredService<ICustomerService>();
        var orderService = sp.GetRequiredService<IOrderService>();
        var invoiceService = sp.GetRequiredService<IInvoiceService>();
        var itemIds = (await sp.GetRequiredService<IItemService>().ListAsync(ct)).ToDictionary(x => x.Sku, x => x.Id);

        var customers = (await customerService.ListAsync(ct)).ToDictionary(x => x.Code);
        foreach (var (code, name, email, terms) in Customers.Where(x => !customers.ContainsKey(x.Code)))
            customers[code] = await customerService.CreateAsync(new CreateCustomerRequest(code, name, email, null, terms), ct);

        var existing = (await orderService.ListAsync(500, ct)).Select(x => x.Notes).ToHashSet();
        var created = 0;
        foreach (var (n, customerCode, state, date, lines) in Orders)
        {
            var marker = $"SEED-{n}";
            if (existing.Contains(marker)) continue;

            var order = await orderService.CreateAsync(new CreateOrderRequest(
                customers[customerCode].Id, date, marker,
                lines.Select(l => new OrderLineRequest(itemIds[l.Sku], l.Qty, null)).ToArray()), ct);
            var id = new SalesOrderId(order.Id);
            created++;
            if (state == SeedState.Draft) continue;
            if (state == SeedState.Cancelled)
            {
                await orderService.TransitionAsync(id, OrderAction.Cancel, ct);
                continue;
            }

            await orderService.TransitionAsync(id, OrderAction.Open, ct);
            if (state == SeedState.Open) continue;
            await orderService.TransitionAsync(id, OrderAction.Release, ct);
            if (state == SeedState.Released) continue;

            var invoice = (await invoiceService.CreateFromOrderAsync(id, new CreateInvoiceRequest(date), ct))!;
            var invoiceId = new SalesInvoiceId(invoice.Id);
            if (state == SeedState.Paid)
                await invoiceService.RecordPaymentAsync(invoiceId,
                    new RecordPaymentRequest(invoice.Total, PaymentMethod.Bank, date.AddDays(10), $"SEED-PAY-{n}"), ct);
            else if (state == SeedState.Partial)
                await invoiceService.RecordPaymentAsync(invoiceId,
                    new RecordPaymentRequest(decimal.Round(invoice.Total / 2, 2), PaymentMethod.Bank, date.AddDays(7), $"SEED-PAY-{n}"), ct);
        }

        log.LogInformation("Sales ready ({Customers} customers, {Created} new orders).", Customers.Length, created);
    }

    private static readonly (string Code, string Name, string Email, int Terms)[] Vendors =
    [
        ("VEN-001", "Asia Pacific Computers", "sales@apcomputers.example", 30),
        ("VEN-002", "Colombo Peripherals Ltd", "orders@cpl.example", 21),
        ("VEN-003", "NetWorks Distribution", "accounts@networks.example", 45),
        ("VEN-004", "Global Storage Imports", "supply@globalstorage.example", 30),
        ("VEN-005", "PowerGuard Lanka", "info@powerguard.example", 14)
    ];

    private enum PurchaseState { Draft, Open, Released, Cancelled, Received, Billed, Partial, Paid }

    // Line cost may differ from the item's standard cost: receipts are valued at the PO price.
    private static readonly (int N, string Vendor, PurchaseState State, DateOnly Date, (string Sku, decimal Qty, decimal? Cost)[] Lines)[] PurchaseOrders =
    [
        (1, "VEN-002", PurchaseState.Draft, new DateOnly(2026, 11, 18), [("MOU-001", 40, null), ("KEY-001", 20, null)]),
        (2, "VEN-003", PurchaseState.Open, new DateOnly(2026, 11, 17), [("NET-008", 10, null)]),
        (3, "VEN-001", PurchaseState.Released, new DateOnly(2026, 11, 16), [("LAP-001", 5, 140_000m), ("MON-024", 6, null)]),
        (4, "VEN-004", PurchaseState.Received, new DateOnly(2026, 11, 10), [("SSD-512", 20, 17_500m), ("RAM-16", 12, null)]),
        (5, "VEN-005", PurchaseState.Billed, new DateOnly(2026, 9, 20), [("UPS-1K", 6, 38_000m)]),
        (6, "VEN-001", PurchaseState.Partial, new DateOnly(2026, 10, 15), [("LAP-002", 4, 193_000m), ("MON-027", 4, null)]),
        (7, "VEN-002", PurchaseState.Paid, new DateOnly(2026, 10, 1), [("KEY-002", 30, null), ("PRN-100", 3, 51_000m)]),
        (8, "VEN-003", PurchaseState.Cancelled, new DateOnly(2026, 11, 3), [("CBL-CAT6", 5, null)])
    ];

    private static async Task SeedPurchasingAsync(IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        var vendorService = sp.GetRequiredService<IVendorService>();
        var orderService = sp.GetRequiredService<IPurchaseOrderService>();
        var billService = sp.GetRequiredService<IBillService>();
        var itemIds = (await sp.GetRequiredService<IItemService>().ListAsync(ct)).ToDictionary(x => x.Sku, x => x.Id);

        var vendors = (await vendorService.ListAsync(ct)).ToDictionary(x => x.Code);
        foreach (var (code, name, email, terms) in Vendors.Where(x => !vendors.ContainsKey(x.Code)))
            vendors[code] = await vendorService.CreateAsync(new CreateVendorRequest(code, name, email, null, terms), ct);

        var existing = (await orderService.ListAsync(500, ct)).Select(x => x.Notes).ToHashSet();
        var created = 0;
        foreach (var (n, vendorCode, state, date, lines) in PurchaseOrders)
        {
            var marker = $"SEED-PO-{n}";
            if (existing.Contains(marker)) continue;

            var order = await orderService.CreateAsync(new CreatePurchaseOrderRequest(
                vendors[vendorCode].Id, date, marker,
                lines.Select(l => new PurchaseLineRequest(itemIds[l.Sku], l.Qty, l.Cost)).ToArray()), ct);
            var id = new PurchasingDomain.PurchaseOrderId(order.Id);
            created++;
            if (state == PurchaseState.Draft) continue;
            if (state == PurchaseState.Cancelled)
            {
                await orderService.TransitionAsync(id, PurchaseOrderAction.Cancel, ct);
                continue;
            }

            await orderService.TransitionAsync(id, PurchaseOrderAction.Open, ct);
            if (state == PurchaseState.Open) continue;
            await orderService.TransitionAsync(id, PurchaseOrderAction.Release, ct);
            if (state == PurchaseState.Released) continue;

            await orderService.ReceiveAsync(id, new ReceiveGoodsRequest(date.AddDays(5)), ct);
            if (state == PurchaseState.Received) continue;

            var bill = (await billService.CreateFromOrderAsync(id, new CreateBillRequest(date.AddDays(8), $"INV/{vendorCode}/{n:000}"), ct))!;
            var billId = new PurchasingDomain.VendorBillId(bill.Id);
            if (state == PurchaseState.Paid)
                await billService.PayAsync(billId, new PayBillRequest(bill.Total, PurchasingDomain.PaymentMethod.Bank, date.AddDays(25), $"SEED-VPAY-{n}"), ct);
            else if (state == PurchaseState.Partial)
                await billService.PayAsync(billId, new PayBillRequest(decimal.Round(bill.Total / 2, 2), PurchasingDomain.PaymentMethod.Bank, date.AddDays(20), $"SEED-VPAY-{n}"), ct);
        }

        log.LogInformation("Purchasing ready ({Vendors} vendors, {Created} new orders).", Vendors.Length, created);
    }
}
