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

    /// <summary>The retention for a category: its own values, else the company's, else the system defaults (either row may be absent).</summary>
    public (int ReadDays, int UnreadDays) EffectiveFor(NotificationRetentionSetting? company, NotificationRetentionSetting? category) =>
        Effective(category?.ReadDays ?? company?.ReadDays, category?.UnreadDays ?? company?.UnreadDays);

    /// <summary>
    /// Problems with overrides (null means inherit), judged against the values that would then apply. <paramref name="inherited"/> is what
    /// a null falls back to: the system defaults for a company, the company's effective values for a category.
    /// </summary>
    public IReadOnlyList<string> ValidateOverrides(int? readOverride, int? unreadOverride, (int Read, int Unread)? inherited = null, string inheritedLabel = "the system default")
    {
        var errors = new List<string>();
        if (readOverride is < 1 or > 3650) errors.Add($"Read days must be between 1 and 3650 (was {readOverride}).");
        if (unreadOverride is < 0 or > 3650) errors.Add($"Unread days must be 0 (keep forever) or between 1 and 3650 (was {unreadOverride}).");
        if (errors.Count > 0) return errors;
        var read = readOverride ?? inherited?.Read ?? ReadDays;
        var unread = unreadOverride ?? inherited?.Unread ?? UnreadDays;
        if (unread != 0 && unread < read)
            errors.Add($"Unread days ({unread}{(unreadOverride is null ? ", " + inheritedLabel : "")}) must not be shorter than read days "
                + $"({read}{(readOverride is null ? ", " + inheritedLabel : "")}): an unread message should outlive a read one.");
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
public sealed record NotificationRetentionResponse(bool Enabled, int ReadDays, int UnreadDays, IReadOnlyList<CategoryRetentionSummary> Categories);

/// <summary>A notification category that is kept for different periods from the rest of the company's messages.</summary>
public sealed record CategoryRetentionSummary(string Category, int ReadDays, int UnreadDays);

/// <summary>A company's retention settings next to the system defaults; a null override inherits the default.</summary>
public sealed record NotificationRetentionSettingsResponse(
    bool Enabled, int SystemReadDays, int SystemUnreadDays, int? ReadDaysOverride, int? UnreadDaysOverride,
    int EffectiveReadDays, int EffectiveUnreadDays, DateTimeOffset? UpdatedAt, string? UpdatedBy,
    IReadOnlyList<CategoryRetentionResponse> Categories);

/// <summary>One notification category's retention: its own override (null inherits the company's value) and what is in effect.</summary>
public sealed record CategoryRetentionResponse(
    string Category, int? ReadDaysOverride, int? UnreadDaysOverride, int EffectiveReadDays, int EffectiveUnreadDays, DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record UpdateNotificationRetentionRequest(int? ReadDays, int? UnreadDays);

/// <summary>The notification categories the platform's modules send, so they can be given a retention before any message exists.</summary>
public static class NotificationCategories
{
    /// <summary>Categories whose email cannot be switched off by the recipient (security notices, ADR 0034). They are not offered as choices.</summary>
    public static readonly IReadOnlyList<string> Mandatory = ["account"];

    public static readonly IReadOnlyList<string> Known = ["approval", "decision", "low-stock", "overdue-bill", "overdue-invoice", "period-close"];
}

/// <summary>
/// What a purge covers: one company (<see cref="Only"/>) or every company but some (<see cref="Except"/>); and, within a company, one
/// category (<see cref="Category"/>) or every category but some (<see cref="ExceptCategories"/>).
/// </summary>
public sealed record PurgeScope(CompanyId? Only, IReadOnlyCollection<CompanyId>? Except, string? Category = null, IReadOnlyCollection<string>? ExceptCategories = null)
{
    public static PurgeScope Company(CompanyId id, IReadOnlyCollection<string>? exceptCategories = null) => new(id, null, null, exceptCategories);
    public static PurgeScope CompanyCategory(CompanyId id, string category) => new(id, null, category);
    public static PurgeScope AllExcept(IReadOnlyCollection<CompanyId> ids) => new(null, ids);
}

public interface INotificationRetentionSettings
{
    Task<NotificationRetentionSettingsResponse> GetAsync(CancellationToken cancellationToken);
    Task<NotificationRetentionSettingsResponse> UpdateAsync(UpdateNotificationRetentionRequest request, CancellationToken cancellationToken);
    /// <summary>Sets (or, with both values null, clears) the retention of one notification category for the active company.</summary>
    Task<NotificationRetentionSettingsResponse> UpdateCategoryAsync(string category, UpdateNotificationRetentionRequest request, CancellationToken cancellationToken);
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

        // Most specific first: a category with its own setting, then the rest of that company, then every company without settings.
        foreach (var company in overrides.GroupBy(x => x.CompanyId))
        {
            var companyRow = company.SingleOrDefault(x => x.IsCompanyWide);
            var categories = company.Where(x => !x.IsCompanyWide).ToList();
            foreach (var category in categories)
            {
                var (categoryRead, categoryUnread) = options.EffectiveFor(companyRow, category);
                var purged = await PurgeAsync(PurgeScope.CompanyCategory(company.Key, category.Category), categoryRead, categoryUnread, now, cancellationToken);
                read += purged.Read;
                unread += purged.Unread;
            }

            var (readDays, unreadDays) = options.Effective(companyRow?.ReadDays, companyRow?.UnreadDays);
            var result = await PurgeAsync(PurgeScope.Company(company.Key, categories.Count == 0 ? null : categories.Select(x => x.Category).ToArray()),
                readDays, unreadDays, now, cancellationToken);
            read += result.Read;
            unread += result.Unread;
        }

        var defaults = await PurgeAsync(PurgeScope.AllExcept(overrides.Select(x => x.CompanyId).Distinct().ToArray()), options.ReadDays, options.UnreadDays, now, cancellationToken);
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

    public Task<NotificationRetentionSettingsResponse> GetAsync(CancellationToken cancellationToken) => MapAsync(cancellationToken);

    public async Task<NotificationRetentionSettingsResponse> UpdateAsync(UpdateNotificationRetentionRequest request, CancellationToken cancellationToken)
    {
        if (options.ValidateOverrides(request.ReadDays, request.UnreadDays) is { Count: > 0 } errors)
            throw new DomainException(string.Join(" ", errors));
        await ApplyAsync(NotificationRetentionSetting.CompanyWide, request, "notification-retention.updated", cancellationToken);
        return await MapAsync(cancellationToken);
    }

    public async Task<NotificationRetentionSettingsResponse> UpdateCategoryAsync(string category, UpdateNotificationRetentionRequest request, CancellationToken cancellationToken)
    {
        var key = NotificationRetentionSetting.NormaliseCategory(category);
        if (key.Length == 0) throw new DomainException("A category is required.");
        var company = await store.GetRetentionSettingAsync(Company, NotificationRetentionSetting.CompanyWide, cancellationToken);
        if (options.ValidateOverrides(request.ReadDays, request.UnreadDays, options.Effective(company?.ReadDays, company?.UnreadDays), "inherited from the company setting") is { Count: > 0 } errors)
            throw new DomainException(string.Join(" ", errors));
        await ApplyAsync(key, request, "notification-retention.category-updated", cancellationToken);
        return await MapAsync(cancellationToken);
    }

    private async Task ApplyAsync(string category, UpdateNotificationRetentionRequest request, string action, CancellationToken cancellationToken)
    {
        var setting = await store.GetRetentionSettingAsync(Company, category, cancellationToken);
        var before = $"read {setting?.ReadDays?.ToString() ?? "inherit"}, unread {setting?.UnreadDays?.ToString() ?? "inherit"}";
        if (request.ReadDays is null && request.UnreadDays is null)
        {
            // Inheriting everything is the absence of a row.
            if (setting is not null) store.RemoveRetentionSetting(setting);
        }
        else
        {
            if (setting is null)
            {
                setting = NotificationRetentionSetting.Create(Company, category);
                await store.AddRetentionSettingAsync(setting, cancellationToken);
            }

            setting.Set(request.ReadDays, request.UnreadDays, clock.UtcNow, currentUser.UserId);
        }

        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Notifications", action, "Company", Company.Value.ToString(), null, AuditOutcome.Succeeded,
            new Dictionary<string, object?>
            {
                ["Category"] = category.Length == 0 ? "(whole company)" : category,
                ["Before"] = before,
                ["After"] = $"read {request.ReadDays?.ToString() ?? "inherit"}, unread {request.UnreadDays?.ToString() ?? "inherit"}"
            }, cancellationToken);
        await audit.SaveAsync(cancellationToken);
    }

    private async Task<NotificationRetentionSettingsResponse> MapAsync(CancellationToken cancellationToken)
    {
        var rows = await store.ListRetentionSettingsAsync(Company, cancellationToken);
        var company = rows.SingleOrDefault(x => x.IsCompanyWide);
        var (read, unread) = options.Effective(company?.ReadDays, company?.UnreadDays);

        var names = NotificationCategories.Known
            .Concat(await store.ListCategoriesAsync(Company, cancellationToken))
            .Concat(rows.Where(x => !x.IsCompanyWide).Select(x => x.Category))
            .Distinct().Order().ToList();
        var categories = new List<CategoryRetentionResponse>();
        foreach (var name in names)
        {
            var row = rows.SingleOrDefault(x => x.Category == name);
            var (categoryRead, categoryUnread) = options.EffectiveFor(company, row);
            categories.Add(new CategoryRetentionResponse(name, row?.ReadDays, row?.UnreadDays, categoryRead, categoryUnread, row?.UpdatedAt, await NameAsync(row, cancellationToken)));
        }

        return new NotificationRetentionSettingsResponse(options.Enabled, options.ReadDays, options.UnreadDays, company?.ReadDays, company?.UnreadDays,
            read, unread, company?.UpdatedAt, await NameAsync(company, cancellationToken), categories);
    }

    private async Task<string?> NameAsync(NotificationRetentionSetting? setting, CancellationToken cancellationToken) =>
        setting?.UpdatedBy is { } id ? (await users.GetByIdAsync(id, cancellationToken))?.UserName : null;
}
