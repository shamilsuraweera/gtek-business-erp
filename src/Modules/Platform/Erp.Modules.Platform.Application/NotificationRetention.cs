using Erp.Application.Abstractions;

namespace Erp.Modules.Platform.Application;

/// <summary>
/// How long notifications are kept (configuration section <c>Notifications:Retention</c>, system-wide). Read messages go
/// <see cref="ReadDays"/> after they were read; unread ones <see cref="UnreadDays"/> after they were sent, or never when that is 0.
/// </summary>
public sealed class NotificationRetentionOptions
{
    public const string Section = "Notifications:Retention";

    /// <summary>Turns the scheduled purge off entirely (messages then accumulate).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Days a message is kept after it was read.</summary>
    public int ReadDays { get; set; } = 30;

    /// <summary>Days an unread message is kept after it was sent; 0 keeps unread messages forever.</summary>
    public int UnreadDays { get; set; } = 180;

    /// <summary>Minutes between purges.</summary>
    public int IntervalMinutes { get; set; } = 1440;

    /// <summary>Seconds to wait after the API starts before the first purge.</summary>
    public int InitialDelaySeconds { get; set; } = 60;

    /// <summary>Rows deleted per statement, so a large backlog never holds one long lock.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Every problem with the values, so a bad configuration fails at startup with a clear message.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (ReadDays is < 1 or > 3650) errors.Add($"{Section}:ReadDays must be between 1 and 3650 (was {ReadDays}).");
        if (UnreadDays is < 0 or > 3650) errors.Add($"{Section}:UnreadDays must be 0 (keep forever) or between 1 and 3650 (was {UnreadDays}).");
        else if (UnreadDays != 0 && UnreadDays < ReadDays)
            errors.Add($"{Section}:UnreadDays ({UnreadDays}) must not be shorter than ReadDays ({ReadDays}): an unread message should outlive a read one.");
        if (IntervalMinutes is < 1 or > 10080) errors.Add($"{Section}:IntervalMinutes must be between 1 and 10080 (was {IntervalMinutes}).");
        if (InitialDelaySeconds is < 0 or > 3600) errors.Add($"{Section}:InitialDelaySeconds must be between 0 and 3600 (was {InitialDelaySeconds}).");
        if (BatchSize is < 100 or > 10000) errors.Add($"{Section}:BatchSize must be between 100 and 10000 (was {BatchSize}).");
        return errors;
    }
}

public sealed record PurgeResult(int Read, int Unread)
{
    public int Total => Read + Unread;
}

public sealed record NotificationRetentionResponse(bool Enabled, int ReadDays, int UnreadDays);

public interface INotificationRetention
{
    /// <summary>Deletes every notification (in all companies) that is past its retention; safe to run at any time and repeatedly.</summary>
    Task<PurgeResult> PurgeAsync(CancellationToken cancellationToken);
}

public sealed class NotificationRetention(INotificationStore store, NotificationRetentionOptions options, IClock clock) : INotificationRetention
{
    public async Task<PurgeResult> PurgeAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return await store.PurgeAsync(
            readBefore: now.AddDays(-options.ReadDays),
            unreadBefore: options.UnreadDays == 0 ? null : now.AddDays(-options.UnreadDays),
            options.BatchSize,
            cancellationToken);
    }
}
