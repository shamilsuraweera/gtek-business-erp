using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.Modules.Platform.Infrastructure.Persistence;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.IntegrationTests;

/// <summary>The purge runs real SQL (batched ExecuteDelete over a sub-query), so it is checked against PostgreSQL.</summary>
public sealed class NotificationRetentionPostgreSqlTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static DateTimeOffset DaysAgo(int days) => Now.AddDays(-days);

    private static Notification Make(CompanyId company, UserId user, string title, int createdDaysAgo, int? readDaysAgo = null, string category = "test")
    {
        var n = Notification.Create(company, user, category, title, null, null, null, DaysAgo(createdDaysAgo));
        if (readDaysAgo is { } d) n.MarkRead(DaysAgo(d));
        return n;
    }

    [Fact]
    public async Task Purge_removes_only_what_is_past_its_retention()
    {
        var options = CreateOptions(RequireConnectionString());
        var company = new CompanyId(Guid.NewGuid());
        var user = new UserId(Guid.NewGuid());
        await using (var setup = new PlatformDbContext(options))
        {
            setup.Notifications.AddRange(
                Make(company, user, "read 40 days ago", createdDaysAgo: 41, readDaysAgo: 40),       // read long ago: purge
                Make(company, user, "read 10 days ago, sent 100 days ago", 100, 10),                // retention counts from reading: keep
                Make(company, user, "read 29 days ago", 30, 29),                                    // inside the window: keep
                Make(company, user, "unread 200 days", 200),                                        // unread past 180: purge
                Make(company, user, "unread 100 days", 100),                                        // unread inside 180: keep
                Make(company, user, "unread 5 days", 5));
            await setup.SaveChangesAsync();
        }

        try
        {
            var result = await PurgeAsync(options, readDays: 30, unreadDays: 180, batch: 1000);
            Assert.True(result.Read >= 1 && result.Unread >= 1);
            Assert.Equal(
                new[] { "read 10 days ago, sent 100 days ago", "read 29 days ago", "unread 100 days", "unread 5 days" }.Order(),
                (await TitlesAsync(options, company)).Order());
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task Unread_messages_are_kept_forever_when_unread_days_is_zero()
    {
        var options = CreateOptions(RequireConnectionString());
        var company = new CompanyId(Guid.NewGuid());
        var user = new UserId(Guid.NewGuid());
        await using (var setup = new PlatformDbContext(options))
        {
            setup.Notifications.AddRange(Make(company, user, "unread 1000 days", 1000), Make(company, user, "read 40 days ago", 41, 40));
            await setup.SaveChangesAsync();
        }

        try
        {
            await PurgeAsync(options, readDays: 30, unreadDays: 0, batch: 1000);
            Assert.Equal(["unread 1000 days"], await TitlesAsync(options, company));
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task A_large_backlog_is_deleted_in_several_batches_and_a_second_run_finds_nothing()
    {
        var options = CreateOptions(RequireConnectionString());
        var company = new CompanyId(Guid.NewGuid());
        var user = new UserId(Guid.NewGuid());
        await using (var setup = new PlatformDbContext(options))
        {
            setup.Notifications.AddRange(Enumerable.Range(0, 2350).Select(i => Make(company, user, $"old read {i}", 60, 50)));
            setup.Notifications.AddRange(Enumerable.Range(0, 300).Select(i => Make(company, user, $"old unread {i}", 400)));
            setup.Notifications.Add(Make(company, user, "fresh", 1));
            await setup.SaveChangesAsync();
        }

        try
        {
            var first = await PurgeAsync(options, readDays: 30, unreadDays: 180, batch: 1000);   // 3 batches of read, 1 of unread
            Assert.True(first.Read >= 2350 && first.Unread >= 300, $"{first}");
            Assert.Equal(["fresh"], await TitlesAsync(options, company));
            var second = await PurgeAsync(options, readDays: 30, unreadDays: 180, batch: 1000);
            Assert.Equal(["fresh"], await TitlesAsync(options, company));
            Assert.True(second.Total < first.Total);
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task Another_users_fresh_messages_are_untouched_across_companies()
    {
        var options = CreateOptions(RequireConnectionString());
        var a = new CompanyId(Guid.NewGuid());
        var b = new CompanyId(Guid.NewGuid());
        await using (var setup = new PlatformDbContext(options))
        {
            setup.Notifications.AddRange(
                Make(a, new UserId(Guid.NewGuid()), "a old", 90, 80), Make(a, new UserId(Guid.NewGuid()), "a fresh", 1),
                Make(b, new UserId(Guid.NewGuid()), "b old", 90, 80), Make(b, new UserId(Guid.NewGuid()), "b fresh", 1));
            await setup.SaveChangesAsync();
        }

        try
        {
            await PurgeAsync(options, 30, 180, 1000);
            Assert.Equal(["a fresh"], await TitlesAsync(options, a));
            Assert.Equal(["b fresh"], await TitlesAsync(options, b));   // the policy is system-wide: both companies are purged
        }
        finally { await CleanUpAsync(options, a); await CleanUpAsync(options, b); }
    }

    private static async Task<PurgeResult> PurgeAsync(
        DbContextOptions<PlatformDbContext> options, int readDays, int unreadDays, int batch, PurgeScope? scope = null)
    {
        await using var context = new PlatformDbContext(options);
        return await new NotificationStore(context).PurgeAsync(scope ?? PurgeScope.AllExcept([]),
            Now.AddDays(-readDays), unreadDays == 0 ? null : Now.AddDays(-unreadDays), batch, CancellationToken.None);
    }

    [Fact]
    public async Task A_scope_limits_the_purge_to_one_company_or_spares_named_companies()
    {
        var options = CreateOptions(RequireConnectionString());
        var a = new CompanyId(Guid.NewGuid());
        var b = new CompanyId(Guid.NewGuid());
        var c = new CompanyId(Guid.NewGuid());
        async Task Seed()
        {
            await using var setup = new PlatformDbContext(options);
            foreach (var company in new[] { a, b, c })
                setup.Notifications.AddRange(Make(company, new UserId(Guid.NewGuid()), "old read", 90, 80), Make(company, new UserId(Guid.NewGuid()), "old unread", 300), Make(company, new UserId(Guid.NewGuid()), "fresh", 1));
            await setup.SaveChangesAsync();
        }

        await Seed();
        try
        {
            await PurgeAsync(options, 30, 180, 1000, PurgeScope.Company(a));
            Assert.Equal(["fresh"], await TitlesAsync(options, a));
            Assert.Equal(3, (await TitlesAsync(options, b)).Count);      // other companies untouched
            Assert.Equal(3, (await TitlesAsync(options, c)).Count);

            await PurgeAsync(options, 30, 180, 1000, PurgeScope.AllExcept([b]));
            Assert.Equal(["fresh"], await TitlesAsync(options, c));
            Assert.Equal(3, (await TitlesAsync(options, b)).Count);      // spared company untouched

            // The spared company has its own retention: read messages 60 days after reading, unread kept forever.
            await PurgeAsync(options, 60, 0, 1000, PurgeScope.Company(b));
            Assert.Equal(new[] { "old unread", "fresh" }.Order(), (await TitlesAsync(options, b)).Order());   // "old read" (read 80 days ago) goes; the unread one stays
        }
        finally { await CleanUpAsync(options, a); await CleanUpAsync(options, b); await CleanUpAsync(options, c); }
    }

    [Fact]
    public async Task A_category_scope_touches_only_that_category_and_an_except_scope_spares_it()
    {
        var options = CreateOptions(RequireConnectionString());
        var company = new CompanyId(Guid.NewGuid());
        var other = new CompanyId(Guid.NewGuid());
        var user = new UserId(Guid.NewGuid());
        await using (var setup = new PlatformDbContext(options))
        {
            foreach (var c in new[] { company, other })
                setup.Notifications.AddRange(
                    Make(c, user, "approval read 20d", 30, 20, "approval"),
                    Make(c, user, "decision read 20d", 30, 20, "decision"),
                    Make(c, user, "general read 20d", 30, 20, "general"));
            await setup.SaveChangesAsync();
        }

        try
        {
            // The approval category keeps read messages for 10 days: its 20-day-old one goes, the other categories stay.
            await PurgeAsync(options, 10, 180, 1000, PurgeScope.CompanyCategory(company, "approval"));
            Assert.Equal(new[] { "decision read 20d", "general read 20d" }.Order(), (await TitlesAsync(options, company)).Order());
            Assert.Equal(3, (await TitlesAsync(options, other)).Count);   // another company's approval message is untouched

            // The rest of the company keeps read messages 30 days, sparing the categories with their own rule.
            await PurgeAsync(options, 30, 180, 1000, PurgeScope.Company(company, ["decision"]));
            Assert.Equal(2, (await TitlesAsync(options, company)).Count);

            // Sparing a category means it is not purged even with a rule that would remove everything read.
            await PurgeAsync(options, 1, 180, 1000, PurgeScope.Company(company, ["decision"]));
            Assert.Equal(["decision read 20d"], await TitlesAsync(options, company));
        }
        finally { await CleanUpAsync(options, company); await CleanUpAsync(options, other); }
    }

    [Fact]
    public async Task Category_rows_are_unique_per_company_and_company_wide_rows_coexist_with_them()
    {
        var options = CreateOptions(RequireConnectionString());
        var company = new CompanyId(Guid.NewGuid());
        await using (var context = new PlatformDbContext(options))
        {
            var wide = NotificationRetentionSetting.Create(company);
            wide.Set(60, null, Now, null);
            var cat = NotificationRetentionSetting.Create(company, "Approval");
            cat.Set(7, null, Now, null);
            context.NotificationRetentionSettings.AddRange(wide, cat);
            await context.SaveChangesAsync();
        }

        try
        {
            await using var context = new PlatformDbContext(options);
            var store = new NotificationStore(context);
            Assert.Equal(60, (await store.GetRetentionSettingAsync(company, NotificationRetentionSetting.CompanyWide, default))!.ReadDays);
            Assert.Equal(7, (await store.GetRetentionSettingAsync(company, "approval", default))!.ReadDays);
            Assert.Null(await store.GetRetentionSettingAsync(company, "decision", default));
            Assert.Equal(2, (await store.ListRetentionSettingsAsync(company, default)).Count);

            await using var again = new PlatformDbContext(options);
            var duplicate = NotificationRetentionSetting.Create(company, "approval");
            duplicate.Set(9, null, Now, null);
            again.NotificationRetentionSettings.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());
        }
        finally
        {
            await using var cleanup = new PlatformDbContext(options);
            await cleanup.NotificationRetentionSettings.Where(x => x.CompanyId == company).ExecuteDeleteAsync();
        }
    }

    private static async Task<List<string>> TitlesAsync(DbContextOptions<PlatformDbContext> options, CompanyId company)
    {
        await using var context = new PlatformDbContext(options);
        return await context.Notifications.AsNoTracking().Where(x => x.CompanyId == company).Select(x => x.Title).ToListAsync();
    }

    private static async Task CleanUpAsync(DbContextOptions<PlatformDbContext> options, CompanyId company)
    {
        await using var context = new PlatformDbContext(options);
        await context.Notifications.Where(x => x.CompanyId == company).ExecuteDeleteAsync();
    }

    private static DbContextOptions<PlatformDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options;

    private static string RequireConnectionString() =>
        Environment.GetEnvironmentVariable("ERP_TEST_DATABASE_CONNECTION")
        ?? throw new InvalidOperationException(
            "Set ERP_TEST_DATABASE_CONNECTION to a disposable PostgreSQL database before running PostgreSQL integration tests.");
}
