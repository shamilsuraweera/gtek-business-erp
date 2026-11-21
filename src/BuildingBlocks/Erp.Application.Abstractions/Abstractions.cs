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

    /// <summary>When the active company was last sent a notification about the subject, or null if never.</summary>
    Task<DateTimeOffset?> LastSentAsync(string subject, CancellationToken cancellationToken);
}

/// <summary>
/// Scheduled reminders (ADR 0029; configuration section <c>Reminders</c>). A reminder is sent at most once per <see cref="RepeatDays"/> calendar days
/// per company, and not before <see cref="HourUtc"/> o'clock UTC.
/// </summary>
public sealed class ReminderOptions
{
    public const string Section = "Reminders";

    public bool Enabled { get; set; } = true;
    /// <summary>Earliest hour of the day (UTC) a reminder is sent.</summary>
    public int HourUtc { get; set; } = 8;
    /// <summary>A reminder is repeated after this many calendar days while the condition lasts.</summary>
    public int RepeatDays { get; set; } = 1;
    public int IntervalMinutes { get; set; } = 60;
    public int InitialDelaySeconds { get; set; } = 120;
    /// <summary>Days after an accounting period ends before it is reminded about as still open: time to finish the month's postings.</summary>
    public int PeriodCloseGraceDays { get; set; } = 3;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (HourUtc is < 0 or > 23) errors.Add($"{Section}:HourUtc must be between 0 and 23 (was {HourUtc}).");
        if (RepeatDays is < 1 or > 90) errors.Add($"{Section}:RepeatDays must be between 1 and 90 (was {RepeatDays}).");
        if (IntervalMinutes is < 1 or > 1440) errors.Add($"{Section}:IntervalMinutes must be between 1 and 1440 (was {IntervalMinutes}).");
        if (InitialDelaySeconds is < 0 or > 3600) errors.Add($"{Section}:InitialDelaySeconds must be between 0 and 3600 (was {InitialDelaySeconds}).");
        if (PeriodCloseGraceDays is < 0 or > 60) errors.Add($"{Section}:PeriodCloseGraceDays must be between 0 and 60 (was {PeriodCloseGraceDays}).");
        return errors;
    }

    /// <summary>Whether a reminder last sent at <paramref name="lastSent"/> is due at <paramref name="now"/>.</summary>
    public bool IsDue(DateTimeOffset now, DateTimeOffset? lastSent)
    {
        var utc = now.UtcDateTime;
        if (utc.Hour < HourUtc) return false;
        return lastSent is not { } last || DateOnly.FromDateTime(last.UtcDateTime) <= DateOnly.FromDateTime(utc).AddDays(-RepeatDays);
    }
}

/// <summary>A scheduled check a module runs for the active company; it notifies through <see cref="INotifier"/> when something needs attention.</summary>
public interface IReminderJob
{
    /// <summary>Notifications sent by this run (0 when nothing is due or nothing needs attention).</summary>
    Task<int> RunAsync(CancellationToken cancellationToken);
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
