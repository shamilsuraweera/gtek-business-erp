using Erp.Application.Abstractions;
using Erp.Modules.Finance.Application;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.Modules.Platform.Application;
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
}
