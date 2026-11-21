using Erp.Modules.Platform.Infrastructure;

namespace Erp.IntegrationTests;

/// <summary>The cross-instance job lock is a PostgreSQL advisory lock, so it is checked against PostgreSQL.</summary>
public sealed class JobLockPostgreSqlTests
{
    private static string Connection() =>
        Environment.GetEnvironmentVariable("ERP_TEST_DATABASE_CONNECTION")
        ?? throw new InvalidOperationException("Set ERP_TEST_DATABASE_CONNECTION to a disposable PostgreSQL database before running PostgreSQL integration tests.");

    private static string Name() => "test-" + Guid.NewGuid();

    [Fact]
    public async Task A_held_lock_cannot_be_taken_by_another_instance_until_it_is_released()
    {
        var name = Name();
        var first = new PostgresJobLock(Connection());
        var second = new PostgresJobLock(Connection());          // another instance: its own connections

        await using (var held = await first.TryAcquireAsync(name, default))
        {
            Assert.NotNull(held);
            Assert.Null(await second.TryAcquireAsync(name, default));
        }

        await using var again = await second.TryAcquireAsync(name, default);
        Assert.NotNull(again);                                    // released on dispose
    }

    [Fact]
    public async Task Different_names_do_not_block_each_other()
    {
        var locks = new PostgresJobLock(Connection());
        await using var a = await locks.TryAcquireAsync(Name(), default);
        await using var b = await locks.TryAcquireAsync(Name(), default);
        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task Of_many_simultaneous_attempts_exactly_one_wins()
    {
        var name = Name();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => new PostgresJobLock(Connection()).TryAcquireAsync(name, default)));
        try
        {
            Assert.Equal(1, attempts.Count(x => x is not null));
        }
        finally
        {
            foreach (var attempt in attempts)
                if (attempt is not null) await attempt.DisposeAsync();
        }
    }
}
