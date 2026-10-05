using System.Reflection;

namespace Erp.ArchitectureTests;

public class DependencyTests
{
    [Fact]
    public void Domain_projects_do_not_reference_infrastructure_or_aspnet()
    {
        var assemblies = new[]
        {
            typeof(Erp.Modules.Finance.Domain.Account).Assembly,
            typeof(Erp.Modules.Sales.Domain.SalesOrder).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var names = assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
            Assert.DoesNotContain(names, x => x is not null && (x.Contains("Infrastructure") || x.Contains("AspNetCore")));
        }
    }

    [Fact]
    public void Application_abstractions_do_not_reference_api()
    {
        var names = typeof(Erp.Application.Abstractions.IClock).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name);

        Assert.DoesNotContain(names, x => x is not null && x.Contains("Erp.Api"));
    }
}
