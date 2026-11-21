using System.Reflection;
using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.Logging.Abstractions;

namespace Erp.UnitTests;

public class AccountNoticeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly CompanyId Acme = new(Guid.NewGuid());

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }

    private static EmailOptions EmailOn() => new() { Enabled = true, Host = "smtp.test", FromAddress = "erp@example.com", BaseUrl = "https://erp.example.com" };

    private sealed class Fixture
    {
        public User Admin { get; } = User.Create("admin", "admin@example.com", Now);
        public User Clerk { get; } = User.Create("clerk", "clerk@example.com", Now);
        public NotificationTests.FakeOutbox Outbox { get; } = new();
        public HashSet<UserId> OptedOut { get; } = [];
        public int Saves { get; private set; }
        public bool UsersThrow { get; set; }
        public EmailOptions Options { get; set; } = EmailOn();

        public AccountNotices Notices()
        {
            var users = DispatchProxy.Create<IUserStore, NotificationTests.Proxy<IUserStore>>();
            ((NotificationTests.Proxy<IUserStore>)(object)users).Handlers["GetByIdAsync"] = a =>
                UsersThrow ? throw new InvalidOperationException("db down")
                : Task.FromResult<User?>(new[] { Admin, Clerk }.SingleOrDefault(u => u.Id == (UserId)a[0]!));
            var companies = DispatchProxy.Create<ICompanyStore, NotificationTests.Proxy<ICompanyStore>>();
            ((NotificationTests.Proxy<ICompanyStore>)(object)companies).Handlers["GetByIdAsync"] = a =>
                Task.FromResult<Company?>((CompanyId)a[0]! == Acme ? Company.Create("ACME", "Acme Ltd", Now) : null);
            var notifications = DispatchProxy.Create<INotificationStore, NotificationTests.Proxy<INotificationStore>>();
            var proxy = (NotificationTests.Proxy<INotificationStore>)(object)notifications;
            proxy.Handlers["ListEmailOptOutsAsync"] = a => Task.FromResult<IReadOnlySet<UserId>>(OptedOut.Where(((IReadOnlyCollection<UserId>)a[0]!).Contains).ToHashSet());
            proxy.Handlers["SaveChangesAsync"] = _ => { Saves++; return Task.CompletedTask; };
            return new AccountNotices(users, companies, notifications, Outbox, Options, new FixedClock(), NullLogger<AccountNotices>.Instance);
        }
    }

    [Fact]
    public async Task A_new_account_is_announced_to_its_owner_without_a_password()
    {
        var f = new Fixture();
        await f.Notices().AccountCreatedAsync(f.Admin.Id, f.Clerk.Id, default);

        var mail = Assert.Single(f.Outbox.Added);
        Assert.Equal("clerk@example.com", mail.ToAddress);
        Assert.Equal("[GTEK ERP] Your GTEK ERP account was created", mail.Subject);
        Assert.Contains("admin created an account for you", mail.TextBody);
        Assert.Contains("Your user name is clerk", mail.TextBody);
        Assert.DoesNotContain("password:", mail.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((null, f.Clerk.Id, EmailStatus.Pending), (mail.CompanyId, mail.UserId, mail.Status));   // not a company message
        Assert.Contains("https://erp.example.com/", mail.TextBody);
        Assert.Equal(1, f.Saves);
    }

    [Theory]
    [InlineData(AccountAccessChange.Granted, "You now have access to Acme Ltd", "admin gave you access to Acme Ltd")]
    [InlineData(AccountAccessChange.Restored, "Your access to Acme Ltd was restored", "admin restored your access")]
    [InlineData(AccountAccessChange.Revoked, "Your access to Acme Ltd was removed", "admin removed your access to Acme Ltd")]
    public async Task Company_access_changes_name_the_company_and_the_administrator(AccountAccessChange change, string subject, string body)
    {
        var f = new Fixture();
        await f.Notices().CompanyAccessChangedAsync(f.Admin.Id, f.Clerk.Id, Acme, change, default);
        var mail = Assert.Single(f.Outbox.Added);
        Assert.Equal("[GTEK ERP] " + subject, mail.Subject);
        Assert.Contains(body, mail.TextBody);
    }

    [Fact]
    public async Task An_unknown_company_still_gets_a_notice()
    {
        var f = new Fixture();
        await f.Notices().CompanyAccessChangedAsync(f.Admin.Id, f.Clerk.Id, new CompanyId(Guid.NewGuid()), AccountAccessChange.Revoked, default);
        Assert.Contains("a company", Assert.Single(f.Outbox.Added).Subject);
    }

    [Theory]
    [InlineData(true, "You were given the Accountant role")]
    [InlineData(false, "The Accountant role was removed from you")]
    public async Task Role_changes_name_the_role(bool granted, string subject)
    {
        var f = new Fixture();
        await f.Notices().RoleChangedAsync(f.Admin.Id, f.Clerk.Id, "Accountant", granted, default);
        var mail = Assert.Single(f.Outbox.Added);
        Assert.Equal("[GTEK ERP] " + subject, mail.Subject);
        Assert.Contains("What you can do in GTEK ERP has changed.", mail.TextBody);
    }

    [Theory]
    [InlineData(false, "Your account was deactivated", "can no longer sign in")]
    [InlineData(true, "Your account was reactivated", "sign in again")]
    public async Task Activation_changes_are_announced(bool active, string subject, string body)
    {
        var f = new Fixture();
        await f.Notices().AccountStatusChangedAsync(f.Admin.Id, f.Clerk.Id, active, default);
        var mail = Assert.Single(f.Outbox.Added);
        Assert.Equal("[GTEK ERP] " + subject, mail.Subject);
        Assert.Contains(body, mail.TextBody);
    }

    [Fact]
    public async Task Without_an_acting_user_the_notice_says_an_administrator()
    {
        var f = new Fixture();
        await f.Notices().RoleChangedAsync(null, f.Clerk.Id, "Accountant", true, default);
        Assert.Contains("An administrator gave you the Accountant role", Assert.Single(f.Outbox.Added).TextBody);
    }

    // ---- who is not emailed -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Nobody_is_emailed_about_their_own_action()
    {
        var f = new Fixture();
        await f.Notices().CompanyAccessChangedAsync(f.Admin.Id, f.Admin.Id, Acme, AccountAccessChange.Granted, default);
        Assert.Empty(f.Outbox.Added);
    }

    [Fact]
    public async Task Nothing_is_queued_when_email_is_off()
    {
        var f = new Fixture { Options = new EmailOptions { Enabled = false } };
        await f.Notices().AccountStatusChangedAsync(f.Admin.Id, f.Clerk.Id, false, default);
        Assert.Empty(f.Outbox.Added);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public async Task Security_notices_ignore_the_recipients_email_preferences()
    {
        var f = new Fixture();
        f.Clerk.SetEmailNotifications(false);          // email switched off overall
        f.OptedOut.Add(f.Clerk.Id);                    // and a stale opt-out of the category
        await f.Notices().AccountStatusChangedAsync(f.Admin.Id, f.Clerk.Id, false, default);
        var mail = Assert.Single(f.Outbox.Added);
        Assert.Contains("security notice about your GTEK ERP account", mail.TextBody);
        Assert.Contains("whatever your email settings are", mail.HtmlBody);
        Assert.DoesNotContain("turn these emails off", mail.TextBody);
    }

    [Fact]
    public void The_account_category_is_mandatory_and_therefore_not_a_choice()
    {
        Assert.Equal("account", AccountNotices.Category);
        Assert.Contains(AccountNotices.Category, NotificationCategories.Mandatory);
        Assert.DoesNotContain(AccountNotices.Category, NotificationCategories.Known);
    }

    [Fact]
    public async Task An_unknown_target_is_ignored()
    {
        var f = new Fixture();
        await f.Notices().AccountStatusChangedAsync(f.Admin.Id, new UserId(Guid.NewGuid()), false, default);
        Assert.Empty(f.Outbox.Added);
    }

    [Fact]
    public async Task A_failure_never_escapes_into_the_administrators_change()
    {
        var f = new Fixture { UsersThrow = true };
        await f.Notices().AccountStatusChangedAsync(f.Admin.Id, f.Clerk.Id, false, default);   // does not throw
        Assert.Empty(f.Outbox.Added);
    }
}
