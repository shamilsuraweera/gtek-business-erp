using Erp.SharedKernel;

namespace Erp.Modules.Finance.Domain;

public readonly record struct AccountId(Guid Value);
public readonly record struct JournalId(Guid Value);
public readonly record struct LedgerEntryId(Guid Value);

public enum AccountType { Asset, Liability, Equity, Revenue, Expense }

public sealed class Account : AggregateRoot<AccountId>
{
    private Account(AccountId id, CompanyId companyId, string code, string name, AccountType type) : base(id)
    {
        CompanyId = companyId;
        Code = string.IsNullOrWhiteSpace(code) ? throw new DomainException("Account code is required.") : code.Trim();
        Name = string.IsNullOrWhiteSpace(name) ? throw new DomainException("Account name is required.") : name.Trim();
        Type = type;
    }

    public CompanyId CompanyId { get; }
    public string Code { get; }
    public string Name { get; }
    public AccountType Type { get; }

    public static Account Create(CompanyId companyId, string code, string name, AccountType type) =>
        new(new AccountId(Guid.NewGuid()), companyId, code, name, type);
}

public sealed record Currency(string Code, int DecimalPlaces);

public sealed record AccountingPeriod(CompanyId CompanyId, int Year, int Month, bool IsClosed)
{
    public void EnsureOpen()
    {
        if (IsClosed) throw new DomainException("The accounting period is closed.");
    }
}

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
        Description = description;
    }

    public AccountId AccountId { get; }
    public decimal Debit { get; }
    public decimal Credit { get; }
    public string Description { get; }
}

public sealed class GeneralJournal : AggregateRoot<JournalId>
{
    private readonly List<GeneralJournalLine> _lines = [];

    private GeneralJournal(JournalId id, CompanyId companyId, Currency currency) : base(id)
    {
        CompanyId = companyId;
        Currency = currency;
    }

    public CompanyId CompanyId { get; }
    public Currency Currency { get; }
    public IReadOnlyCollection<GeneralJournalLine> Lines => _lines.AsReadOnly();
    public bool IsPosted { get; private set; }

    public static GeneralJournal Create(CompanyId companyId, Currency currency) =>
        new(new JournalId(Guid.NewGuid()), companyId, currency);

    public void AddDebit(AccountId accountId, decimal amount, string description) =>
        AddLine(new GeneralJournalLine(accountId, amount, 0, description));

    public void AddCredit(AccountId accountId, decimal amount, string description) =>
        AddLine(new GeneralJournalLine(accountId, 0, amount, description));

    public void Post()
    {
        if (IsPosted) throw new DomainException("The journal is already posted.");
        if (_lines.Count == 0 || _lines.Sum(x => x.Debit) != _lines.Sum(x => x.Credit))
        {
            throw new DomainException("A journal must have equal debit and credit totals before posting.");
        }

        IsPosted = true;
    }

    private void AddLine(GeneralJournalLine line)
    {
        if (IsPosted) throw new DomainException("Posted journals cannot be changed.");
        _lines.Add(line);
    }
}

public sealed record GeneralLedgerEntry(
    LedgerEntryId Id,
    CompanyId CompanyId,
    AccountId AccountId,
    decimal Debit,
    decimal Credit,
    DateTimeOffset PostedAt,
    JournalId SourceJournalId);
