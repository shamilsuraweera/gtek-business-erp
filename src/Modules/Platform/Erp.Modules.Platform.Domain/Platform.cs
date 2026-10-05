using Erp.SharedKernel;

namespace Erp.Modules.Platform.Domain;

public enum CompanyStatus
{
    Active,
    Inactive
}

public sealed class Company : AggregateRoot<CompanyId>
{
    private Company()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Company(CompanyId id, string code, string name, DateTimeOffset createdAt) : base(id)
    {
        Code = NormalizeCode(code);
        Name = ValidateName(name);
        CreatedAt = createdAt;
        Status = CompanyStatus.Active;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public CompanyStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Company Create(string code, string name, DateTimeOffset createdAt) =>
        new(CompanyId.New(), code, name, createdAt);

    public void Rename(string name)
    {
        Name = ValidateName(name);
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        Status = CompanyStatus.Active;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        Status = CompanyStatus.Inactive;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Company code is required.");

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 20 || normalized.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_'))
            throw new DomainException("Company code must contain 2 to 20 letters, numbers, hyphens, or underscores.");

        return normalized;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Company name is required.");

        return name.Trim();
    }
}

public sealed record User(UserId Id, CompanyId CompanyId, string DisplayName);
public sealed record Role(Guid Id, CompanyId CompanyId, string Name);
public sealed record Permission(Guid Id, string Code);
