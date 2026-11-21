using Erp.Modules.Finance.Application;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class AccountMappingTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());

    private static Account Acct(string code, AccountType type, bool active = true)
    {
        var account = Account.Create(Company, code, $"Account {code}", type);
        if (!active) account.Deactivate();
        return account;
    }

    private static (Dictionary<AccountId, Account> ById, Dictionary<string, Account> ByCode) Index(params Account[] accounts) =>
        (accounts.ToDictionary(x => x.Id), accounts.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase));

    // ---- catalogue -------------------------------------------------------------------------------------------

    [Fact]
    public void Every_role_is_in_the_catalogue_once_with_a_distinct_default_code()
    {
        var roles = Enum.GetValues<AccountRole>();
        Assert.Equal(roles.Length, AccountRoleCatalog.All.Count);
        Assert.All(roles, r => Assert.Single(AccountRoleCatalog.All, x => x.Role == r));
        Assert.Equal(roles.Length, AccountRoleCatalog.All.Select(x => x.DefaultCode).Distinct().Count());
    }

    [Fact]
    public void Role_keys_parse_case_insensitively_and_unknown_keys_do_not()
    {
        Assert.True(AccountRoleCatalog.TryParse("accountsreceivable", out var role));
        Assert.Equal(AccountRole.AccountsReceivable, role);
        Assert.False(AccountRoleCatalog.TryParse("Nonsense", out _));
        Assert.False(AccountRoleCatalog.TryParse("99", out _));
    }

    [Theory]
    [InlineData(AccountRole.Bank, AccountType.Asset, true)]
    [InlineData(AccountRole.Bank, AccountType.Liability, false)]
    [InlineData(AccountRole.AccountsPayable, AccountType.Liability, true)]
    [InlineData(AccountRole.AccountsPayable, AccountType.Asset, false)]
    [InlineData(AccountRole.SalesRevenue, AccountType.Revenue, true)]
    [InlineData(AccountRole.SalesRevenue, AccountType.Expense, false)]
    [InlineData(AccountRole.SalesReturns, AccountType.Revenue, true)]   // contra-revenue may sit in revenue...
    [InlineData(AccountRole.SalesReturns, AccountType.Expense, true)]   // ...or be booked as an expense
    [InlineData(AccountRole.SalesReturns, AccountType.Asset, false)]
    [InlineData(AccountRole.CostOfGoodsSold, AccountType.Expense, true)]
    [InlineData(AccountRole.CostOfGoodsSold, AccountType.Equity, false)]
    [InlineData(AccountRole.PurchasePriceVariance, AccountType.Expense, true)]
    [InlineData(AccountRole.PurchasePriceVariance, AccountType.Revenue, true)]
    public void Only_sensible_account_types_may_play_a_role(AccountRole role, AccountType type, bool allowed)
    {
        var account = Acct("9999", type);
        if (allowed) AccountRoleCatalog.EnsureCompatible(role, account);
        else Assert.Throws<DomainException>(() => AccountRoleCatalog.EnsureCompatible(role, account));
    }

    [Fact]
    public void Inactive_accounts_cannot_be_mapped() =>
        Assert.Throws<DomainException>(() => AccountRoleCatalog.EnsureCompatible(AccountRole.Bank, Acct("1015", AccountType.Asset, active: false)));

    [Fact]
    public void Each_default_code_is_a_type_its_own_role_accepts()
    {
        // The seeded chart of accounts must be mappable to its own roles, otherwise "reset to default" could not validate.
        var seeded = new Dictionary<string, AccountType>
        {
            ["1000"] = AccountType.Asset, ["1010"] = AccountType.Asset, ["1100"] = AccountType.Asset, ["1200"] = AccountType.Asset,
            ["2000"] = AccountType.Liability, ["2100"] = AccountType.Liability, ["4000"] = AccountType.Revenue,
            ["4100"] = AccountType.Revenue, ["5000"] = AccountType.Expense, ["5100"] = AccountType.Expense,
            ["2200"] = AccountType.Liability, ["1300"] = AccountType.Asset,
        };
        foreach (var info in AccountRoleCatalog.All)
            Assert.Contains(seeded[info.DefaultCode], info.AllowedTypes);
    }

    // ---- resolver ---------------------------------------------------------------------------------------------

    [Fact]
    public void Unmapped_role_resolves_to_its_default_code()
    {
        var ar = Acct("1100", AccountType.Asset);
        var (byId, byCode) = Index(ar);

        Assert.Same(ar, AccountRoleResolver.Resolve(AccountRole.AccountsReceivable, new Dictionary<string, AccountId>(), byId, byCode));
    }

    [Fact]
    public void A_mapping_overrides_the_default()
    {
        var defaultBank = Acct("1010", AccountType.Asset);
        var savings = Acct("1015", AccountType.Asset);
        var (byId, byCode) = Index(defaultBank, savings);
        var mapped = new Dictionary<string, AccountId> { [nameof(AccountRole.Bank)] = savings.Id };

        Assert.Same(savings, AccountRoleResolver.Resolve(AccountRole.Bank, mapped, byId, byCode));
        Assert.Same(defaultBank, AccountRoleResolver.Resolve(AccountRole.Bank, new Dictionary<string, AccountId>(), byId, byCode));
    }

    [Fact]
    public void Missing_default_account_gives_an_actionable_error()
    {
        var (byId, byCode) = Index(Acct("1000", AccountType.Asset));

        var error = Assert.Throws<DomainException>(() =>
            AccountRoleResolver.Resolve(AccountRole.AccountsReceivable, new Dictionary<string, AccountId>(), byId, byCode));
        Assert.Contains("Accounts receivable", error.Message);
        Assert.Contains("Account mapping", error.Message);
        Assert.Contains("1100", error.Message);
    }

    [Fact]
    public void Inactive_or_vanished_mapped_accounts_are_rejected_at_posting_time()
    {
        var inactive = Acct("1015", AccountType.Asset, active: false);
        var (byId, byCode) = Index(inactive);
        Assert.Throws<DomainException>(() => AccountRoleResolver.Resolve(
            AccountRole.Bank, new Dictionary<string, AccountId> { [nameof(AccountRole.Bank)] = inactive.Id }, byId, byCode));

        var vanished = new Dictionary<string, AccountId> { [nameof(AccountRole.Bank)] = new AccountId(Guid.NewGuid()) };
        Assert.Throws<DomainException>(() => AccountRoleResolver.Resolve(AccountRole.Bank, vanished, byId, byCode));
    }

    [Fact]
    public void A_mapped_role_ignores_the_default_account_even_when_it_exists_and_is_inactive()
    {
        var oldBank = Acct("1010", AccountType.Asset, active: false);
        var newBank = Acct("1015", AccountType.Asset);
        var (byId, byCode) = Index(oldBank, newBank);

        var resolved = AccountRoleResolver.Resolve(
            AccountRole.Bank, new Dictionary<string, AccountId> { [nameof(AccountRole.Bank)] = newBank.Id }, byId, byCode);
        Assert.Same(newBank, resolved);
    }

    // ---- which roles use an account (guards deactivation) --------------------------------------------------------

    [Fact]
    public void Default_accounts_count_as_in_use_until_their_role_is_mapped_elsewhere()
    {
        var bank = Acct("1010", AccountType.Asset);
        var savings = Acct("1015", AccountType.Asset);

        Assert.Equal(["Bank account"], AccountRoleUsage.RolesUsing(bank, []));
        Assert.Empty(AccountRoleUsage.RolesUsing(savings, []));

        var mapping = AccountMapping.Create(Company, nameof(AccountRole.Bank), savings.Id, DateTimeOffset.UtcNow, null);
        Assert.Empty(AccountRoleUsage.RolesUsing(bank, [mapping]));                 // remapped away: free to deactivate
        Assert.Equal(["Bank account"], AccountRoleUsage.RolesUsing(savings, [mapping]));
    }

    [Fact]
    public void One_account_can_play_several_roles()
    {
        var shared = Acct("1100", AccountType.Asset);
        var inventoryMapping = AccountMapping.Create(Company, nameof(AccountRole.Inventory), shared.Id, DateTimeOffset.UtcNow, null);

        var roles = AccountRoleUsage.RolesUsing(shared, [inventoryMapping]);
        Assert.Contains("Accounts receivable", roles);   // by default code
        Assert.Contains("Inventory", roles);             // by explicit mapping
    }

    [Fact]
    public void Mapping_can_be_reassigned_and_requires_a_role()
    {
        var first = Acct("1015", AccountType.Asset);
        var second = Acct("1016", AccountType.Asset);
        var mapping = AccountMapping.Create(Company, " Bank ", first.Id, DateTimeOffset.UtcNow, null);

        Assert.Equal("Bank", mapping.Role);
        mapping.Reassign(second.Id, DateTimeOffset.UtcNow, UserId.New());
        Assert.Equal(second.Id, mapping.AccountId);
        Assert.NotNull(mapping.ModifiedBy);
        Assert.Throws<DomainException>(() => AccountMapping.Create(Company, " ", first.Id, DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Role_lines_are_distinguishable_from_explicit_code_lines()
    {
        var role = PostJournalLine.ForRole(AccountRole.Bank, 10m, 0m, "x");
        var code = new PostJournalLine("1010", 10m, 0m, "x");

        Assert.Equal(AccountRole.Bank, role.Role);
        Assert.Null(code.Role);
    }
}
