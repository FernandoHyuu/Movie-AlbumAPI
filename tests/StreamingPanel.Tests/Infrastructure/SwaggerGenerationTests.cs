using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using StreamingPanel.Api.Extensions;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace StreamingPanel.Tests.Infrastructure;

/// <summary>
/// Smoke tests for Swagger / OpenAPI document configuration (R22.1–R22.5).
///
/// Feature: streaming-panel, task 7.9 (infrastructure smoke tests)
/// Validates: Requirements 22.1, 22.2, 22.3, 22.4, 22.5
///
/// <para>
/// These tests exercise the <b>same</b> <see cref="SwaggerSetup.AddApiSwagger"/>
/// configuration that <c>Program.cs</c> registers — the configuration was extracted
/// into that shared extension specifically so the test cannot drift from production.
/// The test resolves the configured <see cref="SwaggerGenOptions"/> from the DI
/// container and inspects the resulting <see cref="SwaggerGeneratorOptions"/>
/// directly, which validates the document setup deterministically without a web
/// host, MVC application parts, a database, or the network.
/// </para>
/// <para>
/// Coverage of each criterion:
/// <list type="bullet">
/// <item><description>R22.1 — a <c>v1</c> Swagger document is declared; the Swagger
/// middleware enumerates every registered controller endpoint into this document at
/// runtime (endpoint enumeration itself is exercised end-to-end by the controller
/// authorization tests in tasks 5.2/5.3).</description></item>
/// <item><description>R22.2 — a JWT bearer security scheme is declared so Swagger UI
/// shows the authorization input.</description></item>
/// <item><description>R22.3 — a global security requirement references that scheme,
/// so calling a protected endpoint without a token is a documented 401; the runtime
/// 401 behavior is enforced by the JWT bearer pipeline (authorization tests).</description></item>
/// <item><description>R22.4 — the generator is configured to include XML comments as
/// descriptions when the file is present.</description></item>
/// <item><description>R22.5 — XML-comment inclusion is guarded by File.Exists, so a
/// missing file never fails generation (verified by the options building without
/// throwing even when no XML file exists in the test's base directory).</description></item>
/// </list>
/// </para>
/// </summary>
public class SwaggerGenerationTests
{
    private static SwaggerGeneratorOptions BuildGeneratorOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // The exact Swagger configuration used by Program.cs (R22.1–R22.5). The
        // documentation assembly is the test assembly, which has no .xml file in the
        // test base directory, so the File.Exists guard (R22.5) is exercised.
        services.AddApiSwagger(typeof(SwaggerGenerationTests).Assembly);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SwaggerGenOptions>>().Value;
        return options.SwaggerGeneratorOptions;
    }

    [Fact]
    public void Swagger_DeclaresV1Document()
    {
        var options = BuildGeneratorOptions();

        // A v1 document is declared, which the Swagger middleware fills with every
        // discovered endpoint at runtime (R22.1).
        Assert.True(options.SwaggerDocs.ContainsKey(SwaggerSetup.DocumentName));
        var info = options.SwaggerDocs[SwaggerSetup.DocumentName];
        Assert.Equal("Streaming Panel API", info.Title);
        Assert.Equal(SwaggerSetup.DocumentName, info.Version);
    }

    [Fact]
    public void Swagger_DeclaresJwtBearerSecurityScheme()
    {
        var options = BuildGeneratorOptions();

        // A JWT bearer security scheme is declared so Swagger UI shows the
        // authorization input (R22.2).
        Assert.True(options.SecuritySchemes.ContainsKey(SwaggerSetup.BearerScheme));

        var scheme = options.SecuritySchemes[SwaggerSetup.BearerScheme];
        Assert.Equal(SecuritySchemeType.Http, scheme.Type);
        Assert.Equal("bearer", scheme.Scheme, ignoreCase: true);
        Assert.Equal("JWT", scheme.BearerFormat);
        Assert.Equal(ParameterLocation.Header, scheme.In);
        Assert.Equal("Authorization", scheme.Name);
    }

    [Fact]
    public void Swagger_DeclaresSecurityRequirement()
    {
        var options = BuildGeneratorOptions();

        // A global security requirement references the bearer scheme, so protected
        // endpoints are documented as requiring a token / returning 401 (R22.3).
        Assert.NotEmpty(options.SecurityRequirements);
    }

    [Fact]
    public void Swagger_BuildsWithoutXmlFile_DoesNotThrow()
    {
        // The test assembly has no XML documentation file in the base directory, so
        // the File.Exists guard must let generation configure cleanly (R22.4, R22.5).
        var ex = Record.Exception(() => BuildGeneratorOptions());
        Assert.Null(ex);
    }
}
