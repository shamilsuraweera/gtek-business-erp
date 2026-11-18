using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Finance.Application;

public sealed class JournalService(
    IJournalStore journals,
    IAccountStore accounts,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IClock clock,
    IPostingPeriodGuard periodGuard,
    IDocumentNumberGenerator numbers,
    IAuditRecorder audit) : IJournalService
{
    public const string JournalSequence = "JE";

    public Task<JournalResponse> CreateAndPostAsync(CreateJournalRequest request, CancellationToken cancellationToken) =>
        PostCoreAsync(
            "Manual", null, request.Date, request.Description,
            request.Lines.Select(x => new PostJournalLine(x.AccountCode, x.Debit, x.Credit, x.Description)).ToArray(),
            request.CurrencyCode ?? "LKR", cancellationToken);

    public async Task<PostedJournal> PostJournalAsync(PostJournalCommand command, CancellationToken cancellationToken)
    {
        var journal = await PostCoreAsync(
            command.Source, command.SourceReference, command.Date, command.Description,
            command.Lines, command.CurrencyCode, cancellationToken);
        return new PostedJournal(journal.Id, journal.Number);
    }

    public async Task<IReadOnlyList<JournalResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        var list = await journals.ListAsync(companyId, Math.Clamp(take, 1, 200), cancellationToken);
        var names = await AccountLookupAsync(companyId, cancellationToken);
        return list.Select(j => Map(j, names)).ToArray();
    }

    public async Task<JournalResponse?> GetAsync(JournalId id, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        var journal = await journals.GetAsync(companyId, id, cancellationToken);
        return journal is null ? null : Map(journal, await AccountLookupAsync(companyId, cancellationToken));
    }

    public async Task<JournalResponse?> ReverseAsync(JournalId id, ReverseJournalRequest request, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        var original = await journals.GetAsync(companyId, id, cancellationToken);
        if (original is null) return null;

        var date = request.Date ?? original.JournalDate;
        await periodGuard.EnsurePostingAllowedAsync(date, cancellationToken);
        var number = await numbers.NextAsync(JournalSequence, "JE-", cancellationToken);
        var description = string.IsNullOrWhiteSpace(request.Reason)
            ? $"Reversal of {original.Number}"
            : $"Reversal of {original.Number}: {request.Reason.Trim()}";
        var reversal = original.CreateReversal(number, date, description);
        var entries = reversal.Post(clock.UtcNow, currentUser.UserId);
        await journals.AddAsync(reversal, entries, cancellationToken);
        await journals.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Finance", "journal.reversed", "GeneralJournal", original.Id.Value.ToString(), original.Number,
            true, new Dictionary<string, object?> { ["ReversalNumber"] = reversal.Number }, cancellationToken);
        return Map(reversal, await AccountLookupAsync(companyId, cancellationToken));
    }

    private async Task<JournalResponse> PostCoreAsync(
        string source, string? sourceReference, DateOnly date, string description,
        IReadOnlyList<PostJournalLine> lines, string currencyCode, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        await periodGuard.EnsurePostingAllowedAsync(date, cancellationToken);

        var byCode = (await accounts.ListAsync(companyId, cancellationToken))
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var number = await numbers.NextAsync(JournalSequence, "JE-", cancellationToken);
        var journal = GeneralJournal.Create(
            companyId, new Currency(currencyCode.Trim().ToUpperInvariant(), 2), number, date, description, source, sourceReference);
        foreach (var line in lines)
        {
            if (!byCode.TryGetValue(line.AccountCode.Trim(), out var account))
                throw new DomainException($"Account '{line.AccountCode}' was not found.");
            if (!account.IsActive)
                throw new DomainException($"Account '{account.Code}' is inactive.");
            if (line.Debit > 0) journal.AddDebit(account.Id, line.Debit, line.Description ?? string.Empty);
            else journal.AddCredit(account.Id, line.Credit, line.Description ?? string.Empty);
        }

        var entries = journal.Post(clock.UtcNow, currentUser.UserId);
        await journals.AddAsync(journal, entries, cancellationToken);
        await journals.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Finance", "journal.posted", "GeneralJournal", journal.Id.Value.ToString(), journal.Number,
            true, new Dictionary<string, object?>
            {
                ["Source"] = journal.Source, ["Total"] = journal.TotalDebit, ["Date"] = journal.JournalDate
            }, cancellationToken);
        return Map(journal, byCode.Values.ToDictionary(x => x.Id, x => x));
    }

    private async Task<Dictionary<AccountId, Account>> AccountLookupAsync(CompanyId companyId, CancellationToken ct) =>
        (await accounts.ListAsync(companyId, ct)).ToDictionary(x => x.Id, x => x);

    private CompanyId RequireCompany() =>
        companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required.");

    private static JournalResponse Map(GeneralJournal j, IReadOnlyDictionary<AccountId, Account> accountLookup) => new(
        j.Id.Value, j.Number, j.JournalDate, j.Description, j.Source, j.SourceReference, j.Currency.Code,
        j.TotalDebit, j.TotalCredit, j.IsReversed, j.ReversalOfJournalId?.Value, j.PostedAt,
        j.Lines.Select(l => new JournalLineResponse(
            l.AccountId.Value,
            accountLookup.TryGetValue(l.AccountId, out var a) ? a.Code : string.Empty,
            a?.Name ?? string.Empty, l.Debit, l.Credit, l.Description)).ToArray());
}
