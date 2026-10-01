namespace StreamingPanel.Api.Extensions;

/// <summary>
/// Registers the single named CORS policy that lets the configured Angular frontend call the
/// API from the browser. It permits exactly one origin (<c>Cors:FrontendOrigin</c>, matched on
/// scheme, host, and port) with credentials, any header, and any method. Any other origin gets
/// no CORS headers and is blocked by the browser. Registration throws when the origin is absent
/// or empty so startup fails fast.
/// </summary>
public static class CorsSetup
{
    /// <summary>The name of the single global CORS policy applied via <c>UseCors</c>.</summary>
    public const string FrontendPolicy = "FrontendOrigin";

    /// <summary>The configuration key holding the exact-match Frontend origin.</summary>
    public const string FrontendOriginConfigKey = "Cors:FrontendOrigin";

    /// <summary>
    /// Registers the <see cref="FrontendPolicy"/> CORS policy for the configured frontend origin.
    /// Throws <see cref="InvalidOperationException"/> if <c>Cors:FrontendOrigin</c> is absent or empty.
    /// </summary>
    public static IServiceCollection AddFrontendCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var frontendOrigin = configuration[FrontendOriginConfigKey];
        if (string.IsNullOrWhiteSpace(frontendOrigin))
        {
            throw new InvalidOperationException(
                $"Missing required Cross-Origin configuration value '{FrontendOriginConfigKey}'. " +
                "Set the exact Frontend origin (scheme, host, and port, e.g. 'http://localhost:4200') " +
                "via configuration or the 'Cors__FrontendOrigin' environment variable.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(FrontendPolicy, policy =>
                policy
                    .WithOrigins(frontendOrigin)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
        });

        return services;
    }
}
