using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public sealed class AuthorizationTests
{
    [Fact]
    public void Role_code_is_normalised()
    {
        var role = Role.Create(" finance_manager ", "Finance Manager", null, false, DateTimeOffset.UtcNow);
        Assert.Equal("FINANCE_MANAGER", role.Code);
    }

    [Fact]
    public void Invalid_role_code_is_rejected()
    {
        Assert.Throws<DomainException>(() =>
            Role.Create("not valid", "Role", null, false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Role_lifecycle_changes_status()
    {
        var role = Role.Create("SALES", "Sales", null, false, DateTimeOffset.UtcNow);
        role.Deactivate();
        Assert.Equal(RoleStatus.Inactive, role.Status);
        role.Activate();
        Assert.Equal(RoleStatus.Active, role.Status);
    }

    [Fact]
    public void Permission_code_requires_three_segments()
    {
        Assert.Throws<DomainException>(() =>
            Permission.Create("finance.accounts", "Accounts", "finance", null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Permission_code_is_normalised()
    {
        var permission = Permission.Create(" Finance.Accounts.Read ", "Read accounts", "finance", null, DateTimeOffset.UtcNow);
        Assert.Equal("finance.accounts.read", permission.Code);
    }

    [Fact]
    public void User_company_access_starts_active()
    {
        var userId = UserId.New();
        var companyId = CompanyId.New();
        var access = UserCompanyAccess.Create(userId, companyId, userId, DateTimeOffset.UtcNow);

        Assert.Equal(UserCompanyAccessStatus.Active, access.Status);
        Assert.Equal(userId, access.UserId);
        Assert.Equal(companyId, access.CompanyId);
    }

    [Fact]
    public void User_company_access_can_be_deactivated_and_restored()
    {
        var actor = UserId.New();
        var access = UserCompanyAccess.Create(UserId.New(), CompanyId.New(), actor, DateTimeOffset.UtcNow);

        access.Deactivate(actor, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(UserCompanyAccessStatus.Inactive, access.Status);

        access.Activate(actor, DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.Equal(UserCompanyAccessStatus.Active, access.Status);
    }
}
