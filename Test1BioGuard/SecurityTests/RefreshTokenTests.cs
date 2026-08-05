using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.Services;

namespace Test1BioGuard.SecurityTests;

public class RefreshTokenTests
{
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly AuthService _service;

    public RefreshTokenTests()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET_KEY", "BioGuard-Test-Secret-Key-Only-For-Unit-Tests-0123456789");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "BioGuard-Test-Secret-Key-Only-For-Unit-Tests-0123456789",
                ["Jwt:Issuer"] = "BioGuardApi",
                ["Jwt:Audience"] = "BioGuardApp",
                ["Jwt:ExpirationMinutes"] = "60",
                ["Jwt:RefreshTokenDays"] = "7"
            }).Build();

        _mockDb = new Mock<IMongoDbContext>();
        var mockLogger = new Mock<ILogger<AuthService>>();
        var mockEmailService = new Mock<IEmailService>();
        _service = new AuthService(_mockDb.Object, config, new HttpClient(), mockLogger.Object, mockEmailService.Object);
    }

    [Fact]
    public void GenerateRefreshToken_RetornaTokenNoVacio()
    {
        var token = _service.GenerateRefreshToken();

        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateRefreshToken_EsBase64()
    {
        var token = _service.GenerateRefreshToken();

        var act = () => Convert.FromBase64String(token);
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateRefreshToken_LongitudSuficiente()
    {
        var token = _service.GenerateRefreshToken();

        var bytes = Convert.FromBase64String(token);
        bytes.Length.Should().BeGreaterThanOrEqualTo(64);
    }

    [Fact]
    public void GenerateRefreshToken_CadaLlamadaEsUnica()
    {
        var token1 = _service.GenerateRefreshToken();
        var token2 = _service.GenerateRefreshToken();

        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateRefreshToken_CryptoRandom()
    {
        var tokens = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            tokens.Add(_service.GenerateRefreshToken());
        }

        tokens.Count.Should().Be(100);
    }

    [Fact]
    public void GenerateRefreshToken_LongitudConsistente()
    {
        var lengths = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            var token = _service.GenerateRefreshToken();
            lengths.Add(token.Length);
        }

        lengths.Distinct().Count().Should().Be(1);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // REFRESH TOKEN REUSE & ROTATION TESTS
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public async Task RefreshToken_ReutilizarTokenRevocado_RetornaNull()
    {
        var mockRefreshTokens = new Mock<IMongoCollection<RefreshToken>>();
        _mockDb.Setup(db => db.RefreshTokens).Returns(mockRefreshTokens.Object);

        var revokedToken = new RefreshToken
        {
            Id = "rt1",
            UsuarioId = "user123",
            Token = "old_revoked_token",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<RefreshToken>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>()))
            .ReturnsAsync(revokedToken);

        var mockUpdateResult = new Mock<UpdateResult>();
        mockUpdateResult.Setup(r => r.ModifiedCount).Returns(0);
        mockRefreshTokens.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<RefreshToken>>(),
                It.IsAny<UpdateDefinition<RefreshToken>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        var request = new BioGuard.Api.DTOs.RefreshTokenRequest("old_revoked_token");
        var result = await _service.RefreshTokenAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshToken_ReutilizarTokenRevocado_RevocaCadenaCompleta()
    {
        var mockRefreshTokens = new Mock<IMongoCollection<RefreshToken>>();
        _mockDb.Setup(db => db.RefreshTokens).Returns(mockRefreshTokens.Object);

        var revokedToken = new RefreshToken
        {
            Id = "rt1",
            UsuarioId = "user_chain_test",
            Token = "old_revoked_token_chain",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<RefreshToken>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>()))
            .ReturnsAsync(revokedToken);

        var mockRevokeUpdateResult = new Mock<UpdateResult>();
        mockRevokeUpdateResult.Setup(r => r.ModifiedCount).Returns(0);
        mockRefreshTokens.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<RefreshToken>>(),
                It.IsAny<UpdateDefinition<RefreshToken>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockRevokeUpdateResult.Object);

        var chainRevoked = false;
        mockRefreshTokens.Setup(c => c.UpdateManyAsync(
                It.Is<FilterDefinition<RefreshToken>>(f => f != null),
                It.Is<UpdateDefinition<RefreshToken>>(u => u != null),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Moq.Mock<UpdateResult>().Object)
            .Callback(() => chainRevoked = true);

        var request = new BioGuard.Api.DTOs.RefreshTokenRequest("old_revoked_token_chain");
        var result = await _service.RefreshTokenAsync(request);

        result.Should().BeNull();
        chainRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshToken_TokenExpirado_RetornaNull()
    {
        var mockRefreshTokens = new Mock<IMongoCollection<RefreshToken>>();
        _mockDb.Setup(db => db.RefreshTokens).Returns(mockRefreshTokens.Object);

        var expiredToken = new RefreshToken
        {
            Id = "rt2",
            UsuarioId = "user123",
            Token = "expired_token",
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            RevokedAt = null,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<RefreshToken>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>()))
            .ReturnsAsync(expiredToken);

        var mockExpiredUpdateResult = new Mock<UpdateResult>();
        mockExpiredUpdateResult.Setup(r => r.ModifiedCount).Returns(1);
        mockRefreshTokens.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<RefreshToken>>(),
                It.IsAny<UpdateDefinition<RefreshToken>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockExpiredUpdateResult.Object);

        var request = new BioGuard.Api.DTOs.RefreshTokenRequest("expired_token");
        var result = await _service.RefreshTokenAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshToken_TokenNoExistente_RetornaNull()
    {
        var mockRefreshTokens = new Mock<IMongoCollection<RefreshToken>>();
        _mockDb.Setup(db => db.RefreshTokens).Returns(mockRefreshTokens.Object);

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<RefreshToken>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>()))
            .ReturnsAsync((RefreshToken?)null);

        var mockNonExistentUpdateResult = new Mock<UpdateResult>();
        mockNonExistentUpdateResult.Setup(r => r.ModifiedCount).Returns(0);
        mockRefreshTokens.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<RefreshToken>>(),
                It.IsAny<UpdateDefinition<RefreshToken>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockNonExistentUpdateResult.Object);

        var request = new BioGuard.Api.DTOs.RefreshTokenRequest("nonexistent_token");
        var result = await _service.RefreshTokenAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshToken_RotacionExitosa_RetornaNuevoToken()
    {
        var mockRefreshTokens = new Mock<IMongoCollection<RefreshToken>>();
        _mockDb.Setup(db => db.RefreshTokens).Returns(mockRefreshTokens.Object);

        var validToken = new RefreshToken
        {
            Id = "rt3",
            UsuarioId = "user_rotate",
            Token = "valid_token_for_rotation",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<RefreshToken>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>()))
            .ReturnsAsync(validToken);

        var mockUser = new UsuarioWeb
        {
            Id = "user_rotate",
            Correo = "test@test.com",
            Nombre = "Test",
            ApellidoPaterno = "User",
            Activo = true
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(mockUser);

        var insertedNewToken = false;
        mockRefreshTokens.Setup(c => c.InsertOneAsync(
                It.IsAny<RefreshToken>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => insertedNewToken = true)
            .Returns(Task.CompletedTask);

        var mockUpdateResult = new Mock<UpdateResult>();
        mockUpdateResult.Setup(r => r.ModifiedCount).Returns(1);
        mockRefreshTokens.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<RefreshToken>>(),
                It.IsAny<UpdateDefinition<RefreshToken>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        var request = new BioGuard.Api.DTOs.RefreshTokenRequest("valid_token_for_rotation");
        var result = await _service.RefreshTokenAsync(request);

        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBe("valid_token_for_rotation");
        insertedNewToken.Should().BeTrue();
    }
}
