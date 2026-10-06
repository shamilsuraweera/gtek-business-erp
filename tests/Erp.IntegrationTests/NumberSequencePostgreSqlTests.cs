using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.Modules.Platform.Infrastructure.Persistence;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.IntegrationTests;

public sealed class NumberSequencePostgreSqlTests
{
    [Fact]
    public async Task Concurrent_reservations_use_distinct_numbers_across_contexts()
    {
        var connectionString = RequireConnectionString();
        var company = Company.Create("T" + Guid.NewGuid().ToString("N")[..7], "Sequence test company", DateTimeOffset.UtcNow);
        var companyId = company.Id;
        var options = CreateOptions(connectionString);

        await using (var setup = new PlatformDbContext(options))
        {
            setup.Companies.Add(company);
            await setup.SaveChangesAsync();
            setup.NumberSequences.Add(NumberSequence.Create(
                companyId, "CONCURRENT", "Concurrent sequence", "CT-", null, 1, 8, 1, DateTimeOffset.UtcNow, null));
            await setup.SaveChangesAsync();
        }

        try
        {
            var issued = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => ReserveAsync(options, companyId)));

            Assert.Equal(100, issued.Length);
            Assert.Equal(100, issued.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(
                Enumerable.Range(1, 100).Select(value => $"CT-{value:00000000}"),
                issued.OrderBy(value => value, StringComparer.Ordinal));

            await using var verify = new PlatformDbContext(options);
            var sequence = await verify.NumberSequences.SingleAsync(x => x.CompanyId == companyId);
            Assert.Equal(101, sequence.NextValue);
        }
        finally
        {
            await using var cleanup = new PlatformDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM platform.\"NumberSequences\" WHERE \"CompanyId\" = {companyId.Value}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM platform.\"Companies\" WHERE \"Id\" = {companyId.Value}");
        }
    }

    [Fact]
    public async Task Same_code_is_independent_between_companies_and_unique_within_company()
    {
        var connectionString = RequireConnectionString();
        var firstCompany = Company.Create("T" + Guid.NewGuid().ToString("N")[..7], "First sequence company", DateTimeOffset.UtcNow);
        var secondCompany = Company.Create("T" + Guid.NewGuid().ToString("N")[..7], "Second sequence company", DateTimeOffset.UtcNow);
        var firstCompanyId = firstCompany.Id;
        var secondCompanyId = secondCompany.Id;
        var options = CreateOptions(connectionString);

        await using (var setup = new PlatformDbContext(options))
        {
            setup.Companies.AddRange(
                firstCompany,
                secondCompany);
            await setup.SaveChangesAsync();
            setup.NumberSequences.AddRange(
                NumberSequence.Create(firstCompanyId, "SALES-ORDER", "Sales orders", "SO-", null, 1, 6, 1, DateTimeOffset.UtcNow, null),
                NumberSequence.Create(secondCompanyId, "SALES-ORDER", "Sales orders", "SO-", null, 1, 6, 1, DateTimeOffset.UtcNow, null));
            await setup.SaveChangesAsync();
        }

        try
        {
            Assert.Equal("SO-000001", await ReserveAsync(options, firstCompanyId, "SALES-ORDER"));
            Assert.Equal("SO-000001", await ReserveAsync(options, secondCompanyId, "SALES-ORDER"));

            await using var duplicate = new PlatformDbContext(options);
            duplicate.NumberSequences.Add(NumberSequence.Create(
                firstCompanyId, "SALES-ORDER", "Duplicate", "SO-", null, 1, 6, 1, DateTimeOffset.UtcNow, null));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }
        finally
        {
            await using var cleanup = new PlatformDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM platform.\"NumberSequences\" WHERE \"CompanyId\" IN ({firstCompanyId.Value}, {secondCompanyId.Value})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM platform.\"Companies\" WHERE \"Id\" IN ({firstCompanyId.Value}, {secondCompanyId.Value})");
        }
    }

    private static async Task<string> ReserveAsync(
        DbContextOptions<PlatformDbContext> options,
        CompanyId companyId,
        string code = "CONCURRENT")
    {
        await using var context = new PlatformDbContext(options);
        var store = new NumberSequenceStore(context);
        return await store.ReserveNextAsync(companyId, code, CancellationToken.None);
    }

    private static DbContextOptions<PlatformDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString)
            .Options;

    private static string RequireConnectionString() =>
        Environment.GetEnvironmentVariable("ERP_TEST_DATABASE_CONNECTION")
        ?? throw new InvalidOperationException(
            "Set ERP_TEST_DATABASE_CONNECTION to a disposable PostgreSQL database before running PostgreSQL integration tests.");
}
