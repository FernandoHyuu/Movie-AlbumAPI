using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamingPanel.Api.Extensions;

namespace StreamingPanel.Tests.Infrastructure;

/// <summary>
/// Smoke tests for the single global CORS policy registration (R13.5).
///
/// Feature: streaming-panel, task 7.9 (infrastructure smoke tests)
/// Validates: Requirements 13.5
///
/// <para>
/// <see cref="CorsSetup.AddFrontendCors"/> must fail fast at startup when the
/// configured Frontend origin (<c>Cors:FrontendOrigin</c>) is absent or empty, so
/// the API never starts with an unusable CORS policy. These tests invoke the real
/// registration directly — no host is needed.
/// </para>
/// </summary>
public class CorsStartupTests
{
    private static IConfiguration ConfigWith(string? frontendOrigin)
    {
        var values = new Dictionary<string, string?>();
        if (frontendOrigin is not null)
        {
            values[CorsSetup.FrontendOriginConfigKey] = frontendOrigin;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void AddFrontendCors_MissingOrigin_ThrowsClearError()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddFrontendCors(ConfigWith(null)));

        Assert.Contains(CorsSetup.FrontendOriginConfigKey, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddFrontendCors_EmptyOrigin_ThrowsClearError(string origin)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddFrontendCors(ConfigWith(origin)));

        Assert.Contains(CorsSetup.FrontendOriginConfigKey, ex.Message);
    }

    [Fact]
    public void AddFrontendCors_ValidOrigin_RegistersCorsServices()
    {
        var services = new ServiceCollection();

        services.AddFrontendCors(ConfigWith("http://localhost:4200"));

        // The CORS infrastructure is registered when the origin is present.
        using var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetService<Microsoft.Extensions.Options.IOptions<
            Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>();
        Assert.NotNull(corsOptions);
        Assert.NotNull(corsOptions!.Value.GetPolicy(CorsSetup.FrontendPolicy));
    }
}
