using System.Reflection;
using Microsoft.OpenApi.Models;

namespace StreamingPanel.Api.Extensions;

/// <summary>
/// Registers Swagger / OpenAPI document generation. The document declares a JWT bearer
/// security scheme so Swagger UI can attach a token to try-it-out requests, and folds in the
/// XML documentation comments when the XML file is present.
/// </summary>
public static class SwaggerSetup
{
    /// <summary>The name of the JWT bearer security scheme/definition.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>The Swagger document name/version.</summary>
    public const string DocumentName = "v1";

    /// <summary>
    /// Registers API explorer and Swagger generation with the JWT bearer scheme and
    /// optional XML documentation comments. Shared by <c>Program.cs</c> and the
    /// infrastructure smoke tests so both exercise the same configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="documentationAssembly">
    /// The assembly whose XML documentation file should be included when present.
    /// Defaults to the entry/executing assembly.
    /// </param>
    public static IServiceCollection AddApiSwagger(
        this IServiceCollection services,
        Assembly? documentationAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "Streaming Panel API",
                Version = DocumentName,
                Description = "Movie and Album streaming administration API with JWT-based authentication and role-based authorization."
            });

            // A token entered in the "Authorize" dialog is attached as
            // "Authorization: Bearer <token>" to subsequent try-it-out requests.
            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Enter the JWT access token (without the \"Bearer \" prefix).",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = BearerScheme
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Include XML doc comments as descriptions, guarded by File.Exists so a missing
            // XML file degrades gracefully instead of failing document generation.
            var assembly = documentationAssembly ?? Assembly.GetExecutingAssembly();
            var xmlFile = $"{assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        });

        return services;
    }
}
