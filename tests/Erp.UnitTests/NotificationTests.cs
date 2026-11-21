using System.Reflection;
using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.Modules.Purchasing.Application;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class NotificationTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());
    private static readonly UserId User = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_notification_is_unread_and_trimmed()
    {
        var n = Notification.Create(Company, User, " approval ", "  Needs a decision ", "  body  ", " /tolerance-requests ", " tolerance-request:1 ", Now);
        Assert.False(n.IsRead);
        Assert.Null(n.ReadAt);
        Assert.Equal(("approval", "Needs a decision", "body", "/tolerance-requests", "tolerance-request:1"), (n.Category, n.Title, n.Body, n.Link, n.Subject));
        Assert.Equal(Now, n.CreatedAt);
    }

    [Fact]
    public void A_title_is_required_and_a_blank_category_becomes_general()
    {
        Assert.Throws<DomainException>(() => Notification.Create(Company, User, "x", "  ", null, null, null, Now));
        Assert.Equal("general", Notification.Create(Company, User, " ", "Hello", null, null, null, Now).Category);
    }

    [Fact]
    public void Overlong_text_is_cut_to_what_fits_the_columns()
    {
        var n = Notification.Create(Company, User, "x", new string('t', 300), new string('b', 1500), new string('l', 400), new string('s', 300), Now);
        Assert.Equal((200, 1000, 300, 200), (n.Title.Length, n.Body!.Length, n.Link!.Length, n.Subject!.Length));
    }

    [Fact]
    public void Blank_optional_fields_become_null()
    {
        var n = Notification.Create(Company, User, "x", "Hello", " ", "", null, Now);
        Assert.Null(n.Body);
        Assert.Null(n.Link);
    }

    [Fact]
    public void Reading_is_recorded_once_and_stays_at_the_first_time()
    {
        var n = Notification.Create(Company, User, "x", "Hello", null, null, null, Now);
        n.MarkRead(Now.AddMinutes(5));
        n.MarkRead(Now.AddMinutes(30));
        Assert.True(n.IsRead);
        Assert.Equal(Now.AddMinutes(5), n.ReadAt);
    }

    [Fact]
    public void The_purchasing_approve_permission_matches_the_platform_permission() =>
        Assert.Equal(Permissions.Purchasing.OrdersOverrideTolerance, ToleranceRequestService.ApprovePermission);

    // ---- the notifier's recipient rules -----------------------------------------------------------------------------

    private sealed class FakeStore : INotificationStore
    {
        public List<Notification> Added { get; } = [];
        public string? ResolvedSubject { get; private set; }
        public Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct) { Added.AddRange(notifications); return Task.CompletedTask; }
        public Task<IReadOnlyList<Notification>> ListAsync(CompanyId c, UserId u, bool unreadOnly, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountUnreadAsync(CompanyId c, UserId u, CancellationToken ct) => throw new NotSupportedException();
        public Task<Notification?> GetAsync(CompanyId c, UserId u, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> MarkAllReadAsync(CompanyId c, UserId u, DateTimeOffset at, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> MarkSubjectReadAsync(CompanyId c, string subject, DateTimeOffset at, CancellationToken ct) { ResolvedSubject = subject; return Task.FromResult(1); }
        public (DateTimeOffset ReadBefore, DateTimeOffset? UnreadBefore, int Batch)? Purged => Purges.Count == 0 ? null : (Purges[^1].ReadBefore, Purges[^1].UnreadBefore, Purges[^1].Batch);
        public List<(PurgeScope Scope, DateTimeOffset ReadBefore, DateTimeOffset? UnreadBefore, int Batch)> Purges { get; } = [];
        public List<NotificationRetentionSetting> Settings { get; } = [];
        public Task<PurgeResult> PurgeAsync(PurgeScope scope, DateTimeOffset readBefore, DateTimeOffset? unreadBefore, int batchSize, CancellationToken ct)
        {
            Purges.Add((scope, readBefore, unreadBefore, batchSize));
            return Task.FromResult(new PurgeResult(2, 1));
        }
        public Task<NotificationRetentionSetting?> GetRetentionSettingAsync(CompanyId companyId, string category, CancellationToken ct) => Task.FromResult(Settings.SingleOrDefault(x => x.CompanyId == companyId && x.Category == category));
        public Task<IReadOnlyList<NotificationRetentionSetting>> ListRetentionSettingsAsync(CompanyId companyId, CancellationToken ct) => Task.FromResult<IReadOnlyList<NotificationRetentionSetting>>(Settings.Where(x => x.CompanyId == companyId).ToList());
        public List<string> Categories { get; } = [];
        public List<(UserId User, string Category)> OptOuts { get; } = [];
        public Task<IReadOnlySet<UserId>> ListEmailOptOutsAsync(IReadOnlyCollection<UserId> userIds, string category, CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<UserId>>(OptOuts.Where(o => o.Category == category && userIds.Contains(o.User)).Select(o => o.User).ToHashSet());
        public Task<IReadOnlyList<string>> ListEmailOptOutCategoriesAsync(UserId userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(OptOuts.Where(o => o.User == userId).Select(o => o.Category).ToList());
        public Task SetEmailOptOutAsync(UserId userId, string category, bool optedOut, CancellationToken ct)
        {
            OptOuts.RemoveAll(o => o.User == userId && o.Category == category);
            if (optedOut) OptOuts.Add((userId, category));
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<string>> ListCategoriesAsync(CompanyId companyId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Categories);
        public Task<IReadOnlyList<NotificationRetentionSetting>> ListRetentionSettingsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<NotificationRetentionSetting>>(Settings.ToList());
        public Task AddRetentionSettingAsync(NotificationRetentionSetting setting, CancellationToken ct) { Settings.Add(setting); return Task.CompletedTask; }
        public void RemoveRetentionSetting(NotificationRetentionSetting setting) => Settings.Remove(setting);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Answers the two calls the notifier makes and rejects everything else.</summary>
    public class Proxy<T> : DispatchProxy where T : class
    {
        public Dictionary<string, Func<object?[], object?>> Handlers { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Handlers.TryGetValue(method!.Name, out var h) ? h(args ?? []) : throw new NotSupportedException(method.Name);
    }

    public sealed class FakeOutbox : IEmailOutboxStore
    {
        public List<EmailMessage> Added { get; } = [];
        public Task AddRangeAsync(IEnumerable<EmailMessage> messages, CancellationToken ct) { Added.AddRange(messages); return Task.CompletedTask; }
        public Task<IReadOnlyList<EmailMessage>> ClaimDueAsync(DateTimeOffset now, int batch, DateTimeOffset leaseUntil, Guid token, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> PurgeAsync(DateTimeOffset sentBefore, DateTimeOffset failedBefore, CancellationToken ct) => throw new NotSupportedException();
        public Task<EmailOutboxStatus> StatusAsync(CompanyId companyId, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedCompany(CompanyId id) : ICompanyContext { public CompanyId CompanyId => id; public bool HasCompany => true; }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }

    private static User MakeUser(string name) => Erp.Modules.Platform.Domain.User.Create(name, $"{name}@example.com", Now);

    private static EmailOptions EmailOn() => new() { Enabled = true, Host = "smtp.test", FromAddress = "erp@example.com", BaseUrl = "https://erp.example.com" };

    private static (Notifier Notifier, FakeStore Store, User[] Users, FakeOutbox Outbox) Setup(Func<User, bool> hasPermission, EmailOptions? email = null, Action<User[]>? tweakUsers = null)
    {
        var users = new[] { MakeUser("clerk"), MakeUser("approver1"), MakeUser("approver2"), MakeUser("viewer") };
        var access = DispatchProxy.Create<IUserCompanyAccessStore, Proxy<IUserCompanyAccessStore>>();
        ((Proxy<IUserCompanyAccessStore>)(object)access).Handlers["ListUsersForCompanyAsync"] = _ => Task.FromResult<IReadOnlyList<User>>(users);
        var perms = DispatchProxy.Create<IRolePermissionStore, Proxy<IRolePermissionStore>>();
        ((Proxy<IRolePermissionStore>)(object)perms).Handlers["UserHasPermissionAsync"] = a => Task.FromResult(hasPermission(users.Single(u => u.Id == (UserId)a[0]!)));
        tweakUsers?.Invoke(users);
        var store = new FakeStore();
        var outbox = new FakeOutbox();
        return (new Notifier(store, access, perms, new FixedCompany(Company), new FixedClock(), outbox, email ?? new EmailOptions()), store, users, outbox);
    }

    [Fact]
    public async Task Permission_holders_are_notified_except_the_actor()
    {
        var (notifier, store, users, _) = Setup(u => u.UserName.StartsWith("approver") || u.UserName == "clerk");
        await notifier.NotifyPermissionHoldersAsync("p", new NotificationMessage("approval", "Needs a decision", "b", "/x", "s:1"), users[0].Id.Value, default);
        Assert.Equal(new[] { "approver1", "approver2" }.Order(), store.Added.Select(n => users.Single(u => u.Id == n.UserId).UserName).Order());
        Assert.All(store.Added, n => Assert.Equal((Company, "s:1", "approval"), (n.CompanyId, n.Subject, n.Category)));
    }

    [Fact]
    public async Task Nobody_is_notified_when_nobody_holds_the_permission()
    {
        var (notifier, store, _, _) = Setup(_ => false);
        await notifier.NotifyPermissionHoldersAsync("p", new NotificationMessage("approval", "T", null, null), null, default);
        Assert.Empty(store.Added);
    }

    [Fact]
    public async Task Named_users_are_notified_only_if_they_belong_to_the_company_and_only_once()
    {
        var (notifier, store, users, _) = Setup(_ => true);
        await notifier.NotifyUsersAsync([users[0].Id.Value, users[0].Id.Value, Guid.NewGuid()], new NotificationMessage("decision", "T", null, null), default);
        Assert.Single(store.Added);
        Assert.Equal(users[0].Id, store.Added[0].UserId);
    }

    [Fact]
    public async Task Resolving_a_subject_marks_it_read_for_everyone_in_the_company()
    {
        var (notifier, store, _, _) = Setup(_ => true);
        await notifier.ResolveSubjectAsync("tolerance-request:9", default);
        Assert.Equal("tolerance-request:9", store.ResolvedSubject);
    }

    // ---- retention ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Default_retention_is_valid_and_keeps_read_messages_30_days_and_unread_180()
    {
        var o = new NotificationRetentionOptions();
        Assert.Empty(o.Validate());
        Assert.Equal((true, 30, 180, 1440, 60, 1000), (o.Enabled, o.ReadDays, o.UnreadDays, o.IntervalMinutes, o.InitialDelaySeconds, o.BatchSize));
    }

    [Theory]
    [InlineData(0, 180, "ReadDays")]
    [InlineData(3651, 3651, "ReadDays")]
    [InlineData(30, -1, "UnreadDays")]
    [InlineData(30, 3651, "UnreadDays")]
    [InlineData(30, 10, "must not be shorter")]     // unread must outlive read
    public void Invalid_day_settings_are_reported(int read, int unread, string mentions)
    {
        var errors = new NotificationRetentionOptions { ReadDays = read, UnreadDays = unread }.Validate();
        Assert.Contains(errors, e => e.Contains(mentions));
    }

    [Theory]
    [InlineData(30, 30)]      // equal is fine
    [InlineData(30, 0)]       // 0 keeps unread forever
    [InlineData(1, 3650)]
    public void Valid_day_settings_are_accepted(int read, int unread) =>
        Assert.Empty(new NotificationRetentionOptions { ReadDays = read, UnreadDays = unread }.Validate());

    [Fact]
    public void Every_problem_is_listed_at_once()
    {
        var errors = new NotificationRetentionOptions { ReadDays = 0, UnreadDays = 5000, IntervalMinutes = 0, InitialDelaySeconds = -1, BatchSize = 5 }.Validate();
        Assert.Equal(5, errors.Count);
        Assert.All(errors, e => Assert.StartsWith("Notifications:Retention:", e));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10081)]
    public void The_interval_is_bounded(int minutes) =>
        Assert.Contains(new NotificationRetentionOptions { IntervalMinutes = minutes }.Validate(), e => e.Contains("IntervalMinutes"));

    [Fact]
    public async Task A_purge_passes_cutoffs_counted_back_from_now()
    {
        var store = new FakeStore();
        var result = await new NotificationRetention(store, new NotificationRetentionOptions { ReadDays = 7, UnreadDays = 60, BatchSize = 250 }, new FixedClock()).PurgeAsync(default);
        Assert.Equal(Now.AddDays(-7), store.Purged!.Value.ReadBefore);
        Assert.Equal(Now.AddDays(-60), store.Purged.Value.UnreadBefore);
        Assert.Equal(250, store.Purged.Value.Batch);
        Assert.Equal((2, 1, 3), (result.Read, result.Unread, result.Total));
        Assert.Null(store.Purges.Single().Scope.Only);          // no overrides: one purge for every company
        Assert.Empty(store.Purges.Single().Scope.Except!);
    }

    [Fact]
    public async Task Unread_days_of_zero_means_unread_messages_are_never_purged()
    {
        var store = new FakeStore();
        await new NotificationRetention(store, new NotificationRetentionOptions { UnreadDays = 0 }, new FixedClock()).PurgeAsync(default);
        Assert.Null(store.Purged!.Value.UnreadBefore);
    }

    // ---- per-company retention ---------------------------------------------------------------------------------------

    private static NotificationRetentionSetting Override(CompanyId company, int? read, int? unread, string? category = null)
    {
        var s = NotificationRetentionSetting.Create(company, category);
        s.Set(read, unread, Now, null);
        return s;
    }

    [Theory]
    [InlineData(null, null, 30, 180)]    // inherits both
    [InlineData(60, null, 60, 180)]
    [InlineData(null, 365, 30, 365)]
    [InlineData(10, 20, 10, 20)]
    [InlineData(null, 0, 30, 0)]         // a company can keep unread messages forever
    [InlineData(200, null, 200, 200)]    // inherited unread (180) is lifted to the longer read period
    public void Effective_retention_uses_company_values_and_falls_back_to_the_system_defaults(int? read, int? unread, int expectedRead, int expectedUnread) =>
        Assert.Equal((expectedRead, expectedUnread), new NotificationRetentionOptions().Effective(read, unread));

    [Theory]
    [InlineData(0, null, "Read days")]
    [InlineData(3651, null, "Read days")]
    [InlineData(null, -1, "Unread days")]
    [InlineData(null, 3651, "Unread days")]
    [InlineData(null, 10, "system default")]      // 10 < inherited read 30
    [InlineData(200, null, "system default")]     // 200 > inherited unread 180
    [InlineData(100, 50, "must not be shorter")]
    public void Invalid_overrides_are_reported(int? read, int? unread, string mentions) =>
        Assert.Contains(new NotificationRetentionOptions().ValidateOverrides(read, unread), e => e.Contains(mentions));

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, 1)]
    [InlineData(3650, 3650)]
    [InlineData(100, 0)]
    [InlineData(null, 0)]
    [InlineData(null, 30)]
    public void Valid_overrides_are_accepted(int? read, int? unread) =>
        Assert.Empty(new NotificationRetentionOptions().ValidateOverrides(read, unread));

    [Fact]
    public void An_override_row_is_kept_only_while_it_overrides_something()
    {
        var s = NotificationRetentionSetting.Create(Company);
        Assert.True(s.InheritsEverything);
        s.Set(null, 0, Now, null);
        Assert.False(s.InheritsEverything);   // 0 is a real setting, not "unset"
        s.Set(null, null, Now.AddHours(1), null);
        Assert.True(s.InheritsEverything);
    }

    [Fact]
    public async Task Companies_with_their_own_retention_are_purged_by_it_and_everyone_else_by_the_default()
    {
        var store = new FakeStore();
        var a = new CompanyId(Guid.NewGuid());
        var b = new CompanyId(Guid.NewGuid());
        store.Settings.AddRange([Override(a, 7, 14), Override(b, null, 0)]);
        var result = await new NotificationRetention(store, new NotificationRetentionOptions { ReadDays = 30, UnreadDays = 180 }, new FixedClock()).PurgeAsync(default);

        Assert.Equal(3, store.Purges.Count);
        var forA = store.Purges.Single(p => p.Scope.Only == a);
        Assert.Equal((Now.AddDays(-7), (DateTimeOffset?)Now.AddDays(-14)), (forA.ReadBefore, forA.UnreadBefore));
        var forB = store.Purges.Single(p => p.Scope.Only == b);
        Assert.Equal((Now.AddDays(-30), (DateTimeOffset?)null), (forB.ReadBefore, forB.UnreadBefore));   // read inherited, unread kept forever
        var rest = store.Purges.Single(p => p.Scope.Only is null);
        Assert.Equal(new[] { a, b }.Order(Comparer<CompanyId>.Create((x, y) => x.Value.CompareTo(y.Value))), rest.Scope.Except!.Order(Comparer<CompanyId>.Create((x, y) => x.Value.CompareTo(y.Value))));
        Assert.Equal((Now.AddDays(-30), (DateTimeOffset?)Now.AddDays(-180)), (rest.ReadBefore, rest.UnreadBefore));
        Assert.Equal(9, result.Total);   // three purges of (2 read, 1 unread)
    }

    [Fact]
    public async Task A_company_override_that_would_leave_unread_shorter_than_read_is_lifted_when_purging()
    {
        var store = new FakeStore();
        var a = new CompanyId(Guid.NewGuid());
        store.Settings.Add(Override(a, 200, null));          // inherits unread 180 < 200: drift the purge must not act on literally
        await new NotificationRetention(store, new NotificationRetentionOptions(), new FixedClock()).PurgeAsync(default);
        var forA = store.Purges.Single(p => p.Scope.Only == a);
        Assert.Equal((Now.AddDays(-200), (DateTimeOffset?)Now.AddDays(-200)), (forA.ReadBefore, forA.UnreadBefore));
    }

    // ---- per-category retention -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, null, null, null, 30, 180)]      // nothing set: system defaults
    [InlineData(60, 90, null, null, 60, 90)]           // company only
    [InlineData(60, 90, 7, null, 7, 90)]               // category read overrides, unread falls to the company's
    [InlineData(60, 90, null, 0, 60, 0)]               // category keeps unread forever
    [InlineData(null, null, 7, 14, 7, 14)]             // category with no company row
    [InlineData(null, null, 200, null, 200, 200)]      // inherited unread is lifted to the longer read period
    public void A_category_overrides_the_company_which_overrides_the_default(int? companyRead, int? companyUnread, int? catRead, int? catUnread, int expectedRead, int expectedUnread)
    {
        var company = companyRead is null && companyUnread is null ? null : Override(Company, companyRead, companyUnread);
        var category = catRead is null && catUnread is null ? null : Override(Company, catRead, catUnread, "approval");
        Assert.Equal((expectedRead, expectedUnread), new NotificationRetentionOptions().EffectiveFor(company, category));
    }

    [Fact]
    public void Category_overrides_are_validated_against_the_company_values_they_inherit()
    {
        var options = new NotificationRetentionOptions();
        var inherited = (Read: 60, Unread: 90);
        Assert.Empty(options.ValidateOverrides(7, null, inherited, "inherited from the company setting"));
        var errors = options.ValidateOverrides(120, null, inherited, "inherited from the company setting");   // unread 90 inherited < 120
        Assert.Contains(errors, e => e.Contains("inherited from the company setting"));
        Assert.DoesNotContain(errors, e => e.Contains("system default"));
    }

    [Theory]
    [InlineData("Approval", "approval")]
    [InlineData("  decision ", "decision")]
    [InlineData("my_cat-2", "my_cat-2")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void Categories_are_normalised(string? input, string expected) => Assert.Equal(expected, NotificationRetentionSetting.NormaliseCategory(input));

    [Theory]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("a;drop")]
    public void Categories_with_odd_characters_are_refused(string input) =>
        Assert.Throws<DomainException>(() => NotificationRetentionSetting.NormaliseCategory(input));

    [Fact]
    public void A_notification_category_is_stored_lower_case()
    {
        var n = Notification.Create(Company, new UserId(Guid.NewGuid()), " Approval ", "t", null, null, null, Now);
        Assert.Equal("approval", n.Category);
    }

    [Fact]
    public async Task A_category_with_its_own_retention_is_purged_by_it_and_the_rest_of_the_company_by_the_company_values()
    {
        var store = new FakeStore();
        var a = new CompanyId(Guid.NewGuid());
        store.Settings.AddRange([Override(a, 60, 120), Override(a, 7, null, "approval"), Override(a, null, 0, "decision")]);
        await new NotificationRetention(store, new NotificationRetentionOptions(), new FixedClock()).PurgeAsync(default);

        Assert.Equal(4, store.Purges.Count);     // two categories, the rest of the company, everyone else
        var approval = store.Purges.Single(p => p.Scope.Category == "approval");
        Assert.Equal(a, approval.Scope.Only);
        Assert.Equal((Now.AddDays(-7), (DateTimeOffset?)Now.AddDays(-120)), (approval.ReadBefore, approval.UnreadBefore));
        var decision = store.Purges.Single(p => p.Scope.Category == "decision");
        Assert.Equal((Now.AddDays(-60), (DateTimeOffset?)null), (decision.ReadBefore, decision.UnreadBefore));
        var rest = store.Purges.Single(p => p.Scope.Only == a && p.Scope.Category is null);
        Assert.Equal(["approval", "decision"], rest.Scope.ExceptCategories!.Order());
        Assert.Equal((Now.AddDays(-60), (DateTimeOffset?)Now.AddDays(-120)), (rest.ReadBefore, rest.UnreadBefore));
        Assert.Equal([a], store.Purges.Single(p => p.Scope.Only is null).Scope.Except!);
    }

    [Fact]
    public async Task A_company_with_only_category_rows_keeps_the_system_defaults_for_its_other_categories()
    {
        var store = new FakeStore();
        var a = new CompanyId(Guid.NewGuid());
        store.Settings.Add(Override(a, 3, null, "approval"));
        await new NotificationRetention(store, new NotificationRetentionOptions(), new FixedClock()).PurgeAsync(default);
        var rest = store.Purges.Single(p => p.Scope.Only == a && p.Scope.Category is null);
        Assert.Equal((Now.AddDays(-30), (DateTimeOffset?)Now.AddDays(-180)), (rest.ReadBefore, rest.UnreadBefore));
        Assert.Equal(["approval"], rest.Scope.ExceptCategories!);
    }

    // ---- email from the notifier ------------------------------------------------------------------------------------

    private static NotificationMessage Mail(bool email = true) => new("approval", "Needs a decision", "apclerk asked for a change", "/tolerance-requests", "s:1", email);

    [Fact]
    public async Task Flagged_messages_are_also_queued_as_email_for_each_recipient()
    {
        var (notifier, store, users, outbox) = Setup(u => u.UserName.StartsWith("approver"), EmailOn());
        await notifier.NotifyPermissionHoldersAsync("p", Mail(), null, default);
        Assert.Equal(2, store.Added.Count);
        Assert.Equal(new[] { "approver1@example.com", "approver2@example.com" }, outbox.Added.Select(m => m.ToAddress).Order());
        Assert.All(outbox.Added, m => Assert.Equal((Company, EmailStatus.Pending), (m.CompanyId, m.Status)));
        Assert.All(outbox.Added, m => Assert.Contains("https://erp.example.com/tolerance-requests", m.TextBody));
    }

    [Fact]
    public async Task No_email_when_the_message_is_not_flagged_for_email()
    {
        var (notifier, store, _, outbox) = Setup(_ => true, EmailOn());
        await notifier.NotifyPermissionHoldersAsync("p", Mail(email: false), null, default);
        Assert.NotEmpty(store.Added);
        Assert.Empty(outbox.Added);
    }

    [Fact]
    public async Task No_email_when_the_installation_has_email_off_but_the_in_app_message_still_goes()
    {
        var (notifier, store, _, outbox) = Setup(_ => true, new EmailOptions { Enabled = false });
        await notifier.NotifyPermissionHoldersAsync("p", Mail(), null, default);
        Assert.NotEmpty(store.Added);
        Assert.Empty(outbox.Added);
    }

    [Fact]
    public async Task Users_who_turned_email_off_or_have_no_address_get_only_the_in_app_message()
    {
        var (notifier, store, users, outbox) = Setup(_ => true, EmailOn(), us => us[1].SetEmailNotifications(false));
        await notifier.NotifyPermissionHoldersAsync("p", Mail(), null, default);
        Assert.Equal(4, store.Added.Count);                                   // everyone is told in the app
        Assert.DoesNotContain(outbox.Added, m => m.UserId == users[1].Id);    // approver1 opted out
        Assert.Equal(3, outbox.Added.Count);
    }

    [Fact]
    public async Task The_actor_gets_no_email_either()
    {
        var (notifier, _, users, outbox) = Setup(_ => true, EmailOn());
        await notifier.NotifyPermissionHoldersAsync("p", Mail(), users[0].Id.Value, default);
        Assert.DoesNotContain(outbox.Added, m => m.UserId == users[0].Id);
    }

    [Fact]
    public async Task Named_users_can_be_emailed_too()
    {
        var (notifier, _, users, outbox) = Setup(_ => true, EmailOn());
        await notifier.NotifyUsersAsync([users[2].Id.Value], Mail(), default);
        Assert.Equal("approver2@example.com", Assert.Single(outbox.Added).ToAddress);
    }

    [Fact]
    public async Task A_category_opt_out_stops_email_for_that_category_only()
    {
        var (notifier, store, users, outbox) = Setup(_ => true, EmailOn());
        store.OptOuts.Add((users[1].Id, "approval"));
        await notifier.NotifyPermissionHoldersAsync("p", Mail(), null, default);                       // category "approval"
        Assert.Equal(4, store.Added.Count);                                                             // still told in the app
        Assert.DoesNotContain(outbox.Added, m => m.UserId == users[1].Id);
        Assert.Equal(3, outbox.Added.Count);

        outbox.Added.Clear();
        await notifier.NotifyUsersAsync([users[1].Id.Value], new NotificationMessage("decision", "Approved", "b", "/x", "s:2", Email: true), default);
        Assert.Equal(users[1].Id, Assert.Single(outbox.Added).UserId);                                  // other categories are unaffected
    }

    [Fact]
    public async Task Opt_outs_of_other_users_and_categories_do_not_matter()
    {
        var (notifier, store, users, outbox) = Setup(_ => true, EmailOn());
        store.OptOuts.Add((users[1].Id, "decision"));
        store.OptOuts.Add((users[2].Id, "other"));
        await notifier.NotifyPermissionHoldersAsync("p", Mail(), null, default);
        Assert.Equal(4, outbox.Added.Count);
    }

    [Theory]
    [InlineData("approval", false)]
    [InlineData("Approval", false)]
    [InlineData("decision", true)]
    public async Task Preferences_show_each_category_and_changes_are_idempotent(string category, bool expectedEnabled)
    {
        var store = new FakeStore();
        var user = MakeUser("clerk");
        var users = DispatchProxy.Create<IUserStore, Proxy<IUserStore>>();
        ((Proxy<IUserStore>)(object)users).Handlers["GetByIdAsync"] = _ => Task.FromResult<User?>(user);
        var service = new UserPreferenceService(users, store, EmailOn());

        var first = await service.GetAsync(user.Id, default);
        Assert.Equal(new[] { "approval", "decision" }, first!.Categories.Select(c => c.Category));
        Assert.All(first.Categories, c => Assert.True(c.EmailEnabled));

        await service.SetEmailCategoryAsync(user.Id, "approval", false, default);
        var again = await service.SetEmailCategoryAsync(user.Id, "approval", false, default);          // twice: still one opt-out
        Assert.Single(store.OptOuts);
        Assert.Equal(expectedEnabled, again!.Categories.Single(c => c.Category == category.ToLowerInvariant()).EmailEnabled);

        var back = await service.SetEmailCategoryAsync(user.Id, "approval", true, default);
        Assert.Empty(store.OptOuts);
        Assert.All(back!.Categories, c => Assert.True(c.EmailEnabled));
    }

    [Fact]
    public async Task An_unknown_category_the_user_opted_out_of_is_still_listed_and_bad_names_are_refused()
    {
        var store = new FakeStore();
        var user = MakeUser("clerk");
        var users = DispatchProxy.Create<IUserStore, Proxy<IUserStore>>();
        ((Proxy<IUserStore>)(object)users).Handlers["GetByIdAsync"] = _ => Task.FromResult<User?>(user);
        var service = new UserPreferenceService(users, store, EmailOn());
        var result = await service.SetEmailCategoryAsync(user.Id, "custom-x", false, default);
        Assert.Contains(result!.Categories, c => c is { Category: "custom-x", EmailEnabled: false });
        await Assert.ThrowsAsync<DomainException>(() => service.SetEmailCategoryAsync(user.Id, "bad name", false, default));
        await Assert.ThrowsAsync<DomainException>(() => service.SetEmailCategoryAsync(user.Id, "", false, default));
    }
}
