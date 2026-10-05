using Microsoft.EntityFrameworkCore;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();
    public DbSet<UserCompanyAccess> UserCompanyAccesses => Set<UserCompanyAccess>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly)
            .HasDefaultSchema("platform");
}
