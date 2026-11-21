using Erp.Modules.Platform.Domain;
using Erp.Modules.Platform.Infrastructure.Persistence;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.IntegrationTests;

/// <summary>The outbox claims rows with raw SQL (UPDATE … FOR UPDATE SKIP LOCKED), so its behaviour is checked against PostgreSQL.</summary>
public sealed class EmailOutboxPostgreSqlTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static EmailMessage Make(CompanyId company, string tag, DateTimeOffset? created = null) =>
        EmailMessage.Create(company, new UserId(Guid.NewGuid()), $"{tag}@example.com", tag, $"Subject {tag}", "text", "<p>html</p>", created ?? Now.AddMinutes(-1));

    private static async Task<List<EmailMessage>> ClaimAsync(DbContextOptions<PlatformDbContext> options, int batch, Guid? token = null, DateTimeOffset? now = null, int leaseMinutes = 5)
    {
        await using var context = new PlatformDbContext(options);
        var at = now ?? Now;
        return (await new EmailOutboxStore(context).ClaimDueAsync(at, batch, at.AddMinutes(leaseMinutes), token ?? Guid.NewGuid(), default)).ToList();
    }

    [Fact]
    public async Task Only_pending_due_messages_are_claimed()
    {
        var options = Options();
        var company = new CompanyId(Guid.NewGuid());
        var due = Make(company, "due");
        var future = Make(company, "future"); future.MarkFailed("x", false, Now, 5, TimeSpan.FromHours(1));   // retry not due for an hour
        var sent = Make(company, "sent"); sent.MarkSent(Now);
        var failed = Make(company, "failed"); failed.MarkFailed("x", true, Now, 5, null);
        await SeedAsync(options, due, future, sent, failed);
        try
        {
            var claimed = await ClaimAsync(options, 50);
            Assert.Contains(claimed, m => m.Id == due.Id);
            Assert.DoesNotContain(claimed, m => m.Id == future.Id || m.Id == sent.Id || m.Id == failed.Id);
            Assert.All(claimed.Where(m => m.Id == due.Id), m => Assert.NotNull(m.LeasedUntil));
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task A_claimed_message_is_not_handed_out_again_until_its_lease_lapses()
    {
        var options = Options();
        var company = new CompanyId(Guid.NewGuid());
        var mail = Make(company, "leased");
        await SeedAsync(options, mail);
        try
        {
            Assert.Contains(await ClaimAsync(options, 50, leaseMinutes: 5), m => m.Id == mail.Id);
            Assert.DoesNotContain(await ClaimAsync(options, 50), m => m.Id == mail.Id);                                 // still reserved
            Assert.Contains(await ClaimAsync(options, 50, now: Now.AddMinutes(6)), m => m.Id == mail.Id);               // a crashed worker's lease lapsed
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task Concurrent_workers_never_get_the_same_message()
    {
        var options = Options();
        var company = new CompanyId(Guid.NewGuid());
        var mails = Enumerable.Range(0, 60).Select(i => Make(company, $"c{i}", Now.AddMinutes(-2).AddSeconds(i))).ToArray();
        await SeedAsync(options, mails);
        try
        {
            var ids = mails.Select(m => m.Id).ToHashSet();
            var batches = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ClaimAsync(options, 15)));
            var mine = batches.Select(b => b.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToList()).ToList();
            var all = mine.SelectMany(x => x).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());     // nothing claimed twice
            Assert.True(all.Count > 0);
            Assert.All(mine, b => Assert.True(b.Count <= 15));   // the batch size is respected
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task The_oldest_due_messages_go_first()
    {
        var options = Options();
        var company = new CompanyId(Guid.NewGuid());
        var oldest = Make(company, "oldest", Now.AddHours(-5)); var newer = Make(company, "newer", Now.AddHours(-1));
        await SeedAsync(options, newer, oldest);
        try
        {
            var claimed = (await ClaimAsync(options, 1000)).Where(m => m.CompanyId == company).ToList();
            Assert.Equal(["oldest", "newer"], claimed.Select(m => m.ToName!));
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task Sending_after_a_claim_persists_and_releases_the_lease()
    {
        var options = Options();
        var company = new CompanyId(Guid.NewGuid());
        var mail = Make(company, "send");
        await SeedAsync(options, mail);
        try
        {
            await using (var context = new PlatformDbContext(options))
            {
                var store = new EmailOutboxStore(context);
                var claimed = (await store.ClaimDueAsync(Now, 50, Now.AddMinutes(5), Guid.NewGuid(), default)).Single(m => m.Id == mail.Id);
                claimed.MarkSent(Now);
                await store.SaveChangesAsync(default);
            }

            await using var verify = new PlatformDbContext(options);
            var saved = await verify.EmailMessages.AsNoTracking().SingleAsync(m => m.Id == mail.Id);
            Assert.Equal((EmailStatus.Sent, 1, null, null), (saved.Status, saved.Attempts, saved.LeaseToken, saved.LeasedUntil));
            Assert.DoesNotContain(await ClaimAsync(options, 50, now: Now.AddHours(1)), m => m.Id == mail.Id);    // sent mail is never claimed again
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task Purge_removes_old_sent_and_old_failed_messages_but_not_pending_or_recent_ones()
    {
        var options = Options();
        var company = new CompanyId(Guid.NewGuid());
        var oldSent = Make(company, "oldsent", Now.AddDays(-30)); oldSent.MarkSent(Now.AddDays(-20));
        var recentSent = Make(company, "recentsent", Now.AddDays(-2)); recentSent.MarkSent(Now.AddDays(-1));
        var oldFailed = Make(company, "oldfailed", Now.AddDays(-200)); oldFailed.MarkFailed("x", true, Now.AddDays(-199), 5, null);
        var recentFailed = Make(company, "recentfailed", Now.AddDays(-5)); recentFailed.MarkFailed("x", true, Now.AddDays(-5), 5, null);
        var oldPending = Make(company, "oldpending", Now.AddDays(-300));      // never sent: must not be discarded silently
        await SeedAsync(options, oldSent, recentSent, oldFailed, recentFailed, oldPending);
        try
        {
            await using (var context = new PlatformDbContext(options))
                Assert.True(await new EmailOutboxStore(context).PurgeAsync(Now.AddDays(-14), Now.AddDays(-90), default) >= 2);
            await using var verify = new PlatformDbContext(options);
            var left = await verify.EmailMessages.AsNoTracking().Where(m => m.CompanyId == company).Select(m => m.ToName).ToListAsync();
            Assert.Equal(new[] { "oldpending", "recentfailed", "recentsent" }, left.Order());
        }
        finally { await CleanUpAsync(options, company); }
    }

    [Fact]
    public async Task Status_counts_this_companys_outbox_only()
    {
        var options = Options();
        var a = new CompanyId(Guid.NewGuid()); var b = new CompanyId(Guid.NewGuid());
        var pending = Make(a, "p"); var failed = Make(a, "f"); failed.MarkFailed("550 boom", true, Now, 5, null);
        var sent = Make(a, "s"); sent.MarkSent(Now.AddHours(-1));
        var oldSent = Make(a, "o"); oldSent.MarkSent(Now.AddDays(-3));
        var other = Make(b, "other");
        await SeedAsync(options, pending, failed, sent, oldSent, other);
        try
        {
            await using var context = new PlatformDbContext(options);
            var status = await new EmailOutboxStore(context).StatusAsync(a, Now, default);
            Assert.Equal((1, 1, 1), (status.Pending, status.Failed, status.SentLast24Hours));
            Assert.Equal("550 boom", status.LastError);
            Assert.NotNull(status.LastSentAt);
            Assert.NotNull(status.OldestPendingAt);
            var empty = await new EmailOutboxStore(context).StatusAsync(new CompanyId(Guid.NewGuid()), Now, default);
            Assert.Equal((0, 0, 0, null), (empty.Pending, empty.Failed, empty.SentLast24Hours, empty.LastSentAt));
        }
        finally { await CleanUpAsync(options, a); await CleanUpAsync(options, b); }
    }

    private static async Task SeedAsync(DbContextOptions<PlatformDbContext> options, params EmailMessage[] mails)
    {
        await using var context = new PlatformDbContext(options);
        context.EmailMessages.AddRange(mails);
        await context.SaveChangesAsync();
    }

    private static async Task CleanUpAsync(DbContextOptions<PlatformDbContext> options, CompanyId company)
    {
        await using var context = new PlatformDbContext(options);
        await context.EmailMessages.Where(x => x.CompanyId == company).ExecuteDeleteAsync();
    }

    private static DbContextOptions<PlatformDbContext> Options() =>
        new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(RequireConnectionString()) { MaxPoolSize = 20 }.ConnectionString).Options;

    private static string RequireConnectionString() =>
        Environment.GetEnvironmentVariable("ERP_TEST_DATABASE_CONNECTION")
        ?? throw new InvalidOperationException(
            "Set ERP_TEST_DATABASE_CONNECTION to a disposable PostgreSQL database before running PostgreSQL integration tests.");
}
