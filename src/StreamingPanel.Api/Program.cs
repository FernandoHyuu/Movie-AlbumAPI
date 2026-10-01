using System.Reflection;
using FluentValidation;
using StreamingPanel.Api.Extensions;
using StreamingPanel.Api.Middleware;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Core.Validation;
using StreamingPanel.Infrastructure.Repositories;
using StreamingPanel.Infrastructure.Security;
using StreamingPanel.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Validate the DI graph when the container is built so startup fails fast with a
// clear message naming any unresolved dependency, and catch scoped-from-root misuse.
builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddControllers();

builder.Services.AddAppDbContext(builder.Configuration);

builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddScoped<IPersonRepository, PersonRepository>();
builder.Services.AddScoped<IMovieRepository, MovieRepository>();
builder.Services.AddScoped<IAlbumRepository, AlbumRepository>();

builder.Services.AddScoped<IMovieService, MovieService>();
builder.Services.AddScoped<IAlbumService, AlbumService>();
builder.Services.AddScoped<IPersonService, PersonService>();

builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

builder.Services.AddJwtAuthenticationAndPolicies(builder.Configuration);
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddApiSwagger(Assembly.GetExecutingAssembly());

var app = builder.Build();

// Resolve the key orchestration services up front so a resolution failure produces an
// explicit, named error. ValidateOnBuild already checks the whole graph; this just makes
// the message friendlier for the services the app actually depends on.
ValidateKeyServices(app.Services);

static void ValidateKeyServices(IServiceProvider services)
{
    (Type Service, string Name)[] required =
    [
        (typeof(IAuthService), nameof(IAuthService)),
        (typeof(IPersonService), nameof(IPersonService)),
        (typeof(IMovieService), nameof(IMovieService)),
        (typeof(IAlbumService), nameof(IAlbumService)),
    ];

    using var scope = services.CreateScope();
    foreach (var (service, name) in required)
    {
        try
        {
            _ = scope.ServiceProvider.GetRequiredService(service);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Startup aborted: required dependency '{name}' could not be resolved. {ex.Message}",
                ex);
        }
    }
}

// Migrate with a bounded retry loop so the API tolerates PostgreSQL still warming up;
// aborts with a non-zero exit if the database stays unreachable past the timeout.
await app.MigrateDatabaseWithRetryAsync();

await app.SeedDatabaseAsync();

// Pipeline order matters below.

// ExceptionMiddleware is registered first so it wraps every other middleware and endpoint,
// turning any failure into an RFC 7807 Problem Details response.
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// CORS runs before authentication so preflight and cross-origin requests are handled first.
app.UseCors(CorsSetup.FrontendPolicy);

app.UseHttpsRedirection();

// Authentication before authorization: the identity (and role claim) must be populated
// before policies evaluate it.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
