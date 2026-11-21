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
        public (DateTimeOffset ReadBefore, DateTimeOffset? UnreadBefore, int Batch)? Purged { get; private set; }
        public Task<PurgeResult> PurgeAsync(DateTimeOffset readBefore, DateTimeOffset? unreadBefore, int batchSize, CancellationToken ct)
        {
            Purged = (readBefore, unreadBefore, batchSize);
            return Task.FromResult(new PurgeResult(2, 1));
        }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Answers the two calls the notifier makes and rejects everything else.</summary>
    public class Proxy<T> : DispatchProxy where T : class
    {
        public Dictionary<string, Func<object?[], object?>> Handlers { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Handlers.TryGetValue(method!.Name, out var h) ? h(args ?? []) : throw new NotSupportedException(method.Name);
    }

    private sealed class FixedCompany(CompanyId id) : ICompanyContext { public CompanyId CompanyId => id; public bool HasCompany => true; }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }

    private static User MakeUser(string name) => Erp.Modules.Platform.Domain.User.Create(name, $"{name}@example.com", Now);

    private static (Notifier Notifier, FakeStore Store, User[] Users) Setup(Func<User, bool> hasPermission)
    {
        var users = new[] { MakeUser("clerk"), MakeUser("approver1"), MakeUser("approver2"), MakeUser("viewer") };
        var access = DispatchProxy.Create<IUserCompanyAccessStore, Proxy<IUserCompanyAccessStore>>();
        ((Proxy<IUserCompanyAccessStore>)(object)access).Handlers["ListUsersForCompanyAsync"] = _ => Task.FromResult<IReadOnlyList<User>>(users);
        var perms = DispatchProxy.Create<IRolePermissionStore, Proxy<IRolePermissionStore>>();
        ((Proxy<IRolePermissionStore>)(object)perms).Handlers["UserHasPermissionAsync"] = a => Task.FromResult(hasPermission(users.Single(u => u.Id == (UserId)a[0]!)));
        var store = new FakeStore();
        return (new Notifier(store, access, perms, new FixedCompany(Company), new FixedClock()), store, users);
    }

    [Fact]
    public async Task Permission_holders_are_notified_except_the_actor()
    {
        var (notifier, store, users) = Setup(u => u.UserName.StartsWith("approver") || u.UserName == "clerk");
        await notifier.NotifyPermissionHoldersAsync("p", new NotificationMessage("approval", "Needs a decision", "b", "/x", "s:1"), users[0].Id.Value, default);
        Assert.Equal(new[] { "approver1", "approver2" }.Order(), store.Added.Select(n => users.Single(u => u.Id == n.UserId).UserName).Order());
        Assert.All(store.Added, n => Assert.Equal((Company, "s:1", "approval"), (n.CompanyId, n.Subject, n.Category)));
    }

    [Fact]
    public async Task Nobody_is_notified_when_nobody_holds_the_permission()
    {
        var (notifier, store, _) = Setup(_ => false);
        await notifier.NotifyPermissionHoldersAsync("p", new NotificationMessage("approval", "T", null, null), null, default);
        Assert.Empty(store.Added);
    }

    [Fact]
    public async Task Named_users_are_notified_only_if_they_belong_to_the_company_and_only_once()
    {
        var (notifier, store, users) = Setup(_ => true);
        await notifier.NotifyUsersAsync([users[0].Id.Value, users[0].Id.Value, Guid.NewGuid()], new NotificationMessage("decision", "T", null, null), default);
        Assert.Single(store.Added);
        Assert.Equal(users[0].Id, store.Added[0].UserId);
    }

    [Fact]
    public async Task Resolving_a_subject_marks_it_read_for_everyone_in_the_company()
    {
        var (notifier, store, _) = Setup(_ => true);
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
    }

    [Fact]
    public async Task Unread_days_of_zero_means_unread_messages_are_never_purged()
    {
        var store = new FakeStore();
        await new NotificationRetention(store, new NotificationRetentionOptions { UnreadDays = 0 }, new FixedClock()).PurgeAsync(default);
        Assert.Null(store.Purged!.Value.UnreadBefore);
    }
}
