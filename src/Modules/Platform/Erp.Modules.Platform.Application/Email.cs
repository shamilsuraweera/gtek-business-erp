using System.Net;
using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Application;

public enum EmailSecurity
{
    /// <summary>No encryption (a local test server only).</summary>
    None,
    /// <summary>Connect in the clear, then upgrade with STARTTLS (usually port 587).</summary>
    StartTls,
    /// <summary>TLS from the first byte (usually port 465).</summary>
    SslOnConnect
}

/// <summary>Email delivery settings (configuration section <c>Email</c>). Off by default; see ADR 0026.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public bool Enabled { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public EmailSecurity Security { get; set; } = EmailSecurity.StartTls;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "GTEK ERP";

    /// <summary>The web app's address, used to turn in-app links ("/tolerance-requests") into links that work from an email.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Seconds between looks at the outbox.</summary>
    public int PollSeconds { get; set; } = 20;
    public int BatchSize { get; set; } = 20;

    /// <summary>Attempts before a message is given up on.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The wait, in minutes, after the 1st, 2nd, … failed attempt, when none is configured.</summary>
    public static readonly IReadOnlyList<int> DefaultRetryMinutes = [1, 5, 30, 120, 720];

    /// <summary>
    /// Minutes to wait after the 1st, 2nd, … failed attempt before trying again; null uses <see cref="DefaultRetryMinutes"/>.
    /// Deliberately has no default value: the configuration binder appends configured items to a list that already has some,
    /// which would give the defaults followed by the operator's values.
    /// </summary>
    public List<int>? RetryMinutes { get; set; }

    /// <summary>The schedule in force (the configured one, or the default).</summary>
    public IReadOnlyList<int> EffectiveRetryMinutes => RetryMinutes ?? DefaultRetryMinutes;

    public int KeepSentDays { get; set; } = 14;
    public int KeepFailedDays { get; set; } = 90;

    /// <summary>Minutes a claimed message stays reserved for one worker before another may take it (covers a crashed worker).</summary>
    public int LeaseMinutes { get; set; } = 5;

    /// <summary>How long to wait before the next attempt after <paramref name="attemptsMade"/> failures, or null when it should give up.</summary>
    public TimeSpan? RetryDelay(int attemptsMade) =>
        attemptsMade >= MaxAttempts || EffectiveRetryMinutes.Count == 0
            ? null
            : TimeSpan.FromMinutes(EffectiveRetryMinutes[Math.Min(attemptsMade, EffectiveRetryMinutes.Count) - 1]);

    /// <summary>Problems with the settings. Only checked when email is enabled, so an installation without email needs no configuration.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enabled) return errors;
        if (string.IsNullOrWhiteSpace(Host)) errors.Add($"{Section}:Host is required when email is enabled.");
        if (Port is < 1 or > 65535) errors.Add($"{Section}:Port must be between 1 and 65535 (was {Port}).");
        if (string.IsNullOrWhiteSpace(FromAddress) || !System.Net.Mail.MailAddress.TryCreate(FromAddress, out _))
            errors.Add($"{Section}:FromAddress must be a valid email address.");
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            errors.Add($"{Section}:BaseUrl must be the web app's absolute http(s) address, for example https://erp.example.com.");
        if (!string.IsNullOrEmpty(Username) && string.IsNullOrEmpty(Password)) errors.Add($"{Section}:Password is required when a Username is set.");
        if (string.IsNullOrWhiteSpace(FromName)) errors.Add($"{Section}:FromName must not be empty.");
        if (PollSeconds is < 1 or > 3600) errors.Add($"{Section}:PollSeconds must be between 1 and 3600 (was {PollSeconds}).");
        if (BatchSize is < 1 or > 500) errors.Add($"{Section}:BatchSize must be between 1 and 500 (was {BatchSize}).");
        if (MaxAttempts is < 1 or > 20) errors.Add($"{Section}:MaxAttempts must be between 1 and 20 (was {MaxAttempts}).");
        if (EffectiveRetryMinutes.Any(m => m is < 0 or > 1440)) errors.Add($"{Section}:RetryMinutes values must be between 0 and 1440.");
        if (KeepSentDays is < 1 or > 3650) errors.Add($"{Section}:KeepSentDays must be between 1 and 3650 (was {KeepSentDays}).");
        if (KeepFailedDays is < 1 or > 3650) errors.Add($"{Section}:KeepFailedDays must be between 1 and 3650 (was {KeepFailedDays}).");
        if (LeaseMinutes is < 1 or > 120) errors.Add($"{Section}:LeaseMinutes must be between 1 and 120 (was {LeaseMinutes}).");
        return errors;
    }
}

/// <param name="MessageId">A stable identifier of this message, the same on every attempt (the outbox row id). It becomes the <c>Message-ID</c> header, so if a
/// crash makes the same message go out twice, mail systems and people can recognise the copy as a duplicate.</param>
public sealed record OutgoingEmail(string ToAddress, string? ToName, string Subject, string Text, string Html, string? MessageId = null);

/// <summary>A send that failed. <see cref="Permanent"/> failures (a rejected address) are not retried.</summary>
public sealed class EmailDeliveryException(string message, bool permanent, Exception? inner = null) : Exception(message, inner)
{
    public bool Permanent { get; } = permanent;
}

public interface IEmailSender
{
    /// <exception cref="EmailDeliveryException">The message could not be delivered.</exception>
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

public sealed record EmailOutboxStatus(int Pending, int Failed, int SentLast24Hours, string? LastError, DateTimeOffset? LastSentAt, DateTimeOffset? OldestPendingAt);

public interface IEmailOutboxStore
{
    Task AddRangeAsync(IEnumerable<EmailMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Reserves up to <paramref name="batch"/> due messages for this caller until <paramref name="leaseUntil"/>. Concurrent callers get
    /// different messages, and a reservation that lapsed (a crashed worker) can be taken again.
    /// </summary>
    Task<IReadOnlyList<EmailMessage>> ClaimDueAsync(DateTimeOffset now, int batch, DateTimeOffset leaseUntil, Guid token, CancellationToken cancellationToken);

    /// <summary>Deletes sent messages older than <paramref name="sentBefore"/> and failed ones older than <paramref name="failedBefore"/>.</summary>
    Task<int> PurgeAsync(DateTimeOffset sentBefore, DateTimeOffset failedBefore, CancellationToken cancellationToken);

    Task<EmailOutboxStatus> StatusAsync(CompanyId companyId, DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Builds the text and HTML of an email from an in-app notification.</summary>
public static class EmailComposer
{
    public static OutgoingEmail Compose(NotificationMessage message, string toAddress, string? toName, EmailOptions options)
    {
        var subject = $"[{options.FromName}] {message.Title}".ReplaceLineEndings(" ").Trim();
        var link = AbsoluteLink(message.Link, options.BaseUrl);
        var greeting = string.IsNullOrWhiteSpace(toName) ? "Hello," : $"Hello {toName},";

        var text = new System.Text.StringBuilder();
        text.AppendLine(greeting).AppendLine().AppendLine(message.Title);
        if (!string.IsNullOrWhiteSpace(message.Body)) text.AppendLine().AppendLine(message.Body);
        if (link is not null) text.AppendLine().AppendLine($"Open it here: {link}");
        text.AppendLine().AppendLine(message.Mandatory
            ? "This is a security notice about your GTEK ERP account. It is sent whatever your email settings are."
            : "You receive this because it needs your attention in GTEK ERP. You can turn these emails off in the notification panel.");

        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var html = $"<p>{E(greeting)}</p><p><strong>{E(message.Title)}</strong></p>"
            + (string.IsNullOrWhiteSpace(message.Body) ? string.Empty : $"<p>{E(message.Body)}</p>")
            + (link is null ? string.Empty : $"<p><a href=\"{E(link)}\">Open it in {E(options.FromName)}</a></p>")
            + "<p style=\"color:#667085;font-size:12px\">" + (message.Mandatory
                ? "This is a security notice about your account. It is sent whatever your email settings are."
                : "You receive this because it needs your attention. You can turn these emails off in the notification panel.") + "</p>";
        return new OutgoingEmail(toAddress, toName, subject, text.ToString(), html);
    }

    /// <summary>An in-app path made absolute; anything that is not a plain in-app path is dropped rather than put in an email.</summary>
    public static string? AbsoluteLink(string? link, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(baseUrl)) return null;
        if (!link.StartsWith('/') || link.StartsWith("//", StringComparison.Ordinal)) return null;
        return baseUrl.TrimEnd('/') + link;
    }
}

public sealed record EmailRun(int Sent, int Retrying, int GaveUp, int Purged);

public interface IEmailDelivery
{
    /// <summary>One pass: send what is due, then clear out old sent and failed messages.</summary>
    Task<EmailRun> RunOnceAsync(CancellationToken cancellationToken);
}

public sealed class EmailDelivery(IEmailOutboxStore store, IEmailSender sender, EmailOptions options, IClock clock) : IEmailDelivery
{
    public async Task<EmailRun> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var batch = await store.ClaimDueAsync(now, options.BatchSize, now.AddMinutes(options.LeaseMinutes), Guid.NewGuid(), cancellationToken);
        int sent = 0, retrying = 0, gaveUp = 0;
        foreach (var message in batch)
        {
            try
            {
                await sender.SendAsync(new OutgoingEmail(message.ToAddress, message.ToName, message.Subject, message.TextBody, message.HtmlBody, message.Id.ToString("N")), cancellationToken);
                message.MarkSent(clock.UtcNow);
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var permanent = exception is EmailDeliveryException { Permanent: true };
                message.MarkFailed(exception.Message, permanent, clock.UtcNow, options.MaxAttempts, options.RetryDelay(message.Attempts + 1));
                if (message.Status == EmailStatus.Failed) gaveUp++; else retrying++;
            }

            // Saved one by one, so a crash after a send never makes the next run send it again.
            await store.SaveChangesAsync(cancellationToken);
        }

        var purged = await store.PurgeAsync(now.AddDays(-options.KeepSentDays), now.AddDays(-options.KeepFailedDays), cancellationToken);
        return new EmailRun(sent, retrying, gaveUp, purged);
    }
}

public sealed record EmailStatusResponse(
    bool Enabled, string? Host, int? Port, string? FromAddress, int Pending, int Failed, int SentLast24Hours,
    string? LastError, DateTimeOffset? LastSentAt, DateTimeOffset? OldestPendingAt);

public interface IEmailStatusService
{
    Task<EmailStatusResponse> GetAsync(CancellationToken cancellationToken);
}

public sealed class EmailStatusService(IEmailOutboxStore store, EmailOptions options, ICompanyContext companyContext, IClock clock) : IEmailStatusService
{
    public async Task<EmailStatusResponse> GetAsync(CancellationToken cancellationToken)
    {
        var status = await store.StatusAsync(
            companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required."),
            clock.UtcNow, cancellationToken);
        return new EmailStatusResponse(options.Enabled, options.Enabled ? options.Host : null, options.Enabled ? options.Port : null,
            options.Enabled ? options.FromAddress : null, status.Pending, status.Failed, status.SentLast24Hours, status.LastError, status.LastSentAt, status.OldestPendingAt);
    }
}

public sealed record UserPreferencesResponse(bool EmailNotifications, bool EmailAvailable, string Email, IReadOnlyList<EmailCategoryPreference> Categories);

/// <summary>Whether the user wants email for one notification category (only meaningful while email notifications are on).</summary>
public sealed record EmailCategoryPreference(string Category, bool EmailEnabled);

public sealed record UpdateUserPreferencesRequest(bool EmailNotifications);
public sealed record UpdateEmailCategoryRequest(bool Enabled);

public interface IUserPreferenceService
{
    Task<UserPreferencesResponse?> GetAsync(UserId userId, CancellationToken cancellationToken);
    Task<UserPreferencesResponse?> SetEmailNotificationsAsync(UserId userId, bool enabled, CancellationToken cancellationToken);
    /// <summary>Turns email for one notification category on or off for the user.</summary>
    Task<UserPreferencesResponse?> SetEmailCategoryAsync(UserId userId, string category, bool enabled, CancellationToken cancellationToken);
}

public sealed class UserPreferenceService(IUserStore users, INotificationStore notifications, EmailOptions options) : IUserPreferenceService
{
    public async Task<UserPreferencesResponse?> GetAsync(UserId userId, CancellationToken cancellationToken) =>
        await users.GetByIdAsync(userId, cancellationToken) is { } user ? await MapAsync(user, cancellationToken) : null;

    public async Task<UserPreferencesResponse?> SetEmailNotificationsAsync(UserId userId, bool enabled, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null) return null;
        user.SetEmailNotifications(enabled);
        await users.SaveChangesAsync(cancellationToken);
        return await MapAsync(user, cancellationToken);
    }

    public async Task<UserPreferencesResponse?> SetEmailCategoryAsync(UserId userId, string category, bool enabled, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null) return null;
        var key = NotificationRetentionSetting.NormaliseCategory(category);
        if (key.Length == 0) throw new DomainException("A category is required.");
        if (NotificationCategories.Mandatory.Contains(key)) throw new DomainException($"Emails in the '{key}' category are security notices and cannot be turned off.");
        await notifications.SetEmailOptOutAsync(userId, key, optedOut: !enabled, cancellationToken);
        return await MapAsync(user, cancellationToken);
    }

    private async Task<UserPreferencesResponse> MapAsync(User user, CancellationToken cancellationToken)
    {
        // A stale opt-out of a category that has since become mandatory is ignored and not offered.
        var optedOut = (await notifications.ListEmailOptOutCategoriesAsync(user.Id, cancellationToken)).Where(c => !NotificationCategories.Mandatory.Contains(c)).ToHashSet();
        var categories = NotificationCategories.Known.Concat(optedOut).Distinct().Order()
            .Select(c => new EmailCategoryPreference(c, !optedOut.Contains(c))).ToList();
        return new UserPreferencesResponse(user.EmailNotifications, options.Enabled, user.Email, categories);
    }
}
