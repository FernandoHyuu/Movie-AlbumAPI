using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="RefreshToken"/>: unique index on the token hash, a cascading
/// FK to Person, and a composite <c>(PersonId, RevokedAt)</c> index for active-token lookups.
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.PersonId)
            .IsRequired();

        builder.Property(rt => rt.TokenHash)
            .IsRequired()
            .HasMaxLength(256);

        // Unique index on the stored hash so a token value maps to one record.
        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique();

        builder.Property(rt => rt.ExpiresAt)
            .IsRequired();

        builder.Property(rt => rt.CreatedAt)
            .IsRequired();

        builder.Property(rt => rt.RevokedAt);

        builder.Property(rt => rt.ReplacedByTokenHash)
            .HasMaxLength(256);

        // Composite index supporting active-token lookups by person.
        builder.HasIndex(rt => new { rt.PersonId, rt.RevokedAt });

        // IsActive / IsWithinGraceWindow are computed helpers, not columns.
        builder.Ignore(rt => rt.IsActive);

        builder.HasOne(rt => rt.Person!)
            .WithMany(p => p.RefreshTokens)
            .HasForeignKey(rt => rt.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
