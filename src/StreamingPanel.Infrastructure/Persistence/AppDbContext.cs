using Microsoft.EntityFrameworkCore;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Infrastructure.Persistence;

/// <summary>
/// EF Core context mapping the domain entities to PostgreSQL. Mappings live in per-entity
/// <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/> classes.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Person> Persons => Set<Person>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Phone> Phones => Set<Phone>();
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply every IEntityTypeConfiguration<T> in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
