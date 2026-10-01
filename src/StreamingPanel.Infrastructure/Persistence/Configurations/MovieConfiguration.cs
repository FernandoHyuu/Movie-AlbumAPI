using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="Movie"/>. MainActors maps to a native PostgreSQL
/// <c>text[]</c>, the cover is <c>bytea</c>, and the title is indexed.
/// </summary>
public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("movies");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(m => m.Studio)
            .HasMaxLength(200);

        builder.Property(m => m.ReleaseYear)
            .IsRequired();

        // Npgsql maps List<string> to a native PostgreSQL text[] column.
        builder.Property(m => m.MainActors)
            .HasColumnType("text[]");

        builder.Property(m => m.CoverImageData)
            .HasColumnType("bytea");

        builder.Property(m => m.ContentType)
            .HasMaxLength(100);

        builder.HasIndex(m => m.Title);
    }
}
