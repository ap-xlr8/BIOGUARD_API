using System.Text.RegularExpressions;
using FluentAssertions;

namespace Test1BioGuard.NonFunctionalTests;

public class IaCTests
{
    private static readonly string RepoRoot;
    private static readonly string DockerfilePath;
    private static readonly string AppYamlPath;
    private static readonly string AppStagingYamlPath;

    static IaCTests()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null &&
               !Directory.Exists(Path.Combine(dir, ".git")) &&
               !File.Exists(Path.Combine(dir, "BioGuard.Api.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        RepoRoot = dir ?? throw new InvalidOperationException("Cannot find repository root");
        DockerfilePath = Path.Combine(RepoRoot, "BioGuard.Api", "Dockerfile");
        AppYamlPath = Path.Combine(RepoRoot, ".do", "app.yaml");
        AppStagingYamlPath = Path.Combine(RepoRoot, ".do", "app.staging.yaml");
    }

    [Fact]
    public void Dockerfile_NoCorreComoRoot()
    {
        var content = File.ReadAllText(DockerfilePath);
        content.Should().Contain("USER $APP_UID",
            "el contenedor no debe ejecutarse como root");
    }

    [Fact]
    public void Dockerfile_ExponeSoloPuerto8080()
    {
        var content = File.ReadAllText(DockerfilePath);
        var exposeLines = Regex.Matches(content, @"EXPOSE\s+(\d+)");
        exposeLines.Should().HaveCount(1);
        exposeLines[0].Groups[1].Value.Should().Be("8080");
    }

    [Fact]
    public void Dockerfile_NoExponePuertosInnecesarios()
    {
        var content = File.ReadAllText(DockerfilePath);
        var ports = Regex.Matches(content, @"EXPOSE\s+(\d+)")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        ports.Should().OnlyContain(p => p == 8080 || p == 80,
            "solo los puertos 8080 y 80 estan permitidos");
    }

    [Fact]
    public void Dockerfile_UsaMultiStageBuild()
    {
        var content = File.ReadAllText(DockerfilePath);
        var stages = Regex.Matches(content, @"AS\s+\w+");
        stages.Should().HaveCount(c => c >= 3,
            "debe tener al menos 3 etapas: base, build/publish, final");
    }

    [Fact]
    public void Dockerfile_NoContieneSecretosEnClaro()
    {
        var content = File.ReadAllText(DockerfilePath);
        content.Should().NotContain("MONGODB_CONNECTION_STRING");
        content.Should().NotContain("JWT_SECRET_KEY");
        content.Should().NotContain("GOOGLE_CLIENT_ID");
    }

    [Fact]
    public void Dockerfile_AspNetImageVersionEspecifica()
    {
        var content = File.ReadAllText(DockerfilePath);
        content.Should().Contain("dotnet/aspnet:10.0",
            "la imagen base debe tener un tag de version especifico, no 'latest'");
        content.Should().NotContain("dotnet/aspnet:latest");
    }

    [Fact]
    public void AppYaml_NoExponePuertosAdicionales()
    {
        var content = File.ReadAllText(AppYamlPath);
        content.Should().Contain("http_port: 8080");
    }

    [Fact]
    public void AppYaml_TieneHealthCheckConfigurado()
    {
        var content = File.ReadAllText(AppYamlPath);
        content.Should().Contain("health_check:");
        content.Should().Contain("/health");
    }

    [Fact]
    public void AppYaml_SecretosUsanVariablesDeEntorno()
    {
        var content = File.ReadAllText(AppYamlPath);
        content.Should().Contain("${MONGODB_CONNECTION_STRING}");
        content.Should().Contain("${JWT_SECRET_KEY}");
    }

    [Fact]
    public void AppYaml_InstanceSizeNoMayorABasicXxs()
    {
        var content = File.ReadAllText(AppYamlPath);
        content.Should().Contain("instance_size_slug: basic-xxs");
    }

    [Fact]
    public void Dockerignore_ExcluyeArchivosInnecesarios()
    {
        var path = Path.Combine(RepoRoot, ".dockerignore");
        var content = File.ReadAllText(path);
        content.Should().Contain("**/.git");
        content.Should().Contain("**/bin");
        content.Should().Contain("**/obj");
        content.Should().NotContain("BioGuard.Api/Dockerfile",
            "el Dockerfile no debe estar en .dockerignore");
    }

    [Fact]
    public void AppStagingYaml_UsaVariablesStaging()
    {
        var content = File.ReadAllText(AppStagingYamlPath);
        content.Should().Contain("STAGING_MONGODB_CONNECTION_STRING");
        content.Should().Contain("STAGING_JWT_SECRET_KEY");
        content.Should().Contain("STAGING_STRIPE_SECRET_KEY");
        content.Should().Contain("STAGING_PAYPAL_CLIENT_ID");
        content.Should().Contain("STAGING_FIREBASE_SERVICE_ACCOUNT_JSON");
    }

    [Fact]
    public void AppStagingYaml_TieneAspnetEnvironmentStaging()
    {
        var content = File.ReadAllText(AppStagingYamlPath);
        content.Should().Contain("ASPNETCORE_ENVIRONMENT");
        content.Should().Contain("Staging");
    }

    [Fact]
    public void AppProductionYaml_UsaVariablesProduccion()
    {
        var content = File.ReadAllText(AppYamlPath);
        content.Should().Contain("ASPNETCORE_ENVIRONMENT");
        content.Should().Contain("Production");
        content.Should().Contain("MONGODB_CONNECTION_STRING");
        content.Should().Contain("JWT_SECRET_KEY");
        content.Should().Contain("STRIPE_SECRET_KEY");
        content.Should().Contain("PAYPAL_CLIENT_ID");
        content.Should().Contain("FIREBASE_SERVICE_ACCOUNT_JSON");
    }

    [Fact]
    public void AppProductionYaml_NoContieneVariablesStaging()
    {
        var content = File.ReadAllText(AppYamlPath);
        content.Should().NotContain("STAGING_");
    }

    [Fact]
    public void AppStagingYaml_UsaSoloVariablesStaging()
    {
        var content = File.ReadAllText(AppStagingYamlPath);
        content.Should().Contain("STAGING_MONGODB_CONNECTION_STRING");
        content.Should().NotContain("value: ${MONGODB_CONNECTION_STRING}");
        content.Should().NotContain("value: ${JWT_SECRET_KEY}");
    }

    [Fact]
    public void PipelineTieneStagingDeploy()
    {
        var path = Path.Combine(RepoRoot, ".github", "workflows", "ci.yml");
        var content = File.ReadAllText(path);
        content.Should().Contain("deploy-staging");
        content.Should().Contain("Promote to Production");
    }

    [Fact]
    public void PipelineTieneSmokeTestPostDeploy()
    {
        var path = Path.Combine(RepoRoot, ".github", "workflows", "ci.yml");
        var content = File.ReadAllText(path);
        content.Should().Contain("Post-Deployment Smoke Test");
    }

    [Fact]
    public void PipelineTieneAmbienteStaging()
    {
        var path = Path.Combine(RepoRoot, ".github", "workflows", "ci.yml");
        var content = File.ReadAllText(path);
        content.Should().Contain("environment: staging");
        content.Should().Contain("environment:");
        content.Should().Contain("name: production");
    }
}
