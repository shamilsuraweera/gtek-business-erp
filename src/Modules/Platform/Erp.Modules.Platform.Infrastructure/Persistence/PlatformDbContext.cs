using Microsoft.EntityFrameworkCore;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly)
            .HasDefaultSchema("platform");
}
