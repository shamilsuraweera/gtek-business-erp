using Erp.Application.Abstractions;
using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Finance.Application;

public sealed class AccountService(
    IAccountStore store, ICompanyContext companyContext, IClock clock, IAuditRecorder audit) : IAccountService
{
    public async Task<AccountResponse> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        if (await store.GetByCodeAsync(companyId, request.Code, cancellationToken) is not null)
            throw new InvalidOperationException("An account with this code already exists for the company.");

        var account = Account.Create(companyId, request.Code, request.Name, request.Type, clock.UtcNow);
        await store.AddAsync(account, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Finance", "account.created", "Account", account.Id.Value.ToString(), account.Code,
            true, new Dictionary<string, object?> { ["Type"] = account.Type.ToString() }, cancellationToken);
        return Map(account);
    }

    public async Task<IReadOnlyList<AccountResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(RequireCompany(), cancellationToken)).Select(Map).ToArray();

    public async Task<AccountResponse?> RenameAsync(AccountId id, RenameAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (account is null) return null;
        account.Rename(request.Name);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Finance", "account.renamed", "Account", account.Id.Value.ToString(), account.Code,
            true, new Dictionary<string, object?> { ["Name"] = account.Name }, cancellationToken);
        return Map(account);
    }

    public async Task<AccountResponse?> SetActiveAsync(AccountId id, bool active, CancellationToken cancellationToken)
    {
        var account = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (account is null) return null;
        if (active) account.Activate(); else account.Deactivate();
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Finance", active ? "account.activated" : "account.deactivated", "Account",
            account.Id.Value.ToString(), account.Code, true, null, cancellationToken);
        return Map(account);
    }

    private CompanyId RequireCompany() =>
        companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required.");

    private static AccountResponse Map(Account a) => new(a.Id.Value, a.Code, a.Name, a.Type, a.IsActive);
}
