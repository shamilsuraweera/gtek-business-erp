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

/// <summary>Resolves user ids to display names, so business modules can show who did something without referencing Platform.</summary>
public interface IUserDirectory
{
    /// <summary>User names by id; unknown ids are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);
}

/// <param name="Category">A short grouping such as "approval" or "decision".</param>
/// <param name="Link">An in-app path the notification opens, e.g. "/tolerance-requests".</param>
/// <param name="Subject">Identifies what the notification is about (e.g. "tolerance-request:{id}"), so it can be resolved later.</param>
/// <param name="Email">Also send it by email to recipients who have email on (ADR 0026). Reserve it for things that need action or report a decision.</param>
public sealed record NotificationMessage(string Category, string Title, string? Body, string? Link, string? Subject = null, bool Email = false);

/// <summary>Delivers in-app notifications to users of the active company without referencing Platform.</summary>
public interface INotifier
{
    Task NotifyUsersAsync(IEnumerable<Guid> userIds, NotificationMessage message, CancellationToken cancellationToken);

    /// <summary>Notifies every active user of the active company who holds the permission, except one user (typically the actor).</summary>
    Task NotifyPermissionHoldersAsync(string permissionCode, NotificationMessage message, Guid? exceptUserId, CancellationToken cancellationToken);

    /// <summary>Marks every notification about a subject as read for everyone: it no longer needs anyone's attention.</summary>
    Task ResolveSubjectAsync(string subject, CancellationToken cancellationToken);
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
