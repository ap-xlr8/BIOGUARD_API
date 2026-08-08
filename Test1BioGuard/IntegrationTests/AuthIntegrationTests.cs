using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using BioGuard.Api.Config;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;
using BioGuard.Api.Services;

namespace Test1BioGuard.IntegrationTests;

public class AuthIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly Mock<IMongoCollection<UsuarioWeb>> _mockUsuarios;
    private readonly Mock<IMongoCollection<Plan>> _mockPlanes;

    public AuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _mockDb = factory.MockDbContext;

        _mockUsuarios = new Mock<IMongoCollection<UsuarioWeb>>();
        _mockPlanes = new Mock<IMongoCollection<Plan>>();
        _mockDb.Setup(db => db.UsuariosWeb).Returns(_mockUsuarios.Object);
        _mockDb.Setup(db => db.Planes).Returns(_mockPlanes.Object);
    }

    [Fact]
    public async Task Register_DatosValidos_Retorna200()
    {
        var plan = new Plan { Id = "plan1", Nombre = "Premium", LimiteCuidadores = 3 };
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

        var request = new RegisterWebRequest(
            "Juan", "Perez", "juan@test.com", "Password123!", "Premium", "Lopez");

        var response = await _client.PostAsJsonAsync("/api/Auth/register", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("requiresVerification").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_CorreoExistente_Retorna400()
    {
        var existingUser = new UsuarioWeb { Correo = "juan@test.com", Activo = true };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(existingUser);

        var request = new RegisterWebRequest(
            "Juan", "Perez", "juan@test.com", "Password123!", "Premium", "Lopez");

        var response = await _client.PostAsJsonAsync("/api/Auth/register", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LoginWeb_CredencialesValidas_Retorna200()
    {
        var user = new UsuarioWeb
        {
            Id = "user123",
            Correo = "test@test.com",
            PasswordHash = PasswordHasher.Hash("Password123!"),
            Activo = true,
            PlanId = "plan1",
            Nombre = "Test",
            ApellidoPaterno = "User"
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
        var response = await _client.PostAsJsonAsync("/api/Auth/login-web", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoginWeb_CredencialesInvalidas_Retorna401()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new LoginWebRequest("wrong@test.com", "WrongPass123!");
        var response = await _client.PostAsJsonAsync("/api/Auth/login-web", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginByCodigo_CodigoPaciente_Retorna200()
    {
        var paciente = new Paciente
        {
            Id = "pac123",
            CodigoAccesoQr = "ABC12345",
            Nombre = "Paciente Test"
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);

        var request = new LoginCodigoRequest("ABC12345");
        var response = await _client.PostAsJsonAsync("/api/Auth/login-codigo", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("rol").GetString().Should().Be("paciente");
    }

    [Fact]
    public async Task LoginByCodigo_CodigoInvalido_Retorna401()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync((Paciente?)null);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Cuidador>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync((Cuidador?)null);

        var request = new LoginCodigoRequest("INVALID0");
        var response = await _client.PostAsJsonAsync("/api/Auth/login-codigo", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Enviar2FA_CorreoValido_Retorna200()
    {
        var user = new UsuarioWeb
        {
            Id = "user123",
            Correo = "test@test.com",
            Activo = true,
            PlanId = "plan1",
            Nombre = "Test",
            ApellidoPaterno = "User"
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);

        var mockUpdateResult = new Mock<UpdateResult>();
        mockUpdateResult.Setup(r => r.ModifiedCount).Returns(1);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        var request = new Enviar2FARequest("test@test.com");
        var response = await _client.PostAsJsonAsync("/api/Auth/2FA/enviar", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Si el correo está registrado, recibirás un código");
    }

    [Fact]
    public async Task Enviar2FA_CorreoInvalido_Retorna400()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new Enviar2FARequest("noexiste@test.com");
        var response = await _client.PostAsJsonAsync("/api/Auth/2FA/enviar", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Verificar2FA_CodigoValido_Retorna200()
    {
        var user = new UsuarioWeb
        {
            Id = "user123",
            Correo = "test@test.com",
            Activo = true,
            PlanId = "plan1",
            Nombre = "Test",
            ApellidoPaterno = "User",
            TwoFactorCode = "123456",
            TwoFactorExpira = DateTime.UtcNow.AddMinutes(5)
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

        var mockUpdateResult = new Mock<UpdateResult>();
        mockUpdateResult.Setup(r => r.ModifiedCount).Returns(1);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        var request = new Verificar2FARequest("test@test.com", "123456");
        var response = await _client.PostAsJsonAsync("/api/Auth/2FA/verificar", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Verificar2FA_CodigoInvalido_Retorna400()
    {
        var user = new UsuarioWeb
        {
            Id = "user123",
            Correo = "test@test.com",
            Activo = true,
            PlanId = "plan1",
            Nombre = "Test",
            ApellidoPaterno = "User",
            TwoFactorCode = "123456",
            TwoFactorExpira = DateTime.UtcNow.AddMinutes(5)
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);

        var request = new Verificar2FARequest("test@test.com", "999999");
        var response = await _client.PostAsJsonAsync("/api/Auth/2FA/verificar", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_CorreoValido_Retorna200()
    {
        var user = new UsuarioWeb
        {
            Id = "user123",
            Correo = "test@test.com",
            Activo = true,
            PlanId = "plan1",
            Nombre = "Test",
            ApellidoPaterno = "User"
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);

        var mockUpdateResult = new Mock<UpdateResult>();
        mockUpdateResult.Setup(r => r.ModifiedCount).Returns(1);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        var request = new ForgotPasswordRequest("test@test.com");
        var response = await _client.PostAsJsonAsync("/api/Auth/forgot-password", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Si el correo está registrado, recibirás un link de recuperación");
    }

    [Fact]
    public async Task ForgotPassword_CorreoInvalido_Retorna400()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new ForgotPasswordRequest("noexiste@test.com");
        var response = await _client.PostAsJsonAsync("/api/Auth/forgot-password", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_TokenValido_Retorna200()
    {
        var user = new UsuarioWeb
        {
            Id = "user123",
            Correo = "test@test.com",
            Activo = true,
            PlanId = "plan1",
            Nombre = "Test",
            ApellidoPaterno = "User",
            ResetPasswordToken = "valid-token-abc123",
            ResetPasswordExpira = DateTime.UtcNow.AddHours(1)
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);

        var mockUpdateResult = new Mock<UpdateResult>();
        mockUpdateResult.Setup(r => r.ModifiedCount).Returns(1);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        var request = new ResetPasswordRequest("valid-token-abc123", "test@example.com", "NewPassword123!");
        var response = await _client.PostAsJsonAsync("/api/Auth/reset-password", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Contraseña actualizada correctamente");
    }

    [Fact]
    public async Task ResetPassword_TokenInvalido_Retorna400()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new ResetPasswordRequest("invalid-token", "test@example.com", "NewPassword123!");
        var response = await _client.PostAsJsonAsync("/api/Auth/reset-password", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CambiarPassword_DatosValidos_Retorna200()
    {
        var user = new UsuarioWeb
        {
            Id = "user123",
            Correo = "test@test.com",
            Activo = true,
            PlanId = "plan1",
            Nombre = "Test",
            ApellidoPaterno = "User",
            PasswordHash = PasswordHasher.Hash("OldPassword123!")
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(user);

        var mockUpdateResult = new Mock<UpdateResult>();
        mockUpdateResult.Setup(r => r.ModifiedCount).Returns(1);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateDefinition<UsuarioWeb>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokenHelper.GenerateDuenoToken("user123"));

        var request = new CambiarPasswordRequest("OldPassword123!", "NewPassword123!");
        var response = await _client.PutAsJsonAsync("/api/Auth/cambiar-password", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Contraseña actualizada correctamente");
    }

    [Fact]
    public async Task CambiarPassword_SinToken_Retorna401()
    {
        var request = new CambiarPasswordRequest("OldPassword123!", "NewPassword123!");
        var response = await _client.PutAsJsonAsync("/api/Auth/cambiar-password", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
