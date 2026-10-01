using System.Text.RegularExpressions;

namespace StreamingPanel.Tests.Infrastructure;

/// <summary>
/// Smoke tests over the docker-compose environment definition
/// (R21.1–R21.3, R21.6, R21.7).
///
/// Feature: streaming-panel, task 7.9 (infrastructure smoke tests)
/// Validates: Requirements 21.1, 21.2, 21.3, 21.6, 21.7
///
/// <para>
/// The repository-root <c>docker-compose.yml</c> is located by walking up from the
/// test assembly's base directory until the file is found, then asserted on as text:
/// it must define the three services (api, postgres:16, pgadmin4), expose
/// configurable host ports for the api and pgadmin, and declare a named <c>pgdata</c>
/// volume for PostgreSQL persistence. Text/regex assertions avoid adding a YAML
/// dependency while still verifying the required structure.
/// </para>
/// </summary>
public class DockerComposeConfigTests
{
    private static string LoadComposeFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docker-compose.yml");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate docker-compose.yml by walking up from the test base directory.");
    }

    [Fact]
    public void Compose_DefinesApiService()
    {
        var yaml = LoadComposeFile();
        // The api service builds from the local Dockerfile (R21.1).
        Assert.Matches(new Regex(@"^\s{2}api:\s*$", RegexOptions.Multiline), yaml);
        Assert.Contains("dockerfile: Dockerfile", yaml);
    }

    [Fact]
    public void Compose_DefinesPostgres16Service()
    {
        var yaml = LoadComposeFile();
        Assert.Matches(new Regex(@"^\s{2}postgres:\s*$", RegexOptions.Multiline), yaml);
        Assert.Contains("image: postgres:16", yaml);
    }

    [Fact]
    public void Compose_DefinesPgAdminService()
    {
        var yaml = LoadComposeFile();
        Assert.Matches(new Regex(@"^\s{2}pgadmin:\s*$", RegexOptions.Multiline), yaml);
        Assert.Contains("image: dpage/pgadmin4", yaml);
    }

    [Fact]
    public void Compose_ExposesConfigurableApiHostPort()
    {
        var yaml = LoadComposeFile();
        // The host port is driven by an environment variable so it is configurable (R21.6);
        // the container always listens on 8080.
        Assert.Matches(new Regex(@"\$\{API_HOST_PORT[^}]*\}:8080"), yaml);
    }

    [Fact]
    public void Compose_ExposesConfigurablePgAdminHostPort()
    {
        var yaml = LoadComposeFile();
        Assert.Matches(new Regex(@"\$\{PGADMIN_HOST_PORT[^}]*\}:80"), yaml);
    }

    [Fact]
    public void Compose_DeclaresNamedPgDataVolume()
    {
        var yaml = LoadComposeFile();
        // Postgres data is mounted into the named pgdata volume (R21.7)...
        Assert.Contains("pgdata:/var/lib/postgresql/data", yaml);

        // ...and pgdata is declared as a top-level named volume so it persists across restarts.
        Assert.Matches(new Regex(@"^volumes:\s*$", RegexOptions.Multiline), yaml);
        Assert.Matches(new Regex(@"^\s{2}pgdata:\s*$", RegexOptions.Multiline), yaml);
    }
}
