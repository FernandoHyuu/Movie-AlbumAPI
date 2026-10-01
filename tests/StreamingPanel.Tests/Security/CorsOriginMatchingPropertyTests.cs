using CsCheck;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamingPanel.Api.Extensions;

namespace StreamingPanel.Tests.Security;

/// <summary>
/// Property-based tests for the single global CORS policy registered by
/// <see cref="CorsSetup.AddFrontendCors"/>.
///
/// Feature: streaming-panel, Property 27: CORS grants access only to the configured origin
///
/// For any request origin, the API includes cross-origin-permitting headers
/// (identifying the origin and allowing credentials) if and only if the origin
/// exactly matches the configured Frontend origin; for any other origin the
/// permitting headers are omitted.
///
/// The test exercises the CORS policy decision directly — it builds the policy
/// via <see cref="CorsSetup.AddFrontendCors"/> on a minimal service collection
/// backed by an in-memory configuration, resolves <see cref="ICorsService"/> and
/// the registered <see cref="CorsPolicy"/>, and evaluates each candidate origin
/// against the policy. No database or live host is involved.
///
/// Validates: Requirements 13.1, 13.2, 13.4
/// </summary>
public class CorsOriginMatchingPropertyTests
{
    private const string ConfiguredOrigin = "http://localhost:4200";

    /// <summary>
    /// Builds the <see cref="ICorsService"/> and the configured
    /// <see cref="CorsPolicy"/> exactly as the API composition root would, using
    /// the supplied Frontend origin.
    /// </summary>
    private static (ICorsService Service, CorsPolicy Policy) BuildCors(string configuredOrigin)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CorsSetup.FrontendOriginConfigKey] = configuredOrigin,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFrontendCors(configuration);

        var provider = services.BuildServiceProvider();

        var corsService = provider.GetRequiredService<ICorsService>();
        var policyProvider = provider.GetRequiredService<ICorsPolicyProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var policy = policyProvider
            .GetPolicyAsync(httpContext, CorsSetup.FrontendPolicy)
            .GetAwaiter()
            .GetResult();

        Assert.NotNull(policy);
        return (corsService, policy!);
    }

    /// <summary>
    /// Evaluates a candidate origin against the configured policy and returns the
    /// resulting <see cref="CorsResult"/> as the browser-visible CORS decision.
    /// </summary>
    private static CorsResult Evaluate(ICorsService service, CorsPolicy policy, string origin)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Headers.Origin = origin;

        return service.EvaluatePolicy(httpContext, policy);
    }

    /// <summary>
    /// The configured origin is granted: the CORS decision names that exact origin
    /// as the allowed origin and permits credentials.
    ///
    /// Feature: streaming-panel, Property 27: CORS grants access only to the configured origin
    /// Validates: Requirements 13.1, 13.2
    /// </summary>
    [Fact]
    public void ConfiguredOrigin_IsGrantedWithCredentials()
    {
        var (service, policy) = BuildCors(ConfiguredOrigin);

        var result = Evaluate(service, policy, ConfiguredOrigin);

        // IsOriginAllowed is the authoritative decision: only when it is true does
        // the CORS layer emit the Access-Control-Allow-Origin header (naming
        // AllowedOrigin) and the Access-Control-Allow-Credentials header.
        Assert.True(result.IsOriginAllowed);
        Assert.Equal(ConfiguredOrigin, result.AllowedOrigin);
        Assert.True(result.SupportsCredentials);
    }

    /// <summary>
    /// Feature: streaming-panel, Property 27: CORS grants access only to the configured origin
    ///
    /// For any generated candidate origin (varying scheme, host, and port), the CORS
    /// policy names it as the allowed origin if and only if it is byte-for-byte equal
    /// to the configured Frontend origin; any differing origin receives no
    /// origin-permitting header (R13.1, R13.4). When the configured origin is granted,
    /// credentials are permitted (R13.2).
    ///
    /// Validates: Requirements 13.1, 13.2, 13.4
    /// </summary>
    [Fact]
    public void OriginIsAllowed_IffExactlyEqualToConfiguredOrigin()
    {
        var (service, policy) = BuildCors(ConfiguredOrigin);

        // Generate origins across the input space: varying scheme, host, and port,
        // plus the configured origin itself so the "allowed" branch is exercised.
        var genScheme = Gen.OneOfConst("http", "https");
        var genHost = Gen.OneOfConst("localhost", "127.0.0.1", "example.com", "app.local", "localhost.evil");
        var genPort = Gen.OneOfConst("", ":80", ":443", ":4200", ":5000", ":8080");

        var genCandidate =
            from scheme in genScheme
            from host in genHost
            from port in genPort
            select $"{scheme}://{host}{port}";

        // Mix in the configured origin frequently so the IFF holds in both directions.
        var genOrigin = Gen.Frequency(
            (1, Gen.Const(ConfiguredOrigin)),
            (3, genCandidate));

        genOrigin.Sample(origin =>
        {
            var result = Evaluate(service, policy, origin);

            var isConfigured = string.Equals(origin, ConfiguredOrigin, StringComparison.Ordinal);

            // IsOriginAllowed is the authoritative gate: the CORS layer emits the
            // Access-Control-Allow-Origin / Access-Control-Allow-Credentials headers
            // only when it is true. It must be true iff the origin exactly matches.
            Assert.Equal(isConfigured, result.IsOriginAllowed);

            if (isConfigured)
            {
                // Granted: the exact origin is named and credentials are allowed
                // (R13.1, R13.2).
                Assert.Equal(ConfiguredOrigin, result.AllowedOrigin);
                Assert.True(result.SupportsCredentials);
            }
            else
            {
                // Not granted: because IsOriginAllowed is false, no origin-permitting
                // header is emitted, so the browser blocks the response (R13.4).
                Assert.False(result.IsOriginAllowed,
                    $"Origin '{origin}' must not be granted, but IsOriginAllowed was true.");
            }
        }, iter: 100);
    }
}
