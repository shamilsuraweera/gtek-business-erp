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

public enum UserStatus
{
    Active,
    Inactive
}

public sealed class User : AggregateRoot<UserId>
{
    private User()
    {
        UserName = string.Empty;
        Email = string.Empty;
    }

    private User(UserId id, string userName, string email, DateTimeOffset createdAt, bool isBootstrapAdministrator)
        : base(id)
    {
        UserName = NormalizeUserName(userName);
        Email = NormalizeEmail(email);
        CreatedAt = createdAt;
        Status = UserStatus.Active;
        IsBootstrapAdministrator = isBootstrapAdministrator;
    }

    public string UserName { get; private set; }
    public string Email { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public bool IsBootstrapAdministrator { get; private set; }

    public static User Create(string userName, string email, DateTimeOffset createdAt, bool isBootstrapAdministrator = false) =>
        new(UserId.New(), userName, email, createdAt, isBootstrapAdministrator);

    public void ChangeEmail(string email)
    {
        Email = NormalizeEmail(email);
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        Status = UserStatus.Active;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        Status = UserStatus.Inactive;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public static string NormalizeUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("Username is required.");

        var normalized = userName.Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 100 ||
            normalized.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new DomainException("Username must contain 3 to 100 letters, numbers, periods, hyphens, or underscores.");

        return normalized;
    }

    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");

        var normalized = email.Trim().ToLowerInvariant();
        try
        {
            var address = new System.Net.Mail.MailAddress(normalized);
            if (!string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new DomainException("Email must be valid.");
        }

        return normalized;
    }
}
public sealed record Role(Guid Id, CompanyId CompanyId, string Name);
public sealed record Permission(Guid Id, string Code);
