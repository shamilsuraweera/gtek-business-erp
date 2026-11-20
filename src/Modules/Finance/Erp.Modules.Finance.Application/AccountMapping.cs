using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Finance.Application;

public sealed record AccountRoleInfo(
    AccountRole Role, string Label, string Description, string DefaultCode, IReadOnlyList<AccountType> AllowedTypes);

/// <summary>The posting roles, the account code each falls back to, and which account types may be mapped to it.</summary>
public static class AccountRoleCatalog
{
    private static readonly AccountType[] Asset = [AccountType.Asset];
    private static readonly AccountType[] Liability = [AccountType.Liability];

    public static IReadOnlyList<AccountRoleInfo> All { get; } =
    [
        new(AccountRole.Cash, "Cash on hand", "Cash received from customers and paid to vendors in cash.", "1000", Asset),
        new(AccountRole.Bank, "Bank account", "Bank receipts, payments and refunds.", "1010", Asset),
        new(AccountRole.AccountsReceivable, "Accounts receivable", "What customers owe: invoices, credit notes and refunds.", "1100", Asset),
        new(AccountRole.Inventory, "Inventory", "Stock on hand: goods received, returned, issued and returned by customers.", "1200", Asset),
        new(AccountRole.AccountsPayable, "Accounts payable", "What vendors are owed: bills, debit notes and payments.", "2000", Liability),
        new(AccountRole.GoodsReceivedNotInvoiced, "Goods received not invoiced", "Accrual for goods received before the vendor's bill arrives.", "2100", Liability),
        new(AccountRole.SalesRevenue, "Sales revenue", "Revenue recognised when an order is invoiced.", "4000", [AccountType.Revenue]),
        new(AccountRole.SalesReturns, "Sales returns & allowances", "Contra-revenue debited by credit notes (gross sales stay visible).", "4100", [AccountType.Revenue, AccountType.Expense]),
        new(AccountRole.CostOfGoodsSold, "Cost of goods sold", "Cost of stock issued on invoices, reversed by returned goods.", "5000", [AccountType.Expense]),
        new(AccountRole.PurchasePriceVariance, "Purchase price variance", "Difference between the billed price and the order price.", "5100", [AccountType.Expense, AccountType.Revenue]),
    ];

    public static AccountRoleInfo Get(AccountRole role) => All.Single(x => x.Role == role);

    public static bool TryParse(string key, out AccountRole role) =>
        Enum.TryParse(key, ignoreCase: true, out role) && Enum.IsDefined(role);

    /// <summary>Throws unless <paramref name="account"/> may play <paramref name="role"/> (right type, and active).</summary>
    public static void EnsureCompatible(AccountRole role, Account account)
    {
        var info = Get(role);
        if (!account.IsActive) throw new DomainException($"Account '{account.Code}' is inactive and cannot be used for '{info.Label}'.");
        if (!info.AllowedTypes.Contains(account.Type))
            throw new DomainException(
                $"'{info.Label}' needs a {string.Join(" or ", info.AllowedTypes.Select(t => t.ToString().ToLowerInvariant()))} account, " +
                $"but '{account.Code} {account.Name}' is {account.Type.ToString().ToLowerInvariant()}.");
    }
}

public static class AccountRoleUsage
{
    /// <summary>The posting roles that currently resolve to <paramref name="account"/>: by explicit mapping, or by default code when unmapped.</summary>
    public static IReadOnlyList<string> RolesUsing(Account account, IEnumerable<AccountMapping> mappings)
    {
        var rows = mappings.ToList();
        var explicitRoles = rows.Where(m => m.AccountId == account.Id).Select(m => m.Role);
        var mappedRoles = rows.Select(m => m.Role).ToHashSet();
        var defaultRoles = AccountRoleCatalog.All
            .Where(r => !mappedRoles.Contains(r.Role.ToString()) && string.Equals(r.DefaultCode, account.Code, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Role.ToString());
        return explicitRoles.Concat(defaultRoles).Select(r => AccountRoleCatalog.TryParse(r, out var role) ? AccountRoleCatalog.Get(role).Label : r).ToArray();
    }
}

/// <summary>Turns a posting role into a ledger account: the company's mapping if there is one, otherwise the role's default code.</summary>
public static class AccountRoleResolver
{
    public static Account Resolve(
        AccountRole role,
        IReadOnlyDictionary<string, AccountId> mapped,
        IReadOnlyDictionary<AccountId, Account> byId,
        IReadOnlyDictionary<string, Account> byCode)
    {
        var info = AccountRoleCatalog.Get(role);
        Account? account;
        if (mapped.TryGetValue(role.ToString(), out var accountId))
            account = byId.GetValueOrDefault(accountId);
        else
            account = byCode.GetValueOrDefault(info.DefaultCode);

        if (account is null)
            throw new DomainException(
                $"No account is set up for '{info.Label}'. Map it under Finance → Account mapping " +
                $"(the default account {info.DefaultCode} does not exist for this company).");
        if (!account.IsActive)
            throw new DomainException($"The account for '{info.Label}' ({account.Code}) is inactive. Map it to an active account.");
        return account;
    }
}

public sealed record AccountMappingResponse(
    string Role, string Label, string Description, string DefaultCode, IReadOnlyList<AccountType> AllowedTypes,
    Guid? AccountId, string? AccountCode, string? AccountName, bool IsDefault, bool IsMissing, bool HasPostings);
public sealed record SetAccountMappingRequest(Guid AccountId);

public interface IAccountMappingStore
{
    Task<IReadOnlyList<AccountMapping>> ListAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task<AccountMapping?> GetAsync(CompanyId companyId, string role, CancellationToken cancellationToken);
    Task AddAsync(AccountMapping mapping, CancellationToken cancellationToken);
    Task RemoveAsync(AccountMapping mapping, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAccountMappingService
{
    Task<IReadOnlyList<AccountMappingResponse>> ListAsync(CancellationToken cancellationToken);
    Task<AccountMappingResponse> SetAsync(string role, SetAccountMappingRequest request, CancellationToken cancellationToken);
    Task<AccountMappingResponse> ResetAsync(string role, CancellationToken cancellationToken);
}

public sealed class AccountMappingService(
    IAccountMappingStore mappings,
    IAccountStore accounts,
    IJournalStore journals,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditRecorder audit) : IAccountMappingService
{
    public async Task<IReadOnlyList<AccountMappingResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        var (mapped, byId, byCode) = await LoadAsync(companyId, cancellationToken);
        var withPostings = (await journals.BalancesAsync(companyId, null, cancellationToken)).Select(x => x.AccountId).ToHashSet();
        return AccountRoleCatalog.All.Select(info => Map(info, mapped, byId, byCode, withPostings)).ToArray();
    }

    public async Task<AccountMappingResponse> SetAsync(string role, SetAccountMappingRequest request, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        var parsed = ParseRole(role);
        var account = await accounts.GetAsync(companyId, new AccountId(request.AccountId), cancellationToken)
            ?? throw new DomainException("The account was not found.");
        AccountRoleCatalog.EnsureCompatible(parsed, account);

        var existing = await mappings.GetAsync(companyId, parsed.ToString(), cancellationToken);
        var previous = existing?.AccountId;
        if (existing is null) await mappings.AddAsync(AccountMapping.Create(companyId, parsed.ToString(), account.Id, clock.UtcNow, currentUser.UserId), cancellationToken);
        else existing.Reassign(account.Id, clock.UtcNow, currentUser.UserId);
        await mappings.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync("Finance", "account-mapping.changed", "AccountMapping", parsed.ToString(), AccountRoleCatalog.Get(parsed).Label, true,
            new Dictionary<string, object?> { ["Account"] = account.Code, ["PreviousAccountId"] = previous?.Value }, cancellationToken);
        return await GetOneAsync(companyId, parsed, cancellationToken);
    }

    public async Task<AccountMappingResponse> ResetAsync(string role, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        var parsed = ParseRole(role);
        var existing = await mappings.GetAsync(companyId, parsed.ToString(), cancellationToken);
        if (existing is not null)
        {
            await mappings.RemoveAsync(existing, cancellationToken);
            await mappings.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync("Finance", "account-mapping.reset", "AccountMapping", parsed.ToString(), AccountRoleCatalog.Get(parsed).Label, true, null, cancellationToken);
        }

        return await GetOneAsync(companyId, parsed, cancellationToken);
    }

    private async Task<AccountMappingResponse> GetOneAsync(CompanyId companyId, AccountRole role, CancellationToken ct)
    {
        var (mapped, byId, byCode) = await LoadAsync(companyId, ct);
        var withPostings = (await journals.BalancesAsync(companyId, null, ct)).Select(x => x.AccountId).ToHashSet();
        return Map(AccountRoleCatalog.Get(role), mapped, byId, byCode, withPostings);
    }

    private async Task<(Dictionary<string, AccountId> Mapped, Dictionary<AccountId, Account> ById, Dictionary<string, Account> ByCode)> LoadAsync(
        CompanyId companyId, CancellationToken ct)
    {
        var all = await accounts.ListAsync(companyId, ct);
        return ((await mappings.ListAsync(companyId, ct)).ToDictionary(x => x.Role, x => x.AccountId),
            all.ToDictionary(x => x.Id), all.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase));
    }

    private static AccountMappingResponse Map(
        AccountRoleInfo info, Dictionary<string, AccountId> mapped, Dictionary<AccountId, Account> byId,
        Dictionary<string, Account> byCode, HashSet<AccountId> withPostings)
    {
        var isDefault = !mapped.ContainsKey(info.Role.ToString());
        var account = isDefault ? byCode.GetValueOrDefault(info.DefaultCode) : byId.GetValueOrDefault(mapped[info.Role.ToString()]);
        return new AccountMappingResponse(
            info.Role.ToString(), info.Label, info.Description, info.DefaultCode, info.AllowedTypes,
            account?.Id.Value, account?.Code, account?.Name, isDefault, account is null, account is not null && withPostings.Contains(account.Id));
    }

    private static AccountRole ParseRole(string role) =>
        AccountRoleCatalog.TryParse(role, out var parsed) ? parsed : throw new DomainException($"Unknown account role '{role}'.");

    private CompanyId RequireCompany() =>
        companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required.");
}
