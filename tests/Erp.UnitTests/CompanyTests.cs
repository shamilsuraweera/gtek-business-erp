using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public sealed class CompanyTests
{
    [Fact]
    public void Valid_company_creation_normalizes_code()
    {
        var company = Company.Create(" demo ", " Demo Company ", DateTimeOffset.UtcNow);

        Assert.Equal("DEMO", company.Code);
        Assert.Equal("Demo Company", company.Name);
        Assert.Equal(CompanyStatus.Active, company.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("A B")]
    public void Invalid_company_code_is_rejected(string code)
    {
        Assert.Throws<DomainException>(() => Company.Create(code, "Company", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_company_name_is_rejected(string name)
    {
        Assert.Throws<DomainException>(() => Company.Create("DEMO", name, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Company_status_and_name_change_through_behaviour()
    {
        var company = Company.Create("DEMO", "Demo", DateTimeOffset.UtcNow);
        company.Deactivate();
        Assert.Equal(CompanyStatus.Inactive, company.Status);

        company.Activate();
        company.Rename("Renamed");

        Assert.Equal(CompanyStatus.Active, company.Status);
        Assert.Equal("Renamed", company.Name);
        Assert.NotNull(company.ModifiedAt);
    }
}
