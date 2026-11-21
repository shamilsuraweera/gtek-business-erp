using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Platform.Application;

public sealed record NotificationResponse(Guid Id, string Category, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, bool IsRead);
public sealed record UnreadCountResponse(int Count);

public interface INotificationStore
{
    Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken);
    Task<IReadOnlyList<Notification>> ListAsync(CompanyId companyId, UserId userId, bool unreadOnly, int take, CancellationToken cancellationToken);
    Task<int> CountUnreadAsync(CompanyId companyId, UserId userId, CancellationToken cancellationToken);
    Task<Notification?> GetAsync(CompanyId companyId, UserId userId, Guid id, CancellationToken cancellationToken);
    Task<int> MarkAllReadAsync(CompanyId companyId, UserId userId, DateTimeOffset at, CancellationToken cancellationToken);
    Task<int> MarkSubjectReadAsync(CompanyId companyId, string subject, DateTimeOffset at, CancellationToken cancellationToken);
    /// <summary>
    /// Deletes, within the scope, read notifications read before <paramref name="readBefore"/> and (if given) unread ones sent before
    /// <paramref name="unreadBefore"/>, in batches.
    /// </summary>
    Task<PurgeResult> PurgeAsync(PurgeScope scope, DateTimeOffset readBefore, DateTimeOffset? unreadBefore, int batchSize, CancellationToken cancellationToken);

    /// <summary>One row: the company-wide one for <see cref="NotificationRetentionSetting.CompanyWide"/>, or a category's.</summary>
    Task<NotificationRetentionSetting?> GetRetentionSettingAsync(CompanyId companyId, string category, CancellationToken cancellationToken);
    /// <summary>Every row of every company, for the purge.</summary>
    Task<IReadOnlyList<NotificationRetentionSetting>> ListRetentionSettingsAsync(CancellationToken cancellationToken);
    /// <summary>Every row of one company.</summary>
    Task<IReadOnlyList<NotificationRetentionSetting>> ListRetentionSettingsAsync(CompanyId companyId, CancellationToken cancellationToken);
    /// <summary>The newest notification time for a subject in a company.</summary>
    Task<DateTimeOffset?> LastForSubjectAsync(CompanyId companyId, string subject, CancellationToken cancellationToken);
    /// <summary>Which of these users have opted out of email for the category.</summary>
    Task<IReadOnlySet<UserId>> ListEmailOptOutsAsync(IReadOnlyCollection<UserId> userIds, string category, CancellationToken cancellationToken);
    /// <summary>The categories a user has opted out of email for.</summary>
    Task<IReadOnlyList<string>> ListEmailOptOutCategoriesAsync(UserId userId, CancellationToken cancellationToken);
    /// <summary>Records or removes one opt-out (idempotent) and saves.</summary>
    Task SetEmailOptOutAsync(UserId userId, string category, bool optedOut, CancellationToken cancellationToken);
    /// <summary>The categories the company has notifications in.</summary>
    Task<IReadOnlyList<string>> ListCategoriesAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task AddRetentionSettingAsync(NotificationRetentionSetting setting, CancellationToken cancellationToken);
    void RemoveRetentionSetting(NotificationRetentionSetting setting);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>A user's own notifications for the active company.</summary>
public interface INotificationService
{
    Task<IReadOnlyList<NotificationResponse>> ListAsync(bool unreadOnly, int take, CancellationToken cancellationToken);
    Task<int> UnreadCountAsync(CancellationToken cancellationToken);
    Task<NotificationResponse?> MarkReadAsync(Guid id, CancellationToken cancellationToken);
    Task<int> MarkAllReadAsync(CancellationToken cancellationToken);
    /// <summary>How long the active company keeps notifications (its own setting, else the system default).</summary>
    Task<NotificationRetentionResponse> GetRetentionAsync(CancellationToken cancellationToken);
}

public sealed class NotificationService(
    INotificationStore store, ICurrentUser currentUser, ICompanyContext companyContext, IClock clock, NotificationRetentionOptions retention) : INotificationService
{
    public async Task<NotificationRetentionResponse> GetRetentionAsync(CancellationToken cancellationToken)
    {
        var rows = await store.ListRetentionSettingsAsync(Scope().Company, cancellationToken);
        var company = rows.SingleOrDefault(x => x.IsCompanyWide);
        var (read, unread) = retention.Effective(company?.ReadDays, company?.UnreadDays);
        // Only the categories that really differ, so the panel mentions exceptions and nothing else.
        var different = rows.Where(x => !x.IsCompanyWide)
            .Select(x => (x.Category, Effective: retention.EffectiveFor(company, x)))
            .Where(x => x.Effective != (read, unread))
            .OrderBy(x => x.Category)
            .Select(x => new CategoryRetentionSummary(x.Category, x.Effective.ReadDays, x.Effective.UnreadDays))
            .ToList();
        return new NotificationRetentionResponse(retention.Enabled, read, unread, different);
    }

    private (CompanyId Company, UserId User) Scope() =>
        (companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required."),
         currentUser.UserId ?? throw new InvalidOperationException("A signed-in user is required."));

    public async Task<IReadOnlyList<NotificationResponse>> ListAsync(bool unreadOnly, int take, CancellationToken cancellationToken)
    {
        var (company, user) = Scope();
        return (await store.ListAsync(company, user, unreadOnly, Math.Clamp(take, 1, 100), cancellationToken)).Select(Map).ToArray();
    }

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken)
    {
        var (company, user) = Scope();
        return store.CountUnreadAsync(company, user, cancellationToken);
    }

    public async Task<NotificationResponse?> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var (company, user) = Scope();
        var notification = await store.GetAsync(company, user, id, cancellationToken);
        if (notification is null) return null;
        notification.MarkRead(clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return Map(notification);
    }

    public Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var (company, user) = Scope();
        return store.MarkAllReadAsync(company, user, clock.UtcNow, cancellationToken);
    }

    private static NotificationResponse Map(Notification n) => new(n.Id, n.Category, n.Title, n.Body, n.Link, n.CreatedAt, n.IsRead);
}

/// <summary>Implements the cross-module notifier: recipients are users with active access to the active company.</summary>
public sealed class Notifier(
    INotificationStore store, IUserCompanyAccessStore access, IRolePermissionStore permissions, ICompanyContext companyContext, IClock clock,
    IEmailOutboxStore outbox, EmailOptions emailOptions) : INotifier
{
    private CompanyId Company =>
        companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required.");

    public async Task NotifyUsersAsync(IEnumerable<Guid> userIds, NotificationMessage message, CancellationToken cancellationToken)
    {
        var wanted = userIds.ToHashSet();
        var recipients = (await access.ListUsersForCompanyAsync(Company, cancellationToken)).Where(u => wanted.Contains(u.Id.Value));
        await DeliverAsync(recipients, message, cancellationToken);
    }

    public async Task NotifyPermissionHoldersAsync(string permissionCode, NotificationMessage message, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var recipients = new List<User>();
        foreach (var user in await access.ListUsersForCompanyAsync(Company, cancellationToken))
            if (user.Id.Value != exceptUserId && await permissions.UserHasPermissionAsync(user.Id, permissionCode, cancellationToken))
                recipients.Add(user);
        await DeliverAsync(recipients, message, cancellationToken);
    }

    public async Task ResolveSubjectAsync(string subject, CancellationToken cancellationToken) =>
        await store.MarkSubjectReadAsync(Company, subject, clock.UtcNow, cancellationToken);

    public Task<DateTimeOffset?> LastSentAsync(string subject, CancellationToken cancellationToken) =>
        store.LastForSubjectAsync(Company, subject, cancellationToken);

    private async Task DeliverAsync(IEnumerable<User> recipients, NotificationMessage message, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var users = recipients.DistinctBy(u => u.Id).ToList();
        var items = users
            .Select(u => Notification.Create(Company, u.Id, message.Category, message.Title, message.Body, message.Link, message.Subject, now)).ToList();
        if (items.Count == 0) return;
        await store.AddRangeAsync(items, cancellationToken);

        // Email goes to the outbox in the same save as the in-app message: both happen or neither does.
        if (message.Email && emailOptions.Enabled)
        {
            var wanting = users.Where(u => u.EmailNotifications && !string.IsNullOrWhiteSpace(u.Email)).ToList();
            var category = NotificationRetentionSetting.NormaliseCategory(items[0].Category);
            var optedOut = wanting.Count == 0 ? new HashSet<UserId>() : await store.ListEmailOptOutsAsync(wanting.Select(u => u.Id).ToArray(), category, cancellationToken);
            var emails = wanting.Where(u => !optedOut.Contains(u.Id)).Select(u =>
            {
                var mail = EmailComposer.Compose(message, u.Email, u.UserName, emailOptions);
                return EmailMessage.Create(Company, u.Id, mail.ToAddress, mail.ToName, mail.Subject, mail.Text, mail.Html, now);
            }).ToList();
            if (emails.Count > 0) await outbox.AddRangeAsync(emails, cancellationToken);
        }

        await store.SaveChangesAsync(cancellationToken);
    }
}
