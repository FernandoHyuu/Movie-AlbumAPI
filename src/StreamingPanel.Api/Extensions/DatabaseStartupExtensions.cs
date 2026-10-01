using Microsoft.EntityFrameworkCore;
using Npgsql;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Persistence.Seed;

namespace StreamingPanel.Api.Extensions;

/// <summary>
/// Startup helpers that register <see cref="AppDbContext"/> against PostgreSQL and apply
/// migrations with a bounded connect/retry loop so the API tolerates the database still
/// warming up under docker-compose. If it stays unreachable past the configured timeout, the
/// API aborts with a clear error so the container exits non-zero.
/// </summary>
public static class DatabaseStartupExtensions
{
    /// <summary>Configuration key holding the primary connection string.</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Configuration key for the bounded startup connect timeout, in seconds.
    /// The docker-compose environment supplies this as <c>Database__ConnectTimeoutSeconds</c>.
    /// </summary>
    public const string ConnectTimeoutConfigKey = "Database:ConnectTimeoutSeconds";

    /// <summary>Default bounded connect timeout when none is configured.</summary>
    public const int DefaultConnectTimeoutSeconds = 60;

    /// <summary>Delay between successive connection attempts.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Registers <see cref="AppDbContext"/> with the Npgsql provider using the
    /// <c>ConnectionStrings:Default</c> connection string.
    /// </summary>
    public static IServiceCollection AddAppDbContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Missing required connection string 'ConnectionStrings:{ConnectionStringName}'.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }

    /// <summary>
    /// Applies EF Core migrations on startup, retrying the connection until PostgreSQL is ready
    /// or the configured timeout elapses. On timeout it throws so the host exits non-zero.
    /// </summary>
    public static async Task MigrateDatabaseWithRetryAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DatabaseStartup");

        var timeoutSeconds = configuration.GetValue(ConnectTimeoutConfigKey, DefaultConnectTimeoutSeconds);
        if (timeoutSeconds <= 0)
        {
            timeoutSeconds = DefaultConnectTimeoutSeconds;
        }

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var attempt = 0;
        Exception? lastError = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                using var scope = host.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.MigrateAsync(cancellationToken);

                logger.LogInformation(
                    "Database connected and migrations applied on attempt {Attempt}.", attempt);
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
            {
                lastError = ex;

                if (DateTimeOffset.UtcNow >= deadline)
                {
                    break;
                }

                logger.LogWarning(
                    "Database not ready on attempt {Attempt} ({Error}); retrying in {Delay}s (timeout {Timeout}s).",
                    attempt, ex.Message, RetryDelay.TotalSeconds, timeout.TotalSeconds);

                try
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not establish a database connection within the configured timeout of " +
            $"{timeout.TotalSeconds:0} seconds after {attempt} attempt(s). " +
            "Verify the database host, port, name, and credentials.",
            lastError);
    }

    /// <summary>
    /// Runs the idempotent database seed after migrations, populating the default Admin person
    /// and sample Movies/Albums. Repeated runs insert no duplicates; a failure rolls the seed
    /// transaction back and aborts startup.
    /// </summary>
    public static async Task SeedDatabaseAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DatabaseSeed");

        await DatabaseSeeder.SeedAsync(dbContext, passwordHasher, logger, cancellationToken);
    }
}
