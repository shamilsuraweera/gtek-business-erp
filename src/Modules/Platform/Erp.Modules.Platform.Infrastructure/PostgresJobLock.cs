using Erp.Application.Abstractions;
using Npgsql;

namespace Erp.Modules.Platform.Infrastructure;

/// <summary>
/// A PostgreSQL session-level advisory lock held on its own connection for as long as the handle lives. If the process dies the connection
/// closes and the database releases the lock, so a crashed instance never leaves a job locked.
/// </summary>
public sealed class PostgresJobLock(string connectionString) : IJobLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@name, 0))", connection);
            command.Parameters.AddWithValue("name", "job:" + name);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!) return new Handle(connection, name);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Handle(NpgsqlConnection connection, string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@name, 0))", connection);
                command.Parameters.AddWithValue("name", "job:" + name);
                await command.ExecuteScalarAsync();
            }
            catch
            {
                // Closing the connection below releases the lock anyway.
            }

            await connection.DisposeAsync();
        }
    }
}
