using Erp.Modules.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Modules.Platform.Infrastructure.Persistence.Configurations;

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.HasKey(company => company.Id);
        builder.Property(company => company.Id)
            .HasConversion(id => id.Value, value => new Erp.SharedKernel.CompanyId(value));
        builder.Property(company => company.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(company => company.Code).IsUnique();
        builder.Property(company => company.Name).HasMaxLength(200).IsRequired();
        builder.Property(company => company.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(company => company.CreatedAt).IsRequired();
        builder.Property(company => company.ModifiedAt);
    }

}

public sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasConversion(x => x.Value, x => new Erp.SharedKernel.NumberSequenceId(x));
            builder.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new Erp.SharedKernel.CompanyId(x)).IsRequired();
            builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Prefix).HasMaxLength(50).IsRequired();
            builder.Property(x => x.Suffix).HasMaxLength(50);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.CreatedBy).HasConversion(
                x => x.HasValue ? x.Value.Value : (Guid?)null,
                x => x.HasValue ? new Erp.SharedKernel.UserId(x.Value) : null);
            builder.Property(x => x.ModifiedBy).HasConversion(
                x => x.HasValue ? x.Value.Value : (Guid?)null,
                x => x.HasValue ? new Erp.SharedKernel.UserId(x.Value) : null);
            builder.Property<uint>("xmin").IsRowVersion().IsConcurrencyToken();
            builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.HasIndex(x => x.CompanyId);
            builder.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.ToTable("NumberSequences", "platform");
    }
}
