using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Finance.Application;

public sealed record AccountResponse(Guid Id, string Code, string Name, AccountType Type, bool IsActive);
public sealed record CreateAccountRequest(string Code, string Name, AccountType Type);
public sealed record RenameAccountRequest(string Name);

public sealed record JournalLineRequest(string AccountCode, decimal Debit, decimal Credit, string? Description);
public sealed record CreateJournalRequest(DateOnly Date, string Description, IReadOnlyList<JournalLineRequest> Lines, string? CurrencyCode);
public sealed record ReverseJournalRequest(DateOnly? Date, string? Reason);
public sealed record JournalLineResponse(Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit, string Description);
public sealed record JournalResponse(
    Guid Id, string Number, DateOnly Date, string Description, string Source, string? SourceReference,
    string CurrencyCode, decimal TotalDebit, decimal TotalCredit, bool IsReversed, Guid? ReversalOfJournalId,
    DateTimeOffset? PostedAt, IReadOnlyList<JournalLineResponse> Lines);

public sealed record TrialBalanceRow(Guid AccountId, string AccountCode, string AccountName, AccountType Type, decimal Debit, decimal Credit);
public sealed record TrialBalanceResponse(DateOnly? AsOf, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit, bool IsBalanced);

public sealed record AccountBalance(AccountId AccountId, decimal Debit, decimal Credit);

public interface IAccountStore
{
    Task AddAsync(Account account, CancellationToken cancellationToken);
    Task<IReadOnlyList<Account>> ListAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task<Account?> GetAsync(CompanyId companyId, AccountId id, CancellationToken cancellationToken);
    Task<Account?> GetByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IJournalStore
{
    Task AddAsync(GeneralJournal journal, IReadOnlyList<GeneralLedgerEntry> entries, CancellationToken cancellationToken);
    Task<IReadOnlyList<GeneralJournal>> ListAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<GeneralJournal?> GetAsync(CompanyId companyId, JournalId id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountBalance>> BalancesAsync(CompanyId companyId, DateOnly? asOf, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAccountService
{
    Task<AccountResponse> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountResponse>> ListAsync(CancellationToken cancellationToken);
    Task<AccountResponse?> RenameAsync(AccountId id, RenameAccountRequest request, CancellationToken cancellationToken);
    Task<AccountResponse?> SetActiveAsync(AccountId id, bool active, CancellationToken cancellationToken);
}

public interface IJournalService : IFinancePosting
{
    Task<JournalResponse> CreateAndPostAsync(CreateJournalRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<JournalResponse>> ListAsync(int take, CancellationToken cancellationToken);
    Task<JournalResponse?> GetAsync(JournalId id, CancellationToken cancellationToken);
    Task<JournalResponse?> ReverseAsync(JournalId id, ReverseJournalRequest request, CancellationToken cancellationToken);
}

public interface IFinanceReportService
{
    Task<TrialBalanceResponse> TrialBalanceAsync(DateOnly? asOf, CancellationToken cancellationToken);
}

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<JournalService>();
        services.AddScoped<IJournalService>(sp => sp.GetRequiredService<JournalService>());
        services.AddScoped<IFinancePosting>(sp => sp.GetRequiredService<JournalService>());
        services.AddScoped<IFinanceReportService, FinanceReportService>();
        return services;
    }
}
