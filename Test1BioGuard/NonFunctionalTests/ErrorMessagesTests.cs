using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Moq;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using Test1BioGuard.IntegrationTests;

namespace Test1BioGuard.NonFunctionalTests;

public class ErrorMessagesTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly Mock<IMongoCollection<UsuarioWeb>> _mockUsuarios;
    private readonly Mock<IMongoCollection<Auditoria>> _mockAuditoria;
    private readonly Mock<IMongoCollection<Paciente>> _mockPacientes;
    private readonly Mock<IMongoCollection<Alerta>> _mockAlertas;
    private readonly Mock<IMongoCollection<Plan>> _mockPlanes;
    private readonly Mock<IMongoCollection<Cuidador>> _mockCuidadores;
    private readonly Mock<IMongoCollection<Medicamento>> _mockMedicamentos;
    private readonly Mock<IMongoCollection<LecturaSensor>> _mockLecturas;
    private readonly Mock<IMongoCollection<EventoMetabolico>> _mockEventos;
    private readonly Mock<IMongoCollection<Notificacion>> _mockNotificaciones;
    private readonly Mock<IMongoCollection<Dispositivo>> _mockDispositivos;
    private readonly Mock<IMongoCollection<TrackingGps>> _mockTracking;
    private readonly Mock<IMongoCollection<TicketSoporte>> _mockTickets;
    private readonly Mock<IMongoCollection<PrediccionMl>> _mockPredicciones;
    private readonly Mock<IMongoCollection<ModeloMl>> _mockModelos;
    private readonly Mock<IMongoCollection<ReporteCompartido>> _mockReportes;

    private const string DuenoId = "dueno_test";
    private const string PacienteId = "123456789012345678901234";
    private const string CuidadorId = "cuidador_test";

    public ErrorMessagesTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _mockDb = factory.MockDbContext;

        _mockUsuarios = new Mock<IMongoCollection<UsuarioWeb>>();
        _mockAuditoria = new Mock<IMongoCollection<Auditoria>>();
        _mockPacientes = new Mock<IMongoCollection<Paciente>>();
        _mockAlertas = new Mock<IMongoCollection<Alerta>>();
        _mockPlanes = new Mock<IMongoCollection<Plan>>();
        _mockCuidadores = new Mock<IMongoCollection<Cuidador>>();
        _mockMedicamentos = new Mock<IMongoCollection<Medicamento>>();
        _mockLecturas = new Mock<IMongoCollection<LecturaSensor>>();
        _mockEventos = new Mock<IMongoCollection<EventoMetabolico>>();
        _mockNotificaciones = new Mock<IMongoCollection<Notificacion>>();
        _mockDispositivos = new Mock<IMongoCollection<Dispositivo>>();
        _mockTracking = new Mock<IMongoCollection<TrackingGps>>();
        _mockTickets = new Mock<IMongoCollection<TicketSoporte>>();
        _mockPredicciones = new Mock<IMongoCollection<PrediccionMl>>();
        _mockModelos = new Mock<IMongoCollection<ModeloMl>>();
        _mockReportes = new Mock<IMongoCollection<ReporteCompartido>>();

        _mockDb.Setup(db => db.UsuariosWeb).Returns(_mockUsuarios.Object);
        _mockDb.Setup(db => db.Auditoria).Returns(_mockAuditoria.Object);
        _mockDb.Setup(db => db.Pacientes).Returns(_mockPacientes.Object);
        _mockDb.Setup(db => db.Alertas).Returns(_mockAlertas.Object);
        _mockDb.Setup(db => db.Planes).Returns(_mockPlanes.Object);
        _mockDb.Setup(db => db.Cuidadores).Returns(_mockCuidadores.Object);
        _mockDb.Setup(db => db.Medicamentos).Returns(_mockMedicamentos.Object);
        _mockDb.Setup(db => db.LecturasSensores).Returns(_mockLecturas.Object);
        _mockDb.Setup(db => db.EventosMetabolicos).Returns(_mockEventos.Object);
        _mockDb.Setup(db => db.Notificaciones).Returns(_mockNotificaciones.Object);
        _mockDb.Setup(db => db.Dispositivos).Returns(_mockDispositivos.Object);
        _mockDb.Setup(db => db.TrackingGps).Returns(_mockTracking.Object);
        _mockDb.Setup(db => db.TicketsSoporte).Returns(_mockTickets.Object);
        _mockDb.Setup(db => db.PrediccionesMl).Returns(_mockPredicciones.Object);
        _mockDb.Setup(db => db.ModelosMl).Returns(_mockModelos.Object);
        _mockDb.Setup(db => db.ReportesCompartidos).Returns(_mockReportes.Object);
    }

    [Fact]
    public async Task SecurityHeaders_TodasLasRespuestas_LasIncluye()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.Should().ContainKey("X-XSS-Protection");
        response.Headers.Should().ContainKey("Referrer-Policy");
        response.Headers.Should().ContainKey("Permissions-Policy");
        response.Headers.Should().ContainKey("Strict-Transport-Security");
        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task SecurityHeaders_XPoweredByEliminado()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.TryGetValues("X-Powered-By", out _).Should().BeFalse();
    }

    [Fact]
    public async Task NotFound_SinToken_Retorna401()
    {
        _client.DefaultRequestHeaders.Clear();
        var response = await _client.GetAsync("/api/Alertas/notfound");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_PausarUsuario_SinMotivo_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateToken("admin", "administrador"));

        var request = new { Pausar = true };
        var response = await _client.PutAsJsonAsync("/api/Admin/usuarios/u1/pausar", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Motivo es obligatorio al pausar una cuenta");
    }

    [Fact]
    public async Task Alertas_Crear_NivelRiesgoInvalido_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync((Alerta?)null);

        var request = new { PacienteId = PacienteId, Tipo = "glucosa", Nivel = "ultra-critico", Titulo = "Test", Mensaje = "Test" };
        var response = await _client.PostAsJsonAsync("/api/Alertas", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Nivel de riesgo inválido");
    }

    [Fact]
    public async Task Auth_LoginWeb_CredencialesInvalidas_Retorna401ConMessage()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new { Correo = "noexiste@test.com", Password = "wrong" };
        var response = await _client.PostAsJsonAsync("/api/Auth/login-web", request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Credenciales inválidas");
    }

    [Fact]
    public async Task Auth_ForgotPassword_CorreoNoEncontrado_Retorna400ConMessage()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new { Correo = "noexiste@test.com" };
        var response = await _client.PostAsJsonAsync("/api/Auth/forgot-password", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Si el correo está registrado, recibirás un link de recuperación");
    }

    [Fact]
    public async Task UsuariosWeb_SubirFoto_FormatoInvalido_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        var request = new { FotoBase64 = "not-valid-base64!!" };
        var response = await _client.PutAsJsonAsync("/api/UsuariosWeb/mi-perfil/foto", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Formato o tamaño inválido");
    }

    [Fact]
    public async Task UsuariosWeb_CambiarPlan_PlanNoValido_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync((Plan?)null);

        var request = new { PlanNombre = "PlanInexistente" };
        var response = await _client.PutAsJsonAsync("/api/UsuariosWeb/cambiar-plan", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Plan no válido");
    }

    [Fact]
    public async Task Pagos_CrearSesion_ProcesadorInvalido_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        var request = new { PlanNombre = "Free", Procesador = "bitcoin" };
        var response = await _client.PostAsJsonAsync("/api/Pagos/crear-sesion", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Procesador no válido. Use 'stripe' o 'paypal'");
    }

    [Fact]
    public async Task Pagos_CrearSesion_PlanNoValido_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync((Plan?)null);

        var request = new { PlanNombre = "PlanInexistente", Procesador = "stripe" };
        var response = await _client.PostAsJsonAsync("/api/Pagos/crear-sesion", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Plan no válido");
    }

    [Fact]
    public async Task Planes_Seed_YaExisten_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateAdminToken(DuenoId));

        var planesExistentes = new List<Plan>
        {
            new() { Id = "p1", Nombre = "Free", Precio = 0m, Activo = true }
        };
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(planesExistentes);

        var response = await _client.PostAsJsonAsync("/api/Planes/seed", new { });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Ya existen planes activos");
    }

    [Fact]
    public async Task Reportes_Compartir_DiasValidezInvalido_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(new Paciente { Id = PacienteId, UsuarioWebId = DuenoId });

        var request = new { PacienteId = PacienteId, DiasValidez = 0 };
        var response = await _client.PostAsJsonAsync($"/api/Reportes/{PacienteId}/compartir", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Contain("validation errors");
    }

    [Fact]
    public async Task Sensores_LecturaRango_DesdeMayorQueHasta_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(new Paciente { Id = PacienteId, UsuarioWebId = DuenoId });

        var response = await _client.GetAsync($"/api/Sensores/lecturas/{PacienteId}/rango?desde=2024-06-01T00:00:00Z&hasta=2024-01-01T00:00:00Z");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("El parámetro 'desde' debe ser anterior o igual a 'hasta'");
    }

    [Fact]
    public async Task Sensores_TrackingRuta_DesdeMayorQueHasta_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GeneratePacienteToken(PacienteId));

        var response = await _client.GetAsync($"/api/Sensores/tracking/{PacienteId}/ruta?desde=2024-06-01T00:00:00Z&hasta=2024-01-01T00:00:00Z");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("El parámetro 'desde' debe ser anterior o igual a 'hasta'");
    }

    [Fact]
    public async Task Sensores_EstadisticasTendencia_PeriodoInvalido_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(new Paciente { Id = PacienteId, UsuarioWebId = DuenoId });

        var response = await _client.GetAsync($"/api/Sensores/estadisticas/{PacienteId}/tendencia?periodo=decada");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Periodo inválido. Use: diario, semanal o mensual");
    }

    [Fact]
    public async Task Auth_CambiarPassword_ActualIncorrecto_Retorna400ConMessage()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(DuenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(new UsuarioWeb { Id = DuenoId, PasswordHash = "$2a$11$dummyhashthathas60charactersfortestingabc123" });

        var request = new { PasswordActual = "actual_incorrecto", NuevaPassword = "Nueva123!", ConfirmarPassword = "Nueva123!" };
        var response = await _client.PutAsJsonAsync("/api/Auth/cambiar-password", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Password actual incorrecto");
    }

    [Fact]
    public async Task Auth_ResetPassword_TokenInvalido_Retorna400ConMessage()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var request = new { Token = "invalid_token", Correo = "test@example.com", NuevaPassword = "Nueva123!", ConfirmarPassword = "Nueva123!" };
        var response = await _client.PostAsJsonAsync("/api/Auth/reset-password", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Token inválido o expirado");
    }

    [Fact]
    public async Task Pagos_StripeWebhook_SinFirma_Retorna400()
    {
        var request = new { tipo = "checkout.session.completed", data = new { } };
        var response = await _client.PostAsJsonAsync("/api/Pagos/webhook/stripe", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task TodosLosEndpointsConAuthorize_SinToken_Retornan401()
    {
        var endpoints = new[]
        {
            ("GET", "/api/Admin/usuarios"),
            ("GET", "/api/Alertas/by-paciente/123"),
            ("GET", "/api/Cuidadores"),
            ("GET", "/api/Dispositivos/d1"),
            ("GET", "/api/Medicamentos/by-paciente/123"),
            ("GET", "/api/ML/predicciones/123"),
            ("GET", "/api/Notificaciones"),
            ("GET", "/api/Pacientes/mi-paciente"),
            ("GET", "/api/Pagos/historial"),
            ("GET", "/api/Reportes/resumen/123"),
            ("GET", "/api/Sensores/lecturas/123"),
            ("GET", "/api/Auditoria"),
            ("GET", "/api/UsuariosWeb/mi-perfil"),
        };

        foreach (var (method, path) in endpoints)
        {
            _client.DefaultRequestHeaders.Clear();
            var request = new HttpRequestMessage(new HttpMethod(method), path);
            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"Endpoint {method} {path} sin token debe retornar 401");
        }
    }
}
