using Erp.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.IntegrationTests;

public class PersistenceTests
{
    [Fact]
    public void Finance_context_uses_finance_schema()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(nameof(Finance_context_uses_finance_schema))
            .Options;

        using var context = new FinanceDbContext(options);

        Assert.Equal("finance", context.Model.GetDefaultSchema());
    }
}
