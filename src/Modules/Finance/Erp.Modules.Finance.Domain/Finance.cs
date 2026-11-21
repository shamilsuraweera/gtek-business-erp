using Erp.SharedKernel;

namespace Erp.Modules.Finance.Domain;

public readonly record struct AccountId(Guid Value);
public readonly record struct JournalId(Guid Value);
public readonly record struct LedgerEntryId(Guid Value);
public readonly record struct AccountMappingId(Guid Value);
public readonly record struct TaxCodeId(Guid Value);

public enum AccountType { Asset, Liability, Equity, Revenue, Expense }

public sealed class Account : AggregateRoot<AccountId>
{
    private Account()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Account(AccountId id, CompanyId companyId, string code, string name, AccountType type, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Code = NormalizeCode(code);
        Name = NormalizeName(name);
        Type = type;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public AccountType Type { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Debit-normal accounts increase with debits (assets and expenses).</summary>
    public bool IsDebitNormal => Type is AccountType.Asset or AccountType.Expense;

    public static Account Create(CompanyId companyId, string code, string name, AccountType type, DateTimeOffset? createdAt = null) =>
        new(new AccountId(Guid.NewGuid()), companyId, code, name, type, createdAt ?? DateTimeOffset.UtcNow);

    public static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? throw new DomainException("Account code is required.") : code.Trim().ToUpperInvariant();

    public void Rename(string name) => Name = NormalizeName(name);
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private static string NormalizeName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("Account name is required.") : name.Trim();
}

public sealed record Currency(string Code, int DecimalPlaces);

public sealed record GeneralJournalLine
{
    public GeneralJournalLine(AccountId accountId, decimal debit, decimal credit, string description)
    {
        if (debit < 0 || credit < 0 || (debit > 0 && credit > 0) || (debit == 0 && credit == 0))
        {
            throw new DomainException("A journal line must contain either a positive debit or a positive credit.");
        }

        AccountId = accountId;
        Debit = debit;
        Credit = credit;
        Description = description ?? string.Empty;
    }

    public AccountId AccountId { get; }
    public decimal Debit { get; }
    public decimal Credit { get; }
    public string Description { get; }
}

public sealed record GeneralLedgerEntry(
    LedgerEntryId Id,
    CompanyId CompanyId,
    AccountId AccountId,
    decimal Debit,
    decimal Credit,
    DateTimeOffset PostedAt,
    JournalId SourceJournalId,
    DateOnly PostingDate);

public sealed class GeneralJournal : AggregateRoot<JournalId>
{
    private readonly List<GeneralJournalLine> _lines = [];

    private GeneralJournal()
    {
        Currency = new Currency("LKR", 2);
        Number = string.Empty;
        Description = string.Empty;
        Source = string.Empty;
    }

    private GeneralJournal(
        JournalId id, CompanyId companyId, Currency currency, string number, DateOnly journalDate,
        string description, string source, string? sourceReference) : base(id)
    {
        CompanyId = companyId;
        Currency = currency;
        Number = number ?? string.Empty;
        JournalDate = journalDate;
        Description = description ?? string.Empty;
        Source = string.IsNullOrWhiteSpace(source) ? "Manual" : source.Trim();
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
    }

    public CompanyId CompanyId { get; private set; }
    public Currency Currency { get; private set; }
    public string Number { get; private set; }
    public DateOnly JournalDate { get; private set; }
    public string Description { get; private set; }
    public string Source { get; private set; }
    public string? SourceReference { get; private set; }
    public IReadOnlyCollection<GeneralJournalLine> Lines => _lines.AsReadOnly();
    public bool IsPosted { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public UserId? PostedBy { get; private set; }
    public JournalId? ReversalOfJournalId { get; private set; }
    public bool IsReversed { get; private set; }

    public decimal TotalDebit => _lines.Sum(x => x.Debit);
    public decimal TotalCredit => _lines.Sum(x => x.Credit);

    public static GeneralJournal Create(
        CompanyId companyId, Currency currency, string number, DateOnly journalDate,
        string description, string source = "Manual", string? sourceReference = null) =>
        new(new JournalId(Guid.NewGuid()), companyId, currency, number, journalDate, description, source, sourceReference);

    public void AddDebit(AccountId accountId, decimal amount, string description) =>
        AddLine(new GeneralJournalLine(accountId, amount, 0, description));

    public void AddCredit(AccountId accountId, decimal amount, string description) =>
        AddLine(new GeneralJournalLine(accountId, 0, amount, description));

    /// <summary>Posts the journal and returns the immutable ledger entries it produces.</summary>
    public IReadOnlyList<GeneralLedgerEntry> Post(DateTimeOffset at, UserId? by)
    {
        if (IsPosted) throw new DomainException("The journal is already posted.");
        if (_lines.Count < 2 || TotalDebit != TotalCredit)
        {
            throw new DomainException("A journal must have at least two lines and equal debit and credit totals before posting.");
        }

        IsPosted = true;
        PostedAt = at;
        PostedBy = by;
        return _lines
            .Select(x => new GeneralLedgerEntry(
                new LedgerEntryId(Guid.NewGuid()), CompanyId, x.AccountId, x.Debit, x.Credit, at, Id, JournalDate))
            .ToArray();
    }

    /// <summary>Builds an unposted mirror journal and marks this one as reversed (ADR 0003: corrections never edit history).</summary>
    public GeneralJournal CreateReversal(string number, DateOnly journalDate, string description)
    {
        if (!IsPosted) throw new DomainException("Only posted journals can be reversed.");
        if (IsReversed) throw new DomainException("The journal has already been reversed.");
        if (ReversalOfJournalId is not null) throw new DomainException("A reversal journal cannot be reversed.");

        var reversal = new GeneralJournal(
            new JournalId(Guid.NewGuid()), CompanyId, new Currency(Currency.Code, Currency.DecimalPlaces), number, journalDate, description, Source, SourceReference)
        {
            ReversalOfJournalId = Id
        };
        foreach (var line in _lines)
            reversal._lines.Add(new GeneralJournalLine(line.AccountId, line.Credit, line.Debit, line.Description));
        IsReversed = true;
        return reversal;
    }

    private void AddLine(GeneralJournalLine line)
    {
        if (IsPosted) throw new DomainException("Posted journals cannot be changed.");
        _lines.Add(line);
    }
}

/// <summary>Maps a posting role (e.g. "AccountsReceivable") to a ledger account for one company. No row means the role's default code applies.</summary>
public sealed class AccountMapping : AggregateRoot<AccountMappingId>
{
    private AccountMapping()
    {
        Role = string.Empty;
    }

    private AccountMapping(AccountMappingId id, CompanyId companyId, string role, AccountId accountId, DateTimeOffset at, UserId? by) : base(id)
    {
        CompanyId = companyId;
        Role = string.IsNullOrWhiteSpace(role) ? throw new DomainException("A role is required.") : role.Trim();
        AccountId = accountId;
        ModifiedAt = at;
        ModifiedBy = by;
    }

    public CompanyId CompanyId { get; private set; }
    public string Role { get; private set; }
    public AccountId AccountId { get; private set; }
    public DateTimeOffset ModifiedAt { get; private set; }
    public UserId? ModifiedBy { get; private set; }

    public static AccountMapping Create(CompanyId companyId, string role, AccountId accountId, DateTimeOffset at, UserId? by) =>
        new(new AccountMappingId(Guid.NewGuid()), companyId, role, accountId, at, by);

    public void Reassign(AccountId accountId, DateTimeOffset at, UserId? by)
    {
        AccountId = accountId;
        ModifiedAt = at;
        ModifiedBy = by;
    }
}

/// <summary>A named tax rate (e.g. VAT18 = 18%). Documents record the rate they were issued at, so changing a rate affects new documents only.</summary>
public sealed class TaxCode : AggregateRoot<TaxCodeId>
{
    private TaxCode()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private TaxCode(TaxCodeId id, CompanyId companyId, string code, string name, decimal ratePercent, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Code = NormalizeCode(code);
        Name = NormalizeName(name);
        RatePercent = ValidateRate(ratePercent);
        IsActive = true;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public decimal RatePercent { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static TaxCode Create(CompanyId companyId, string code, string name, decimal ratePercent, DateTimeOffset createdAt) =>
        new(new TaxCodeId(Guid.NewGuid()), companyId, code, name, ratePercent, createdAt);

    public static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? throw new DomainException("Tax code is required.") : code.Trim().ToUpperInvariant();

    public void Update(string name, decimal ratePercent)
    {
        Name = NormalizeName(name);
        RatePercent = ValidateRate(ratePercent);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private static string NormalizeName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("Tax code name is required.") : name.Trim();

    private static decimal ValidateRate(decimal rate) =>
        rate is < 0 or > 100 ? throw new DomainException("A tax rate must be between 0% and 100%.") : decimal.Round(rate, 4);
}
