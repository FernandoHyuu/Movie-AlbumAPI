using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;

namespace StreamingPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for the <see cref="Person"/> aggregate root, its <see cref="Address"/> and
/// <see cref="Phone"/> children, and the unique email index.
/// </summary>
public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("persons");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Email)
            .IsRequired()
            .HasMaxLength(254);

        // Unique email so duplicate registrations surface as a conflict.
        builder.HasIndex(p => p.Email)
            .IsUnique();

        builder.Property(p => p.PasswordHash)
            .IsRequired();

        // Store the role as its name rather than an integer, so the column stays readable.
        builder.Property(p => p.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // Cascade delete so removing a Person takes its addresses and phones with it.
        builder.HasMany(p => p.Addresses)
            .WithOne(a => a.Person!)
            .HasForeignKey(a => a.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Phones)
            .WithOne(ph => ph.Person!)
            .HasForeignKey(ph => ph.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // The Person <-> RefreshToken relationship is configured in
        // RefreshTokenConfiguration (unique hash index, composite index, cascade).
    }
}
