using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Erp.Modules.Platform.Domain;
using Erp.Modules.Platform.Infrastructure.Persistence;

namespace Erp.Modules.Platform.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new Erp.SharedKernel.RoleId(x));
        b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.ToTable("Roles", "platform");
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new Erp.SharedKernel.PermissionId(x));
        b.Property(x => x.Code).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Module).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.ToTable("Permissions", "platform");
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRoleEntity>
{
    public void Configure(EntityTypeBuilder<UserRoleEntity> b)
    {
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.Property(x => x.UserId).HasConversion(x => x.Value, x => new Erp.SharedKernel.UserId(x));
        b.Property(x => x.RoleId).HasConversion(x => x.Value, x => new Erp.SharedKernel.RoleId(x));
        b.HasIndex(x => x.RoleId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable("UserRoles", "platform");
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermissionEntity>
{
    public void Configure(EntityTypeBuilder<RolePermissionEntity> b)
    {
        b.HasKey(x => new { x.RoleId, x.PermissionId });
        b.Property(x => x.RoleId).HasConversion(x => x.Value, x => new Erp.SharedKernel.RoleId(x));
        b.Property(x => x.PermissionId).HasConversion(x => x.Value, x => new Erp.SharedKernel.PermissionId(x));
        b.HasIndex(x => x.PermissionId);
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable("RolePermissions", "platform");
    }
}
