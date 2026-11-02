using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class UserCredential
{
    private UserCredential()
    {
        PasswordHash = string.Empty;
    }

    public UserCredential(UserId userId, string passwordHash)
    {
        UserId = userId;
        PasswordHash = passwordHash;
    }

    public UserId UserId { get; private set; }
    public string PasswordHash { get; private set; }
}

public sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.HasKey(credential => credential.UserId);
        builder.Property(credential => credential.UserId)
            .HasConversion(id => id.Value, value => new Erp.SharedKernel.UserId(value));
        builder.Property(credential => credential.PasswordHash).HasMaxLength(500).IsRequired();
        builder.HasOne<User>().WithOne().HasForeignKey<UserCredential>(credential => credential.UserId);
        builder.ToTable("UserCredentials");
    }
}
