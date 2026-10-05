using Erp.Modules.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Modules.Platform.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id)
            .HasConversion(id => id.Value, value => new Erp.SharedKernel.UserId(value));
        builder.Property(user => user.UserName).HasMaxLength(100).IsRequired();
        builder.HasIndex(user => user.UserName).IsUnique();
        builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(user => user.CreatedAt).IsRequired();
        builder.Property(user => user.ModifiedAt);
        builder.Property(user => user.IsBootstrapAdministrator).IsRequired();
    }
}
