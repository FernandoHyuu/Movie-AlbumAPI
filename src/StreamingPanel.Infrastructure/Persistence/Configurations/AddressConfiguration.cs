using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for the <see cref="Address"/> child. The per-person cardinality limit lives
/// in the service/validator layer, not the schema.
/// </summary>
public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("addresses");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.PersonId)
            .IsRequired();

        builder.Property(a => a.Street)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.State)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.ZipCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(a => a.PersonId);
    }
}
