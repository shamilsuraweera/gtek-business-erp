using Erp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Erp.Modules.Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
partial class PlatformDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("platform").HasAnnotation("ProductVersion", "10.0.0");
        modelBuilder.Entity("Erp.Modules.Platform.Domain.Company", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Code").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("ModifiedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.HasKey("Id");
            b.HasIndex("Code").IsUnique();
            b.ToTable("Companies", "platform");
        });
    }
}
