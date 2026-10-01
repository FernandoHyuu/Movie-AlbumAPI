using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StreamingPanel.Api.Extensions;

namespace StreamingPanel.Tests.Infrastructure;

/// <summary>
/// Smoke tests for the bounded API-to-PostgreSQL connect/retry on startup
/// (R21.4, R21.5).
///
/// Feature: streaming-panel, task 7.9 (infrastructure smoke tests)
/// Validates: Requirements 21.4, 21.5
///
/// <para>
/// <see cref="DatabaseStartupExtensions.MigrateDatabaseWithRetryAsync"/> retries
/// the connection until PostgreSQL is ready or the configured timeout elapses
/// (R21.4); on exhausting the timeout it throws a clear
/// <see cref="InvalidOperationException"/> so the host exits non-zero (R21.5). These
/// tests point the Npgsql DbContext at an unreachable endpoint
/// (<c>Host=localhost;Port=1</c>) with a tiny 1-second timeout so the bounded loop
/// gives up quickly and the test stays fast — no live database is involved.
/// </para>
/// </summary>
public class DatabaseStartupRetryTests
{
    // An unreachable PostgreSQL endpoint: port 1 is not listening. A short Npgsql
    // connect timeout keeps each failed attempt brief.
    private const string UnreachableConnectionString =
        "Host=localhost;Port=1;Database=streamingpanel;Username=app;Password=secret;Timeout=1;Command Timeout=1";

    private static IHost BuildHost(int connectTimeoutSeconds)
    {
        return new HostBuilder()
            .ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = UnreachableConnectionString,
                    [DatabaseStartupExtensions.ConnectTimeoutConfigKey] =
                        connectTimeoutSeconds.ToString(),
                }))
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddAppDbContext(context.Configuration);
            })
            .Build();
    }

    [Fact]
    public async Task MigrateDatabaseWithRetryAsync_UnreachableDatabase_ThrowsAfterTimeout()
    {
        using var host = BuildHost(connectTimeoutSeconds: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.MigrateDatabaseWithRetryAsync());

        // The failure clearly reports the connection could not be established within
        // the bounded timeout (R21.5).
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task MigrateDatabaseWithRetryAsync_RespectsBoundedTimeout()
    {
        using var host = BuildHost(connectTimeoutSeconds: 1);

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.MigrateDatabaseWithRetryAsync());
        stopwatch.Stop();

        // With a 1-second bounded timeout the loop must give up quickly rather than
        // running toward the 60-second default (R21.4). Allow generous headroom for
        // the Npgsql connect attempt and the 2s inter-attempt delay.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"Retry loop took too long: {stopwatch.Elapsed}. It should honor the bounded timeout.");
    }
}
