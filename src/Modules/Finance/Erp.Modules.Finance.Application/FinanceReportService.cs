using Erp.Application.Abstractions;

namespace Erp.Modules.Finance.Application;

public sealed class FinanceReportService(IJournalStore journals, IAccountStore accounts, ICompanyContext companyContext)
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
}
