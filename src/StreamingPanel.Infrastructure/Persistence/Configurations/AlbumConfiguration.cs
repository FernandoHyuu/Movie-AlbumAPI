using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="Album"/>. The cover is stored in-row as <c>bytea</c> and
/// the title is indexed.
/// </summary>
public sealed class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("albums");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Band)
            .HasMaxLength(200);

        builder.Property(a => a.ReleaseYear)
            .IsRequired();

        builder.Property(a => a.Genre)
            .HasMaxLength(200);

        builder.Property(a => a.CoverImageData)
            .HasColumnType("bytea");

        builder.Property(a => a.ContentType)
            .HasMaxLength(100);

        builder.HasIndex(a => a.Title);
    }
}
