namespace Erp.Modules.Finance.Contracts;

/// <summary>
/// What a posting line is *for*. Business modules post to roles; Finance maps each role to a ledger account per company
/// (configurable, with a default code when nothing is mapped).
/// </summary>
public enum AccountRole
{
    Cash,
    Bank,
    AccountsReceivable,
    Inventory,
    AccountsPayable,
    /// <summary>Goods received not invoiced: accrued at receipt, cleared by the vendor bill.</summary>
    GoodsReceivedNotInvoiced,
    SalesRevenue,
    /// <summary>Contra-revenue: credit notes debit this so gross sales stay visible.</summary>
    SalesReturns,
    CostOfGoodsSold,
    PurchasePriceVariance,
    /// <summary>Tax charged on sales, owed to the tax authority (credited on invoices, debited by credit notes).</summary>
    TaxPayable,
    /// <summary>Tax paid on purchases, recoverable from the tax authority (debited on vendor bills, credited by debit notes).</summary>
    TaxReceivable
}

/// <summary>A posting line: either an explicit account code (manual journals) or a role resolved through the account mapping.</summary>
public sealed record PostJournalLine(string AccountCode, decimal Debit, decimal Credit, string? Description, AccountRole? Role = null)
{
    public static PostJournalLine ForRole(AccountRole role, decimal debit, decimal credit, string? description) =>
        new(string.Empty, debit, credit, description, role);
}

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

public sealed record TaxCodeInfo(string Code, string Name, decimal RatePercent, bool IsActive);

/// <summary>Cross-module read access to tax codes (rates are applied by the module that issues the document).</summary>
public interface ITaxCatalog
{
    /// <summary>The tax codes that exist for the active company, keyed by code (case-insensitive). Unknown codes are absent.</summary>
    Task<IReadOnlyDictionary<string, TaxCodeInfo>> GetAsync(IEnumerable<string> codes, CancellationToken cancellationToken);
}
