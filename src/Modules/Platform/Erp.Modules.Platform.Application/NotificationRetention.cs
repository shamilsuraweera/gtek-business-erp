using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

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

    /// <summary>The retention that applies to a company: its own values where set, otherwise the system defaults.</summary>
    public (int ReadDays, int UnreadDays) Effective(int? readOverride, int? unreadOverride)
    {
        var read = readOverride ?? ReadDays;
        var unread = unreadOverride ?? UnreadDays;
        // A company's longer read period must never push an inherited unread period below it.
        if (unread != 0 && unread < read) unread = read;
        return (read, unread);
    }

    /// <summary>Problems with a company's overrides (null means inherit), judged against the values that would then apply.</summary>
    public IReadOnlyList<string> ValidateOverrides(int? readOverride, int? unreadOverride)
    {
        var errors = new List<string>();
        if (readOverride is < 1 or > 3650) errors.Add($"Read days must be between 1 and 3650 (was {readOverride}).");
        if (unreadOverride is < 0 or > 3650) errors.Add($"Unread days must be 0 (keep forever) or between 1 and 3650 (was {unreadOverride}).");
        if (errors.Count > 0) return errors;
        var read = readOverride ?? ReadDays;
        var unread = unreadOverride ?? UnreadDays;
        if (unread != 0 && unread < read)
            errors.Add($"Unread days ({unread}{(unreadOverride is null ? ", the system default" : "")}) must not be shorter than read days "
                + $"({read}{(readOverride is null ? ", the system default" : "")}): an unread message should outlive a read one.");
        return errors;
    }

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

/// <summary>The retention that applies to the active company.</summary>
public sealed record NotificationRetentionResponse(bool Enabled, int ReadDays, int UnreadDays);

/// <summary>A company's retention settings next to the system defaults; a null override inherits the default.</summary>
public sealed record NotificationRetentionSettingsResponse(
    bool Enabled, int SystemReadDays, int SystemUnreadDays, int? ReadDaysOverride, int? UnreadDaysOverride,
    int EffectiveReadDays, int EffectiveUnreadDays, DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record UpdateNotificationRetentionRequest(int? ReadDays, int? UnreadDays);

/// <summary>Which companies a purge covers.</summary>
public sealed record PurgeScope(CompanyId? Only, IReadOnlyCollection<CompanyId>? Except)
{
    public static PurgeScope Company(CompanyId id) => new(id, null);
    public static PurgeScope AllExcept(IReadOnlyCollection<CompanyId> ids) => new(null, ids);
}

public interface INotificationRetentionSettings
{
    Task<NotificationRetentionSettingsResponse> GetAsync(CancellationToken cancellationToken);
    Task<NotificationRetentionSettingsResponse> UpdateAsync(UpdateNotificationRetentionRequest request, CancellationToken cancellationToken);
}

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
        var overrides = await store.ListRetentionSettingsAsync(cancellationToken);
        var read = 0;
        var unread = 0;

        // Each company with its own settings is purged by them; everyone else by the system defaults.
        foreach (var setting in overrides)
        {
            var (readDays, unreadDays) = options.Effective(setting.ReadDays, setting.UnreadDays);
            var result = await PurgeAsync(PurgeScope.Company(setting.CompanyId), readDays, unreadDays, now, cancellationToken);
            read += result.Read;
            unread += result.Unread;
        }

        var defaults = await PurgeAsync(PurgeScope.AllExcept(overrides.Select(x => x.CompanyId).ToArray()), options.ReadDays, options.UnreadDays, now, cancellationToken);
        return new PurgeResult(read + defaults.Read, unread + defaults.Unread);
    }

    private Task<PurgeResult> PurgeAsync(PurgeScope scope, int readDays, int unreadDays, DateTimeOffset now, CancellationToken cancellationToken) =>
        store.PurgeAsync(scope, now.AddDays(-readDays), unreadDays == 0 ? null : now.AddDays(-unreadDays), options.BatchSize, cancellationToken);
}

public sealed class NotificationRetentionSettingsService(
    INotificationStore store, NotificationRetentionOptions options, ICompanyContext companyContext, ICurrentUser currentUser,
    IClock clock, IUserStore users, IAuditTrail audit) : INotificationRetentionSettings
{
    private CompanyId Company =>
        companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required.");

    public async Task<NotificationRetentionSettingsResponse> GetAsync(CancellationToken cancellationToken) =>
        await MapAsync(await store.GetRetentionSettingAsync(Company, cancellationToken), cancellationToken);

    public async Task<NotificationRetentionSettingsResponse> UpdateAsync(UpdateNotificationRetentionRequest request, CancellationToken cancellationToken)
    {
        if (options.ValidateOverrides(request.ReadDays, request.UnreadDays) is { Count: > 0 } errors)
            throw new DomainException(string.Join(" ", errors));

        var setting = await store.GetRetentionSettingAsync(Company, cancellationToken);
        var before = $"read {setting?.ReadDays?.ToString() ?? "default"}, unread {setting?.UnreadDays?.ToString() ?? "default"}";
        if (request.ReadDays is null && request.UnreadDays is null)
        {
            // Inheriting everything is the absence of a row.
            if (setting is not null) store.RemoveRetentionSetting(setting);
            setting = null;
        }
        else
        {
            if (setting is null)
            {
                setting = NotificationRetentionSetting.Create(Company);
                await store.AddRetentionSettingAsync(setting, cancellationToken);
            }

            setting.Set(request.ReadDays, request.UnreadDays, clock.UtcNow, currentUser.UserId);
        }

        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Notifications", "notification-retention.updated", "Company", Company.Value.ToString(), null, AuditOutcome.Succeeded,
            new Dictionary<string, object?>
            {
                ["Before"] = before,
                ["After"] = $"read {request.ReadDays?.ToString() ?? "default"}, unread {request.UnreadDays?.ToString() ?? "default"}"
            }, cancellationToken);
        await audit.SaveAsync(cancellationToken);
        return await MapAsync(setting, cancellationToken);
    }

    private async Task<NotificationRetentionSettingsResponse> MapAsync(NotificationRetentionSetting? setting, CancellationToken cancellationToken)
    {
        var (read, unread) = options.Effective(setting?.ReadDays, setting?.UnreadDays);
        var by = setting?.UpdatedBy is { } id ? (await users.GetByIdAsync(id, cancellationToken))?.UserName : null;
        return new NotificationRetentionSettingsResponse(options.Enabled, options.ReadDays, options.UnreadDays, setting?.ReadDays, setting?.UnreadDays,
            read, unread, setting?.UpdatedAt, by);
    }
}
