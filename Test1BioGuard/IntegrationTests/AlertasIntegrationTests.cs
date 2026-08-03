using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Moq;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;

namespace Test1BioGuard.IntegrationTests;

public class AlertasIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly Mock<IMongoCollection<Alerta>> _mockAlertas;

    public AlertasIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _mockDb = factory.MockDbContext;
        _mockAlertas = new Mock<IMongoCollection<Alerta>>();
        _mockDb.Setup(db => db.Alertas).Returns(_mockAlertas.Object);
    }

    [Fact]
    public async Task ObtenerPorPaciente_ConAlertas_Retorna200()
    {
        var pacienteId = "123456789012345678901234";
        var alertas = new List<Alerta>
        {
            new() { Id = "a1", PacienteId = pacienteId, Tipo = "glucosa", Nivel = "critico", Titulo = "Alerta", Mensaje = "Glucosa alta", Atendida = false, FechaCreacion = DateTime.UtcNow }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<FilterDefinition<Alerta>>(),
                It.IsAny<SortDefinition<Alerta>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(alertas);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GeneratePacienteToken(pacienteId));

        var response = await _client.GetAsync($"/api/Alertas/by-paciente/{pacienteId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ObtenerPorPaciente_SinToken_Retorna401()
    {
        var response = await _client.GetAsync("/api/Alertas/by-paciente/pac123");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ObtenerPendientes_ConPendientes_Retorna200()
    {
        var pacienteId = "123456789012345678901234";
        var alertas = new List<Alerta>
        {
            new() { Id = "a1", PacienteId = pacienteId, Tipo = "glucosa", Nivel = "critico", Titulo = "Pendiente", Mensaje = "X", Atendida = false, FechaCreacion = DateTime.UtcNow }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<FilterDefinition<Alerta>>(),
                It.IsAny<SortDefinition<Alerta>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(alertas);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GeneratePacienteToken(pacienteId));

        var response = await _client.GetAsync($"/api/Alertas/pendientes/{pacienteId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Crear_DatosValidos_Retorna200()
    {
        _mockAlertas.Setup(c => c.InsertOneAsync(
            It.IsAny<Alerta>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GenerateDuenoToken());

        var request = new
        {
            PacienteId = "pac123", Tipo = "glucosa", Nivel = "Crítico",
            Titulo = "Glucosa alta", Mensaje = "Nivel peligroso",
            PulsoBpm = 120, TemperaturaC = 38.5
        };
        var response = await _client.PostAsJsonAsync("/api/Alertas", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Alerta creada");
    }

    [Fact]
    public async Task Resolver_AlertaExiste_Retorna200()
    {
        var alerta = new Alerta
        {
            Id = "a1",
            PacienteId = "123456789012345678901234",
            Tipo = "glucosa",
            Nivel = "Crítico",
            Titulo = "Alerta",
            Mensaje = "Glucosa alta",
            Atendida = false,
            FechaCreacion = DateTime.UtcNow
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync(alerta);

        var mockResult = new Mock<UpdateResult>();
        mockResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockAlertas.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Alerta>>(),
                It.IsAny<UpdateDefinition<Alerta>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GenerateDuenoToken());

        var request = new { CuidadorId = "cuidador1", AccionTomada = "Medicina dada" };
        var response = await _client.PutAsJsonAsync("/api/Alertas/a1/resolver", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Alerta resuelta");
    }

    [Fact]
    public async Task Eliminar_AlertaExiste_Retorna204()
    {
        var alerta = new Alerta
        {
            Id = "a1", PacienteId = "123456789012345678901234",
            Tipo = "glucosa", Nivel = "critico", Titulo = "X", Mensaje = "Y"
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync(alerta);

        var paciente = new Paciente { Id = "123456789012345678901234", UsuarioWebId = "user123" };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);

        var mockResult = new Mock<DeleteResult>();
        mockResult.Setup(r => r.DeletedCount).Returns(1);
        _mockAlertas.Setup(c => c.DeleteOneAsync(
                It.IsAny<FilterDefinition<Alerta>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GenerateDuenoToken("user123"));

        var response = await _client.DeleteAsync("/api/Alertas/a1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetById_AlertaExiste_Retorna200()
    {
        var alerta = new Alerta
        {
            Id = "a1", PacienteId = "123456789012345678901234",
            Tipo = "glucosa", Nivel = "critico", Titulo = "Alerta",
            Mensaje = "Test", Atendida = false, FechaCreacion = DateTime.UtcNow
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync(alerta);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GeneratePacienteToken("123456789012345678901234"));

        var response = await _client.GetAsync("/api/Alertas/a1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("tipo").GetString().Should().Be("glucosa");
    }

    [Fact]
    public async Task GetById_AlertaNoExiste_Retorna404()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync((Alerta?)null);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GenerateDuenoToken());

        var response = await _client.GetAsync("/api/Alertas/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ObtenerPendientes_SinToken_Retorna401()
    {
        var response = await _client.GetAsync("/api/Alertas/pendientes/pac123");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ObtenerById_SinToken_Retorna401()
    {
        var response = await _client.GetAsync("/api/Alertas/a1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Atender_AlertaExiste_Retorna200()
    {
        var alerta = new Alerta
        {
            Id = "a1",
            PacienteId = "123456789012345678901234",
            Tipo = "glucosa",
            Nivel = "Crítico",
            Titulo = "Alerta",
            Mensaje = "Glucosa alta",
            Atendida = false,
            FechaCreacion = DateTime.UtcNow
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync(alerta);

        var mockResult = new Mock<UpdateResult>();
        mockResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockAlertas.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Alerta>>(),
                It.IsAny<UpdateDefinition<Alerta>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GenerateDuenoToken());

        var request = new { NotasAtencion = "Emergencia atendida" };
        var response = await _client.PostAsJsonAsync("/api/Alertas/a1/atender", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Alerta atendida");
    }
}
