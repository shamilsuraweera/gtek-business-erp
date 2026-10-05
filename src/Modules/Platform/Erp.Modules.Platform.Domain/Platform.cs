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

public enum UserCompanyAccessStatus
{
    Active,
    Inactive
}

public sealed class UserCompanyAccess
{
    private UserCompanyAccess()
    {
    }

    private UserCompanyAccess(
        UserId userId,
        CompanyId companyId,
        UserId grantedBy,
        DateTimeOffset createdAt)
    {
        UserId = userId;
        CompanyId = companyId;
        CreatedAt = createdAt;
        CreatedBy = grantedBy;
        Status = UserCompanyAccessStatus.Active;
    }

    public UserId UserId { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public UserCompanyAccessStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public UserId? ModifiedBy { get; private set; }

    public static UserCompanyAccess Create(UserId userId, CompanyId companyId, UserId grantedBy, DateTimeOffset createdAt) =>
        new(userId, companyId, grantedBy, createdAt);

    public void Activate(UserId actor, DateTimeOffset at)
    {
        Status = UserCompanyAccessStatus.Active;
        ModifiedAt = at;
        ModifiedBy = actor;
    }

    public void Deactivate(UserId actor, DateTimeOffset at)
    {
        Status = UserCompanyAccessStatus.Inactive;
        ModifiedAt = at;
        ModifiedBy = actor;
    }
}

public sealed record CompanyAccessGranted(UserId ActorUserId, UserId TargetUserId, CompanyId CompanyId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record CompanyAccessRevoked(UserId ActorUserId, UserId TargetUserId, CompanyId CompanyId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record CompanyAccessRestored(UserId ActorUserId, UserId TargetUserId, CompanyId CompanyId, DateTimeOffset OccurredAt) : IDomainEvent;

public enum RoleStatus
{
    Active,
    Inactive
}

public sealed class Role : AggregateRoot<RoleId>
{
    private Role()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Role(RoleId id, string code, string name, string? description, bool isSystem, DateTimeOffset createdAt)
        : base(id)
    {
        Code = NormalizeCode(code);
        Name = ValidateText(name, "Role name");
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsSystem = isSystem;
        CreatedAt = createdAt;
        Status = RoleStatus.Active;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public RoleStatus Status { get; private set; }
    public bool IsSystem { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Role Create(string code, string name, string? description, bool isSystem, DateTimeOffset createdAt) =>
        new(RoleId.New(), code, name, description, isSystem, createdAt);

    public void Rename(string name)
    {
        Name = ValidateText(name, "Role name");
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        Status = RoleStatus.Active;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        Status = RoleStatus.Inactive;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Role code is required.");
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 100 ||
            normalized.Any(character => !char.IsLetterOrDigit(character) && character != '_' && character != '-'))
            throw new DomainException("Role code must contain 2 to 100 letters, numbers, hyphens, or underscores.");
        return normalized;
    }

    private static string ValidateText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required.");
        return value.Trim();
    }
}

public sealed class Permission : AggregateRoot<PermissionId>
{
    private Permission()
    {
        Code = string.Empty;
        Name = string.Empty;
        Module = string.Empty;
    }

    private Permission(PermissionId id, string code, string name, string module, string? description, DateTimeOffset createdAt)
        : base(id)
    {
        Code = NormalizeCode(code);
        Name = Validate(name, "Permission name");
        Module = Validate(module, "Permission module");
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CreatedAt = createdAt;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public string Module { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Permission Create(string code, string name, string module, string? description, DateTimeOffset createdAt) =>
        new(PermissionId.New(), code, name, module, description, createdAt);

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Permission code is required.");
        var normalized = code.Trim().ToLowerInvariant();
        var parts = normalized.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace) ||
            normalized.Any(character => !char.IsLetterOrDigit(character) && character != '.' && character != '_' && character != '-'))
            throw new DomainException("Permission code must use module.resource.action format.");
        return normalized;
    }

    private static string Validate(string value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException($"{field} is required.") : value.Trim();
}

public sealed record UserRole(UserId UserId, RoleId RoleId);
public sealed record RolePermission(RoleId RoleId, PermissionId PermissionId);
