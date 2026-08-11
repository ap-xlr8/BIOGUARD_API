using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using BioGuard.Api.Config;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;
using FluentAssertions;
using MongoDB.Driver;

namespace Test1BioGuard.UnitTest;

public class AuthServiceTests
{
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly AuthService _service;
    private readonly Mock<IMongoCollection<UsuarioWeb>> _mockUsuarios;
    private readonly Mock<IMongoCollection<Plan>> _mockPlanes;
    private readonly Mock<IMongoCollection<Paciente>> _mockPacientes;
    private readonly Mock<IMongoCollection<Cuidador>> _mockCuidadores;

    private readonly Mock<IMongoCollection<RefreshToken>> _mockRefreshTokens;

    public AuthServiceTests()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET_KEY", "BioGuard-Test-Secret-Key-Only-For-Unit-Tests-0123456789");

        _mockDb = new Mock<IMongoDbContext>();
        _mockUsuarios = new Mock<IMongoCollection<UsuarioWeb>>();
        _mockPlanes = new Mock<IMongoCollection<Plan>>();
        _mockPacientes = new Mock<IMongoCollection<Paciente>>();
        _mockCuidadores = new Mock<IMongoCollection<Cuidador>>();
        _mockRefreshTokens = new Mock<IMongoCollection<RefreshToken>>();

        _mockDb.Setup(db => db.UsuariosWeb).Returns(_mockUsuarios.Object);
        _mockDb.Setup(db => db.Planes).Returns(_mockPlanes.Object);
        _mockDb.Setup(db => db.Pacientes).Returns(_mockPacientes.Object);
        _mockDb.Setup(db => db.Cuidadores).Returns(_mockCuidadores.Object);
        _mockDb.Setup(db => db.RefreshTokens).Returns(_mockRefreshTokens.Object);

        _mockRefreshTokens.Setup(c => c.InsertOneAsync(
            It.IsAny<RefreshToken>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "BioGuard-Test-Secret-Key-Only-For-Unit-Tests-0123456789",
            ["Jwt:Issuer"] = "BioGuardApi",
            ["Jwt:Audience"] = "BioGuardApp",
            ["Jwt:ExpirationMinutes"] = "1440"
        }).Build();

        var mockLogger = new Mock<ILogger<AuthService>>();
        var mockEmailService = new Mock<IEmailService>();
        _service = new AuthService(_mockDb.Object, config, new HttpClient(), mockLogger.Object, mockEmailService.Object);
    }

    [Fact]
    public async Task RegisterWebAsync_DatosValidos_RetornaAuthResponse()
    {
        var plan = new Plan { Id = "plan1", Nombre = "Premium" };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(plan);
        _mockUsuarios.Setup(c => c.InsertOneAsync(
            It.IsAny<UsuarioWeb>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RegisterWebRequest("Juan", "Perez", "juan@test.com", "Password123!", "Premium", "Lopez");
        var result = await _service.RegisterWebAsync(request);

        result.Should().NotBeNull();
        result!.RequiresVerification.Should().BeTrue();
        result.Rol.Should().Be("dueno");
        result.Plan.Should().Be("Premium");
    }

    [Fact]
    public async Task RegisterWebAsync_CorreoExistente_RetornaNull()
    {
        var existing = new UsuarioWeb { Correo = "juan@test.com", Activo = true };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(existing);

        var request = new RegisterWebRequest("Juan", "Perez", "juan@test.com", "Password123!", "Premium", "Lopez");
        var result = await _service.RegisterWebAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RegisterWebAsync_PlanNoExiste_RetornaNull()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync((Plan?)null);

        var request = new RegisterWebRequest("Juan", "Perez", "juan@test.com", "Password123!", "Inexistente", "Lopez");
        var result = await _service.RegisterWebAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginWebAsync_CredencialesValidas_RetornaAuthResponse()
    {
        var user = new UsuarioWeb
        {
            Id = "user123", Correo = "test@test.com", Activo = true,
            PasswordHash = PasswordHasher.Hash("Password123!"),
            PlanId = "plan1", Nombre = "Test", ApellidoPaterno = "User"
        };
        var plan = new Plan { Id = "plan1", Nombre = "Premium" };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(plan);

        var request = new LoginWebRequest("test@test.com", "Password123!");
        var result = await _service.LoginWebAsync(request);

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
        result.Rol.Should().Be("dueno");
    }

    [Fact]
    public async Task LoginWebAsync_CredencialesInvalidas_RetornaNull()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new LoginWebRequest("wrong@test.com", "WrongPass123!");
        var result = await _service.LoginWebAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginWebAsync_UsuarioInactivo_RetornaNull()
    {
        var user = new UsuarioWeb
        {
            Id = "user123", Correo = "test@test.com", Activo = false,
            PasswordHash = PasswordHasher.Hash("Password123!")
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);

        var request = new LoginWebRequest("test@test.com", "Password123!");
        var result = await _service.LoginWebAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginByCodigoAsync_CodigoPaciente_RetornaAuthResponse()
    {
        var paciente = new Paciente { Id = "pac123", CodigoAccesoQr = "ABC12345", Nombre = "Paciente" };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);

        var request = new LoginCodigoRequest("ABC12345");
        var result = await _service.LoginByCodigoAsync(request);

        result.Should().NotBeNull();
        result!.Rol.Should().Be("paciente");
        result.Nombre.Should().Be("Paciente");
    }

    [Fact]
    public async Task LoginByCodigoAsync_CodigoCuidador_RetornaAuthResponse()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync((Paciente?)null);
        var cuidador = new Cuidador { Id = "cuid123", CodigoAccesoQr = "CU-ABC123", Nombre = "Cuidador" };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Cuidador>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync(cuidador);

        var request = new LoginCodigoRequest("CU-ABC123");
        var result = await _service.LoginByCodigoAsync(request);

        result.Should().NotBeNull();
        result!.Rol.Should().Be("cuidador");
    }

    [Fact]
    public async Task LoginByCodigoAsync_CodigoInvalido_RetornaNull()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync((Paciente?)null);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Cuidador>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync((Cuidador?)null);

        var request = new LoginCodigoRequest("INVALID");
        var result = await _service.LoginByCodigoAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Enviar2FAAsync_UsuarioExiste_RetornaTrue()
    {
        var user = new UsuarioWeb { Id = "user123", Correo = "test@test.com", Activo = true };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<UpdateResult>().Object);

        var request = new Enviar2FARequest("test@test.com");
        var result = await _service.Enviar2FAAsync(request);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Enviar2FAAsync_UsuarioNoExiste_RetornaFalse()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new Enviar2FARequest("noexist@test.com");
        var result = await _service.Enviar2FAAsync(request);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Verificar2FAAsync_CodigoValido_RetornaAuthResponse()
    {
        var user = new UsuarioWeb
        {
            Id = "user123", Correo = "test@test.com", Activo = true,
            TwoFactorCode = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("123456"))), TwoFactorExpira = DateTime.UtcNow.AddMinutes(5),
            PlanId = "plan1", Nombre = "Test", ApellidoPaterno = "User"
        };
        var plan = new Plan { Id = "plan1", Nombre = "Premium" };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(plan);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<UpdateResult>().Object);

        var request = new Verificar2FARequest("test@test.com", "123456");
        var result = await _service.Verificar2FAAsync(request);

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Verificar2FAAsync_CodigoInvalido_RetornaNull()
    {
        var user = new UsuarioWeb
        {
            Id = "user123", Correo = "test@test.com", Activo = true,
            TwoFactorCode = "123456", TwoFactorExpira = DateTime.UtcNow.AddMinutes(5)
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);

        var request = new Verificar2FARequest("test@test.com", "999999");
        var result = await _service.Verificar2FAAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ForgotPasswordAsync_UsuarioExiste_RetornaTrue()
    {
        var user = new UsuarioWeb { Id = "user123", Correo = "test@test.com", Activo = true };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<UpdateResult>().Object);

        var request = new ForgotPasswordRequest("test@test.com");
        var result = await _service.ForgotPasswordAsync(request);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ForgotPasswordAsync_UsuarioNoExiste_RetornaFalse()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new ForgotPasswordRequest("noexist@test.com");
        var result = await _service.ForgotPasswordAsync(request);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPasswordAsync_TokenValido_RetornaTrue()
    {
        var user = new UsuarioWeb
        {
            Id = "user123", ResetPasswordToken = "valid-token",
            ResetPasswordExpira = DateTime.UtcNow.AddHours(1)
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<UpdateResult>().Object);

        var request = new ResetPasswordRequest("valid-token", "test@example.com", "NewPassword123!");
        var result = await _service.ResetPasswordAsync(request);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPasswordAsync_TokenInvalido_RetornaFalse()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new ResetPasswordRequest("invalid-token", "test@example.com", "NewPassword123!");
        var result = await _service.ResetPasswordAsync(request);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CambiarPasswordAsync_PasswordCorrecta_RetornaTrue()
    {
        var user = new UsuarioWeb
        {
            Id = "user123", PasswordHash = PasswordHasher.Hash("OldPass123!")
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<UpdateResult>().Object);

        var request = new CambiarPasswordRequest("OldPass123!", "NewPass123!");
        var result = await _service.CambiarPasswordAsync("user123", request);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CambiarPasswordAsync_PasswordIncorrecta_RetornaFalse()
    {
        var user = new UsuarioWeb
        {
            Id = "user123", PasswordHash = PasswordHasher.Hash("CorrectPass123!")
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);

        var request = new CambiarPasswordRequest("WrongPass123!", "NewPass123!");
        var result = await _service.CambiarPasswordAsync("user123", request);

        result.Should().BeFalse();
    }
}
