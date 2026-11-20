namespace Erp.Application.Abstractions;

using Erp.SharedKernel;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ICurrentUser
{
    UserId? UserId { get; }
    bool IsAuthenticated { get; }
}

public interface ICompanyContext
{
    CompanyId CompanyId { get; }
    bool HasCompany { get; }
}

public interface IAuditRequestContext
{
    string CorrelationId { get; }
    string? IpAddress { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class UnavailableCompanyContext : ICompanyContext
{
    public CompanyId CompanyId => throw new InvalidOperationException("An active company context has not been configured.");
    public bool HasCompany => false;
}

/// <summary>Lets business modules ask whether posting on a date is allowed, without referencing Platform.</summary>
public interface IPostingPeriodGuard
{
    /// <summary>Throws <see cref="DomainException"/> unless an open accounting period covers the date for the active company.</summary>
    Task EnsurePostingAllowedAsync(DateOnly postingDate, CancellationToken cancellationToken);
}

/// <summary>Issues company-scoped document numbers (backed by Platform number sequences).</summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(string sequenceCode, string defaultPrefix, CancellationToken cancellationToken);
}

/// <summary>Records audit history for business modules without referencing Platform.</summary>
public interface IAuditRecorder
{
    Task RecordAsync(
        string category,
        string action,
        string entityType,
        string? entityId,
        string? entityDisplay,
        bool succeeded,
        IReadOnlyDictionary<string, object?>? metadata,
        CancellationToken cancellationToken);
}
