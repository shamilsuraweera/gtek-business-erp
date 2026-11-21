using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Platform.Application;

/// <summary>
/// Tells a person by email when an administrator changes their own access (ADR 0032): an account created for them, access to a company
/// granted, removed or restored, a role given or taken away, the account deactivated or reactivated. These are not company events, so there is
/// no in-app message: someone whose access to a company was removed could not open it to read one. Email only, queued through the outbox.
/// </summary>
public interface IAccountNotices
{
    Task AccountCreatedAsync(UserId? actor, UserId target, CancellationToken cancellationToken);
    Task AccountStatusChangedAsync(UserId? actor, UserId target, bool active, CancellationToken cancellationToken);
    Task CompanyAccessChangedAsync(UserId? actor, UserId target, CompanyId company, AccountAccessChange change, CancellationToken cancellationToken);
    Task RoleChangedAsync(UserId? actor, UserId target, string roleName, bool granted, CancellationToken cancellationToken);
}

public enum AccountAccessChange
{
    Granted,
    Restored,
    Revoked
}

/// <summary>
/// Best effort, after the change is saved: a problem here is logged and never undoes or fails the administrator's change. These are
/// <b>mandatory</b> security notices (ADR 0034): they ignore the recipient's email preferences. Nothing is sent to a person for their own
/// action, when email is off for the installation, or when they have no address.
/// </summary>
public sealed class AccountNotices(
    IUserStore users, ICompanyStore companies, INotificationStore notifications, IEmailOutboxStore outbox,
    EmailOptions emailOptions, IClock clock, ILogger<AccountNotices> logger) : IAccountNotices
{
    public const string Category = "account";

    public Task AccountCreatedAsync(UserId? actor, UserId target, CancellationToken cancellationToken) =>
        SendAsync(actor, target, (who, by) => ("Your GTEK ERP account was created",
            $"{by} created an account for you. Your user name is {who.UserName}. Ask {by} for your password if you have not been given it."), cancellationToken);

    public Task AccountStatusChangedAsync(UserId? actor, UserId target, bool active, CancellationToken cancellationToken) =>
        SendAsync(actor, target, (_, by) => active
            ? ("Your account was reactivated", $"{by} reactivated your account. You can sign in again.")
            : ("Your account was deactivated", $"{by} deactivated your account, so you can no longer sign in. If you did not expect this, contact an administrator."),
            cancellationToken);

    public async Task CompanyAccessChangedAsync(UserId? actor, UserId target, CompanyId company, AccountAccessChange change, CancellationToken cancellationToken)
    {
        var name = await SafeAsync(async () => (await companies.GetByIdAsync(company, cancellationToken))?.Name) ?? "a company";
        await SendAsync(actor, target, (_, by) => change switch
        {
            AccountAccessChange.Granted => ($"You now have access to {name}", $"{by} gave you access to {name}. Sign in and choose it from the company list."),
            AccountAccessChange.Restored => ($"Your access to {name} was restored", $"{by} restored your access to {name}."),
            _ => ($"Your access to {name} was removed", $"{by} removed your access to {name}. If you did not expect this, contact an administrator.")
        }, cancellationToken);
    }

    public Task RoleChangedAsync(UserId? actor, UserId target, string roleName, bool granted, CancellationToken cancellationToken) =>
        SendAsync(actor, target, (_, by) => granted
            ? ($"You were given the {roleName} role", $"{by} gave you the {roleName} role. What you can do in GTEK ERP has changed.")
            : ($"The {roleName} role was removed from you", $"{by} removed the {roleName} role from you. What you can do in GTEK ERP has changed."),
            cancellationToken);

    private async Task SendAsync(UserId? actor, UserId target, Func<User, string, (string Title, string Body)> compose, CancellationToken cancellationToken)
    {
        if (!emailOptions.Enabled || actor == target) return;
        try
        {
            var user = await users.GetByIdAsync(target, cancellationToken);
            if (user is null || string.IsNullOrWhiteSpace(user.Email)) return;

            var by = actor is { } id ? (await users.GetByIdAsync(id, cancellationToken))?.UserName ?? "An administrator" : "An administrator";
            var (title, body) = compose(user, by);
            var mail = EmailComposer.Compose(new NotificationMessage(Category, title, body, "/", null, Email: true, Mandatory: true), user.Email, user.UserName, emailOptions);
            await outbox.AddRangeAsync([EmailMessage.Create(null, user.Id, mail.ToAddress, mail.ToName, mail.Subject, mail.Text, mail.Html, clock.UtcNow)], cancellationToken);
            await notifications.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "An account notice could not be queued.");
        }
    }

    private static async Task<T?> SafeAsync<T>(Func<Task<T?>> work)
    {
        try { return await work(); }
        catch (Exception) { return default; }
    }
}
