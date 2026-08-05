using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using BioGuard.Api.Services;

namespace Test1BioGuard.SecurityTests;

public class JwtSecurityTests
{
    private readonly AuthService _service;

    public JwtSecurityTests()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET_KEY", "BioGuard-Test-Secret-Key-Only-For-Unit-Tests-0123456789");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "BioGuard-Test-Secret-Key-Only-For-Unit-Tests-0123456789",
            ["Jwt:Issuer"] = "BioGuardApi",
            ["Jwt:Audience"] = "BioGuardApp",
            ["Jwt:ExpirationMinutes"] = "60"
        }).Build();

        var mockDb = new Moq.Mock<BioGuard.Api.Config.IMongoDbContext>();
        var mockLogger = new Moq.Mock<ILogger<AuthService>>();
        var mockEmailService = new Moq.Mock<IEmailService>();
        _service = new AuthService(mockDb.Object, config, new HttpClient(), mockLogger.Object, mockEmailService.Object);
    }

    [Fact]
    public void GenerateToken_DatosValidos_RetornaTokenNoVacio()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        token.Should().NotBeNullOrEmpty();
        token.Should().Contain(".");
    }

    [Fact]
    public void GenerateToken_PuedeSerDecodificado()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Should().NotBeNull();
        jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "user123");
        jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == "test@test.com");
        jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "dueno");
    }

    [Fact]
    public void GenerateToken_TieneIssuerCorrecto()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Issuer.Should().Be("BioGuardApi");
    }

    [Fact]
    public void GenerateToken_TieneAudienceCorrecto()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Audiences.Should().Contain("BioGuardApp");
    }

    [Fact]
    public void GenerateToken_TieneExpiracion()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.ValidTo.Should().BeAfter(DateTime.UtcNow);
        jwtToken.ValidTo.Should().BeBefore(DateTime.UtcNow.AddMinutes(61));
    }

    [Fact]
    public void GenerateToken_CadaLlamadaEsUnica()
    {
        var token1 = _service.GenerateToken("user123", "test@test.com", "dueno");
        var token2 = _service.GenerateToken("user123", "test@test.com", "dueno");

        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateToken_ContraFirmaHMACSHA256()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.SignatureAlgorithm.Should().Be("HS256");
    }

    [Fact]
    public void GenerateToken_ConClaveSecreta()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "BioGuardApi",
            ValidAudience = "BioGuardApp",
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes("BioGuard-Test-Secret-Key-Only-For-Unit-Tests-0123456789"))
        };

        var principal = handler.ValidateToken(token, validationParams, out _);
        principal.Should().NotBeNull();
        principal!.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void GenerateToken_ConClaveIncorrecta_LanzaExcepcion()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "BioGuardApi",
            ValidAudience = "BioGuardApp",
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes("WrongSecretKey12345678901234567890!"))
        };

        var act = () => handler.ValidateToken(token, validationParams, out _);
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void GenerateToken_TieneJti()
    {
        var token = _service.GenerateToken("user123", "test@test.com", "dueno");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Claims.Should().Contain(c => c.Type == "jti");
    }
}
