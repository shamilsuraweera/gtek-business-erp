using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Finance.Application;

public sealed class FinanceReportService(
    IJournalStore journals, IAccountStore accounts, IAccountMappingStore mappings, ICompanyContext companyContext)
    : IFinanceReportService
{
    public async Task<TrialBalanceResponse> TrialBalanceAsync(DateOnly? asOf, CancellationToken cancellationToken)
    {
        var companyId = companyContext.HasCompany
            ? companyContext.CompanyId
            : throw new InvalidOperationException("An active company context is required.");
        var balances = (await journals.BalancesAsync(companyId, asOf, cancellationToken)).ToDictionary(x => x.AccountId);
        var rows = new List<TrialBalanceRow>();
        foreach (var account in await accounts.ListAsync(companyId, cancellationToken))
        {
            if (!balances.TryGetValue(account.Id, out var b)) continue;
            var net = b.Debit - b.Credit;
            rows.Add(new TrialBalanceRow(
                account.Id.Value, account.Code, account.Name, account.Type,
                net > 0 ? net : 0, net < 0 ? -net : 0));
        }

        var ordered = rows.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToArray();
        var totalDebit = ordered.Sum(x => x.Debit);
        var totalCredit = ordered.Sum(x => x.Credit);
        return new TrialBalanceResponse(asOf, ordered, totalDebit, totalCredit, totalDebit == totalCredit);
    }

    public async Task<TaxSummaryResponse> TaxSummaryAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var companyId = companyContext.HasCompany
            ? companyContext.CompanyId
            : throw new InvalidOperationException("An active company context is required.");
        var all = await accounts.ListAsync(companyId, cancellationToken);
        var mapped = (await mappings.ListAsync(companyId, cancellationToken)).ToDictionary(x => x.Role, x => x.AccountId);
        var byId = all.ToDictionary(x => x.Id);
        var byCode = all.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var notes = new List<string>();
        Account? Find(AccountRole role)
        {
            try { return AccountRoleResolver.Resolve(role, mapped, byId, byCode); }
            catch (DomainException e) { notes.Add(e.Message); return null; }
        }

        var output = Find(AccountRole.TaxPayable);
        var input = Find(AccountRole.TaxReceivable);

        // Movement in the period = balance at the end less balance just before the start.
        var closing = (await journals.BalancesAsync(companyId, to, cancellationToken)).ToDictionary(x => x.AccountId);
        var opening = from is { } f
            ? (await journals.BalancesAsync(companyId, f.AddDays(-1), cancellationToken)).ToDictionary(x => x.AccountId)
            : [];

        decimal Movement(Account? account, bool creditNormal)
        {
            if (account is null) return 0m;
            var end = closing.GetValueOrDefault(account.Id);
            var start = opening.GetValueOrDefault(account.Id);
            var net = (end?.Debit ?? 0) - (end?.Credit ?? 0) - ((start?.Debit ?? 0) - (start?.Credit ?? 0));
            return creditNormal ? -net : net;
        }

        var outputTax = Movement(output, creditNormal: true);
        var inputTax = Movement(input, creditNormal: false);
        return new TaxSummaryResponse(from, to, output?.Code, input?.Code, outputTax, inputTax, outputTax - inputTax, notes);
    }
}
