namespace Erp.Modules.Finance.Contracts;

public sealed record PostJournalLine(string AccountCode, decimal Debit, decimal Credit, string? Description);

public sealed record PostJournalCommand(
    string Source,
    string? SourceReference,
    DateOnly Date,
    string Description,
    IReadOnlyList<PostJournalLine> Lines,
    string CurrencyCode = "LKR");

public sealed record PostedJournal(Guid JournalId, string Number);

/// <summary>Cross-module posting contract. Other modules post to the general ledger only through this interface.</summary>
public interface IFinancePosting
{
    Task<PostedJournal> PostJournalAsync(PostJournalCommand command, CancellationToken cancellationToken);

    /// <summary>Posts a mirror journal that reverses a previously posted one (used to compensate failed multi-module operations).</summary>
    Task<PostedJournal> ReverseJournalAsync(Guid journalId, string reason, CancellationToken cancellationToken);
}
