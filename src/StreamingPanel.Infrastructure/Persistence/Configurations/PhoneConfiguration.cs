using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for the <see cref="Phone"/> child. The per-person cardinality limit lives
/// in the service/validator layer, not the schema.
/// </summary>
public sealed class PhoneConfiguration : IEntityTypeConfiguration<Phone>
{
    public void Configure(EntityTypeBuilder<Phone> builder)
    {
        builder.ToTable("phones");

        builder.HasKey(ph => ph.Id);

        builder.Property(ph => ph.PersonId)
            .IsRequired();

        builder.Property(ph => ph.Number)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(ph => ph.Type)
            .IsRequired()
            .HasMaxLength(40);

        builder.HasIndex(ph => ph.PersonId);
    }
}
