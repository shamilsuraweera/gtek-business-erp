using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public sealed class UserTests
{
    [Fact]
    public void User_normalises_username_and_email()
    {
        var user = User.Create(" Admin.User ", " ADMIN@Example.COM ", DateTimeOffset.UtcNow);

        Assert.Equal("admin.user", user.UserName);
        Assert.Equal("admin@example.com", user.Email);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Invalid_username_is_rejected()
    {
        Assert.Throws<DomainException>(() => User.Create("ab", "admin@example.com", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Invalid_email_is_rejected()
    {
        Assert.Throws<DomainException>(() => User.Create("admin", "not-an-email", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void User_lifecycle_changes_status()
    {
        var user = User.Create("admin", "admin@example.com", DateTimeOffset.UtcNow);

        user.Deactivate();
        Assert.Equal(UserStatus.Inactive, user.Status);

        user.Activate();
        Assert.Equal(UserStatus.Active, user.Status);
    }
}
