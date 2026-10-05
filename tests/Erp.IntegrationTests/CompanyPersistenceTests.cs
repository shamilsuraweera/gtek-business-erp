using Erp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.IntegrationTests;

public sealed class CompanyPersistenceTests
{
    [Fact]
    public async Task Company_is_persisted_and_code_is_unique()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(nameof(Company_is_persisted_and_code_is_unique))
            .Options;

        await using var context = new PlatformDbContext(options);
        var company = Erp.Modules.Platform.Domain.Company.Create("DEMO", "Demo", DateTimeOffset.UtcNow);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        Assert.NotNull(await context.Companies.SingleAsync());
        Assert.True(context.Model.FindEntityType(typeof(Erp.Modules.Platform.Domain.Company))!
            .GetIndexes().Single(index => index.Properties.Any(property => property.Name == "Code")).IsUnique);
    }
}
