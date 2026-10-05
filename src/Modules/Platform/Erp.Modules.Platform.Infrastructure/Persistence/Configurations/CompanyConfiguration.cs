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
