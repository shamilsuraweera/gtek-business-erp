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
/// <param name="Mandatory">A security notice about the recipient's own account (ADR 0034): sent by email whatever the recipient's email preferences, and said so in the email.</param>
public sealed record NotificationMessage(string Category, string Title, string? Body, string? Link, string? Subject = null, bool Email = false, bool Mandatory = false);

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
/// per company, and not before <see cref="Hour"/> o'clock in <see cref="TimeZone"/> (ADR 0036). Only the schedule uses the time zone: what counts
/// as overdue or ended is judged on UTC dates like everywhere else in the application.
/// </summary>
public sealed class ReminderOptions
{
    public const string Section = "Reminders";

    public bool Enabled { get; set; } = true;
    /// <summary>The time zone the schedule follows: an IANA id such as "Asia/Colombo" (or a Windows id on Windows). "UTC" by default.</summary>
    public string TimeZone { get; set; } = "UTC";
    /// <summary>Earliest hour of the day, in <see cref="TimeZone"/>, a reminder is sent.</summary>
    public int Hour { get; set; } = 8;
    /// <summary>A reminder is repeated after this many calendar days while the condition lasts.</summary>
    public int RepeatDays { get; set; } = 1;
    public int IntervalMinutes { get; set; } = 60;
    public int InitialDelaySeconds { get; set; } = 120;
    /// <summary>Days after an accounting period ends before it is reminded about as still open: time to finish the month's postings.</summary>
    public int PeriodCloseGraceDays { get; set; } = 3;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Hour is < 0 or > 23) errors.Add($"{Section}:Hour must be between 0 and 23 (was {Hour}).");
        if (RepeatDays is < 1 or > 90) errors.Add($"{Section}:RepeatDays must be between 1 and 90 (was {RepeatDays}).");
        if (IntervalMinutes is < 1 or > 1440) errors.Add($"{Section}:IntervalMinutes must be between 1 and 1440 (was {IntervalMinutes}).");
        if (InitialDelaySeconds is < 0 or > 3600) errors.Add($"{Section}:InitialDelaySeconds must be between 0 and 3600 (was {InitialDelaySeconds}).");
        if (PeriodCloseGraceDays is < 0 or > 60) errors.Add($"{Section}:PeriodCloseGraceDays must be between 0 and 60 (was {PeriodCloseGraceDays}).");
        if (!TryResolveTimeZone(out _)) errors.Add($"{Section}:TimeZone '{TimeZone}' is not a time zone this system knows (use an IANA id such as Asia/Colombo or UTC).");
        return errors;
    }

    /// <summary>Whether a reminder last sent at <paramref name="lastSent"/> is due at <paramref name="now"/>, by the clock and calendar of <see cref="TimeZone"/>.</summary>
    public bool IsDue(DateTimeOffset now, DateTimeOffset? lastSent)
    {
        var zone = TryResolveTimeZone(out var found) ? found! : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        if (local.Hour < Hour) return false;
        return lastSent is not { } last || DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(last, zone).DateTime) <= DateOnly.FromDateTime(local.DateTime).AddDays(-RepeatDays);
    }

    private bool TryResolveTimeZone(out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(TimeZone) ? "UTC" : TimeZone.Trim());
            return true;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = null;
            return false;
        }
    }
}

/// <summary>
/// A named lock shared by every instance of the application (backed by the database), so scheduled work that must happen once, such as sending
/// a company's daily reminder, does not run in two instances at the same moment (ADR 0035).
/// </summary>
public interface IJobLock
{
    /// <summary>The lock if nobody holds it (dispose to release it), or null straight away if another instance does. Never waits.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken);
}

/// <summary>How much of each item is on its way from vendors, so other modules can tell "low" from "low but already reordered" (ADR 0037).</summary>
public interface IOnOrderQuantities
{
    /// <summary>The quantity still to be received on the active company's orders placed with vendors, by item id. Items with nothing on order are absent.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetAsync(CancellationToken cancellationToken);
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
