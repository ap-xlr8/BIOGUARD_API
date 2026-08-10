using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Moq;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;
using BioGuard.Api.Controllers;

namespace Test1BioGuard.SecurityTests;

public class AuthorizationSecurityTests : IClassFixture<IntegrationTests.CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly Mock<IMongoCollection<Paciente>> _mockPacientes;
    private readonly Mock<IMongoCollection<Medicamento>> _mockMedicamentos;
    private readonly Mock<IMongoCollection<Alerta>> _mockAlertas;
    private readonly Mock<IMongoCollection<LecturaSensor>> _mockLecturas;
    private readonly Mock<IMongoCollection<EventoMetabolico>> _mockEventos;
    private readonly Mock<IMongoCollection<Plan>> _mockPlanes;
    private readonly Mock<IMongoCollection<Cuidador>> _mockCuidadores;
    private readonly Mock<IMongoCollection<PrediccionMl>> _mockPredicciones;
    private readonly Mock<IMongoCollection<ModeloMl>> _mockModelos;
    private readonly Mock<IMongoCollection<ReporteCompartido>> _mockReportesCompartidos;

    private const string PacienteAId = "111111111111111111111111";
    private const string PacienteBId = "222222222222222222222222";
    private const string DuenoAId = "dueno_user_a";
    private const string DuenoBId = "dueno_user_b";
    private const string CuidadorId = "cuidador_user";

    public AuthorizationSecurityTests(IntegrationTests.CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _mockDb = factory.MockDbContext;

        _mockPacientes = new Mock<IMongoCollection<Paciente>>();
        _mockMedicamentos = new Mock<IMongoCollection<Medicamento>>();
        _mockAlertas = new Mock<IMongoCollection<Alerta>>();
        _mockLecturas = new Mock<IMongoCollection<LecturaSensor>>();
        _mockEventos = new Mock<IMongoCollection<EventoMetabolico>>();
        _mockPlanes = new Mock<IMongoCollection<Plan>>();
        _mockCuidadores = new Mock<IMongoCollection<Cuidador>>();
        _mockPredicciones = new Mock<IMongoCollection<PrediccionMl>>();
        _mockModelos = new Mock<IMongoCollection<ModeloMl>>();
        _mockReportesCompartidos = new Mock<IMongoCollection<ReporteCompartido>>();

        _mockDb.Setup(db => db.Pacientes).Returns(_mockPacientes.Object);
        _mockDb.Setup(db => db.Medicamentos).Returns(_mockMedicamentos.Object);
        _mockDb.Setup(db => db.Alertas).Returns(_mockAlertas.Object);
        _mockDb.Setup(db => db.LecturasSensores).Returns(_mockLecturas.Object);
        _mockDb.Setup(db => db.EventosMetabolicos).Returns(_mockEventos.Object);
        _mockDb.Setup(db => db.Planes).Returns(_mockPlanes.Object);
        _mockDb.Setup(db => db.Cuidadores).Returns(_mockCuidadores.Object);
        _mockDb.Setup(db => db.PrediccionesMl).Returns(_mockPredicciones.Object);
        _mockDb.Setup(db => db.ModelosMl).Returns(_mockModelos.Object);
        _mockDb.Setup(db => db.ReportesCompartidos).Returns(_mockReportesCompartidos.Object);
    }

    private void SetupFindToListAsyncEmpty<T>(Mock<IMongoCollection<T>> mockCollection)
    {
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<T>>(),
                It.IsAny<FilterDefinition<T>>(),
                It.IsAny<SortDefinition<T>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(new List<T>());
    }

    private void SetupFindFirstOrDefaultAsync<T>(Mock<IMongoCollection<T>> mockCollection, T? result)
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<T>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<T, bool>>>()))
            .ReturnsAsync(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // IDOR PROTECTION TESTS - Medicamentos
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task IDOR_Medicamentos_PacienteANoPuedeVerPacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockMedicamentos);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.GetAsync($"/api/Medicamentos/by-paciente/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Medicamentos_PacienteNoPuedeEditarPacienteB_Retorna403()
    {
        var pacienteB = new Paciente { Id = PacienteBId, UsuarioWebId = DuenoBId, Nombre = "Paciente B" };
        SetupFindFirstOrDefaultAsync(_mockPacientes, pacienteB);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var request = new { Nombre = "Hacked", Dosis = "X", Horario = "X" };
        var response = await _client.PutAsJsonAsync($"/api/Medicamentos/m1", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Medicamentos_PacienteNoPuedeEliminarPacienteB_Retorna403()
    {
        var medicamento = new Medicamento { Id = "m1", PacienteId = PacienteBId, Nombre = "Test" };
        SetupFindFirstOrDefaultAsync(_mockMedicamentos, medicamento);

        var pacienteB = new Paciente { Id = PacienteBId, UsuarioWebId = DuenoBId, Nombre = "Paciente B" };
        SetupFindFirstOrDefaultAsync(_mockPacientes, pacienteB);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.DeleteAsync("/api/Medicamentos/m1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Medicamentos_CuidadorResumenNoPuedeVerDetallePacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockMedicamentos);
        var cuidadorAsignado = new Cuidador { Id = CuidadorId, UsuarioWebId = CuidadorId, PacienteId = PacienteBId, Nombre = "Cuidador Test", NivelAcceso = "resumen_semanal" };
        SetupFindFirstOrDefaultAsync(_mockCuidadores, cuidadorAsignado);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId));

        var response = await _client.GetAsync($"/api/Medicamentos/by-paciente/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Medicamentos_DuenoAPuedeVerPacienteA_Retorna200()
    {
        var pacienteA = new Paciente { Id = PacienteAId, UsuarioWebId = DuenoAId, Nombre = "Paciente A" };
        SetupFindFirstOrDefaultAsync(_mockPacientes, pacienteA);
        SetupFindToListAsyncEmpty(_mockMedicamentos);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateDuenoToken(DuenoAId));

        var response = await _client.GetAsync($"/api/Medicamentos/by-paciente/{PacienteAId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IDOR_Medicamentos_DuenoANoPuedeVerPacienteB_Retorna403()
    {
        var pacienteB = new Paciente { Id = PacienteBId, UsuarioWebId = DuenoBId, Nombre = "Paciente B" };
        SetupFindFirstOrDefaultAsync(_mockPacientes, pacienteB);
        SetupFindToListAsyncEmpty(_mockMedicamentos);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateDuenoToken(DuenoAId));

        var response = await _client.GetAsync($"/api/Medicamentos/by-paciente/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════
    // IDOR PROTECTION TESTS - Alertas
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task IDOR_Alertas_PacienteANoPuedeVerPacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockAlertas);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.GetAsync($"/api/Alertas/by-paciente/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Alertas_PacienteANoPuedeVerPendientesPacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockAlertas);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.GetAsync($"/api/Alertas/pendientes/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Alertas_CuidadorPuedeVerPacienteB_Retorna200()
    {
        SetupFindToListAsyncEmpty(_mockAlertas);
        var cuidadorAsignado = new Cuidador { Id = CuidadorId, UsuarioWebId = CuidadorId, PacienteId = PacienteBId, Nombre = "Cuidador Test", NivelAcceso = "resumen_semanal" };
        SetupFindFirstOrDefaultAsync(_mockCuidadores, cuidadorAsignado);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId));

        var response = await _client.GetAsync($"/api/Alertas/by-paciente/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IDOR_Alertas_DuenoANoPuedeEliminarAlertasPacienteB_Retorna403()
    {
        var alerta = new Alerta { Id = "a1", PacienteId = PacienteBId, Tipo = "glucosa", Nivel = "critico", Titulo = "Test", Mensaje = "Test" };
        SetupFindFirstOrDefaultAsync(_mockAlertas, alerta);

        var pacienteB = new Paciente { Id = PacienteBId, UsuarioWebId = DuenoBId, Nombre = "Paciente B" };
        SetupFindFirstOrDefaultAsync(_mockPacientes, pacienteB);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateDuenoToken(DuenoAId));

        var response = await _client.DeleteAsync("/api/Alertas/a1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════
    // IDOR PROTECTION TESTS - Reportes
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task IDOR_Reportes_PacienteANoPuedeVerResumenPacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockLecturas);
        SetupFindToListAsyncEmpty(_mockEventos);
        SetupFindToListAsyncEmpty(_mockAlertas);
        SetupFindToListAsyncEmpty(_mockMedicamentos);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.GetAsync($"/api/Reportes/resumen/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Reportes_PacienteANoPuedeVerHistorialAlertasPacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockAlertas);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.GetAsync($"/api/Reportes/historial-alertas/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Reportes_PacienteANoPuedeVerHistorialEventosPacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockEventos);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.GetAsync($"/api/Reportes/historial-eventos/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Reportes_PacienteANoPuedeVerHistorialMedicamentosPacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockMedicamentos);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.GetAsync($"/api/Reportes/historial-medicamentos/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Reportes_PacienteANoPuedeVerHistorialLecturasPacienteB_Retorna403()
    {
        SetupFindToListAsyncEmpty(_mockLecturas);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.GetAsync($"/api/Reportes/historial-lecturas/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IDOR_Reportes_CuidadorPuedeVerResumenPacienteB_Retorna200()
    {
        SetupFindToListAsyncEmpty(_mockLecturas);
        SetupFindToListAsyncEmpty(_mockEventos);
        SetupFindToListAsyncEmpty(_mockAlertas);
        SetupFindToListAsyncEmpty(_mockMedicamentos);
        var cuidadorAsignado = new Cuidador
        {
            Id = CuidadorId,
            UsuarioWebId = CuidadorId,
            PacienteId = PacienteBId,
            Nombre = "Cuidador Test",
            NivelAcceso = "resumen_semanal"
        };
        SetupFindFirstOrDefaultAsync(_mockCuidadores, cuidadorAsignado);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "resumen_semanal"));

        var response = await _client.GetAsync($"/api/Reportes/resumen/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IDOR_Reportes_DuenoANoPuedeVerResumenPacienteB_Retorna403()
    {
        var pacienteB = new Paciente { Id = PacienteBId, UsuarioWebId = DuenoBId, Nombre = "Paciente B" };
        SetupFindFirstOrDefaultAsync(_mockPacientes, pacienteB);
        SetupFindToListAsyncEmpty(_mockLecturas);
        SetupFindToListAsyncEmpty(_mockEventos);
        SetupFindToListAsyncEmpty(_mockAlertas);
        SetupFindToListAsyncEmpty(_mockMedicamentos);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateDuenoToken(DuenoAId));

        var response = await _client.GetAsync($"/api/Reportes/resumen/{PacienteBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════
    // ROLE-BASED AUTHORIZATION - Planes (dueno only)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuthZ_Planes_PacienteNoPuedeCrear_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var request = new { Nombre = "Plan", Precio = 9.99m, Descripcion = "Test" };
        var response = await _client.PostAsJsonAsync("/api/Planes", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Planes_CuidadorNoPuedeCrear_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId));

        var request = new { Nombre = "Plan", Precio = 9.99m, Descripcion = "Test" };
        var response = await _client.PostAsJsonAsync("/api/Planes", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Planes_PacienteNoPuedeEditar_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var request = new { Nombre = "Plan", Precio = 9.99m, Descripcion = "Test" };
        var response = await _client.PutAsJsonAsync("/api/Planes/plan1", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Planes_CuidadorNoPuedeEditar_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId));

        var request = new { Nombre = "Plan", Precio = 9.99m, Descripcion = "Test" };
        var response = await _client.PutAsJsonAsync("/api/Planes/plan1", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Planes_PacienteNoPuedeEliminar_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.DeleteAsync("/api/Planes/plan1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Planes_CuidadorNoPuedeEliminar_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId));

        var response = await _client.DeleteAsync("/api/Planes/plan1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Planes_PacienteNoPuedeSeed_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.PostAsJsonAsync("/api/Planes/seed", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Planes_SinTokenCrear_Retorna401()
    {
        var request = new { Nombre = "Plan", Precio = 9.99m, Descripcion = "Test" };
        var response = await _client.PostAsJsonAsync("/api/Planes", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Planes_SinTokenEliminar_Retorna401()
    {
        var response = await _client.DeleteAsync("/api/Planes/plan1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════════════════════════════
    // ROLE-BASED AUTHORIZATION - Medicamentos (dueno only for write)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuthZ_Medicamentos_PacienteNoPuedeCrear_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var request = new { PacienteId = PacienteAId, Nombre = "Test", Dosis = "10mg", Horario = "8:00" };
        var response = await _client.PostAsJsonAsync("/api/Medicamentos", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Medicamentos_CuidadorNoPuedeCrear_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId));

        var request = new { PacienteId = PacienteAId, Nombre = "Test", Dosis = "10mg", Horario = "8:00" };
        var response = await _client.PostAsJsonAsync("/api/Medicamentos", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Medicamentos_PacienteNoPuedeEditar_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var request = new { Nombre = "Test", Dosis = "10mg", Horario = "8:00" };
        var response = await _client.PutAsJsonAsync("/api/Medicamentos/m1", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Medicamentos_PacienteNoPuedeEliminar_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.DeleteAsync("/api/Medicamentos/m1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Medicamentos_SinTokenCrear_Retorna401()
    {
        var request = new { PacienteId = "pac123", Nombre = "Test", Dosis = "10mg", Horario = "8:00" };
        var response = await _client.PostAsJsonAsync("/api/Medicamentos", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Medicamentos_SinTokenEditar_Retorna401()
    {
        var request = new { Nombre = "Test", Dosis = "10mg", Horario = "8:00" };
        var response = await _client.PutAsJsonAsync("/api/Medicamentos/m1", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Medicamentos_SinTokenEliminar_Retorna401()
    {
        var response = await _client.DeleteAsync("/api/Medicamentos/m1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════════════════════════════
    // ROLE-BASED AUTHORIZATION - Alertas
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuthZ_Alertas_SinTokenCrear_Retorna401()
    {
        var request = new { PacienteId = "pac123", Tipo = "glucosa", Nivel = "critico", Titulo = "Test", Mensaje = "Test" };
        var response = await _client.PostAsJsonAsync("/api/Alertas", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Alertas_SinTokenResolver_Retorna401()
    {
        var request = new { CuidadorId = "c1", AccionTomada = "Dada" };
        var response = await _client.PutAsJsonAsync("/api/Alertas/a1/resolver", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Alertas_SinTokenEliminar_Retorna401()
    {
        var response = await _client.DeleteAsync("/api/Alertas/a1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Alertas_PacienteNoPuedeEliminar_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var response = await _client.DeleteAsync("/api/Alertas/a1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthZ_Alertas_CuidadorNoPuedeEliminar_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId));

        var response = await _client.DeleteAsync("/api/Alertas/a1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════
    // ROLE-BASED AUTHORIZATION - Notificaciones
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuthZ_Notificaciones_SinTokenListar_Retorna401()
    {
        var response = await _client.GetAsync("/api/Notificaciones");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Notificaciones_SinTokenCrear_Retorna401()
    {
        var request = new { PacienteId = "pac123", Titulo = "Test", Mensaje = "Test", Tipo = "info" };
        var response = await _client.PostAsJsonAsync("/api/Notificaciones", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Notificaciones_SinTokenEliminar_Retorna401()
    {
        var response = await _client.DeleteAsync("/api/Notificaciones/n1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════════════════════════════
    // ROLE-BASED AUTHORIZATION - Dispositivos
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuthZ_Dispositivos_SinTokenVincular_Retorna401()
    {
        var request = new { Nombre = "Watch", MacAddress = "AA:BB:CC" };
        var response = await _client.PostAsJsonAsync("/api/Dispositivos/vincular", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Dispositivos_SinTokenHeartbeat_Retorna401()
    {
        var request = new { PacienteId = "pac123" };
        var response = await _client.PostAsJsonAsync("/api/Dispositivos/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthZ_Dispositivos_DuenoNoPuedeVincular_Retorna401()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                IntegrationTests.TestTokenHelper.GenerateDuenoToken(DuenoAId));

        var request = new { Nombre = "Watch", MacAddress = "AA:BB:CC" };
        var response = await _client.PostAsJsonAsync("/api/Dispositivos/vincular", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════════════════════════════
    // ROLE-BASED AUTHORIZATION - ML (dueno only for write operations)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ML_Entrenar_PacienteNoPuede_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var request = new EntrenarModeloRequest("v1.0", "Test");
        var response = await _client.PostAsJsonAsync("/api/ML/entrenar", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ML_Entrenar_CuidadorNoPuede_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId));

        var request = new EntrenarModeloRequest("v1.0", "Test");
        var response = await _client.PostAsJsonAsync("/api/ML/entrenar", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ML_Reentrenar_PacienteNoPuede_Retorna403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GeneratePacienteToken(PacienteAId));

        var request = new EntrenarModeloRequest("v1.1", "Re-train");
        var response = await _client.PostAsJsonAsync("/api/ML/reentrenar", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ML_Metricas_DuenoPuedeVerCualquierModelo_Retorna200()
    {
        var modelo = new ModeloMl { Id = "mod1", Version = "v1.0", Accuracy = 0.92 };
        SetupFindFirstOrDefaultAsync(_mockModelos, modelo);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateDuenoToken(DuenoAId));

        var response = await _client.GetAsync("/api/ML/metricas/mod1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ML_Entrenar_SinToken_Retorna401()
    {
        var request = new EntrenarModeloRequest("v1.0", "Test");
        var response = await _client.PostAsJsonAsync("/api/ML/entrenar", request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════════════════════════════
    // CUIDADOR NIVEL DE ACCESO ESCALATION TESTS
    // ═══════════════════════════════════════════════════════════════

    private void SetupCuidadorOwnership(string nivelAcceso = "solo_alertas")
    {
        var cuidadorAsignado = new Cuidador { Id = CuidadorId, UsuarioWebId = CuidadorId, PacienteId = PacienteBId, Nombre = "Cuidador Test", NivelAcceso = nivelAcceso };
        SetupFindFirstOrDefaultAsync(_mockCuidadores, cuidadorAsignado);

        var pacienteB = new Paciente { Id = PacienteBId, UsuarioWebId = DuenoBId, Nombre = "Paciente B" };
        SetupFindFirstOrDefaultAsync(_mockPacientes, pacienteB);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorSoloAlertasNoPuedeVerResumen_Retorna403()
    {
        SetupCuidadorOwnership();

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "solo_alertas"));

        var response = await _client.GetAsync($"/api/Reportes/resumen/{PacienteBId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorSoloAlertasNoPuedeVerHistorialAlertas_Retorna403()
    {
        SetupCuidadorOwnership();

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "solo_alertas"));

        var response = await _client.GetAsync($"/api/Reportes/historial-alertas/{PacienteBId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorSoloAlertasNoPuedeVerHistorialEventos_Retorna403()
    {
        SetupCuidadorOwnership();

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "solo_alertas"));

        var response = await _client.GetAsync($"/api/Reportes/historial-eventos/{PacienteBId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorSoloAlertasNoPuedeCompartirReporte_Retorna403()
    {
        SetupCuidadorOwnership();

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "solo_alertas"));

        var request = new { PacienteId = PacienteBId, DiasValidez = 7 };
        var response = await _client.PostAsJsonAsync($"/api/Reportes/{PacienteBId}/compartir", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorResumenSemanalNoPuedeCompartirReporte_Retorna403()
    {
        SetupCuidadorOwnership("resumen_semanal");

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "resumen_semanal"));

        var request = new { PacienteId = PacienteBId, DiasValidez = 7 };
        var response = await _client.PostAsJsonAsync($"/api/Reportes/{PacienteBId}/compartir", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorHistoricoCompletoPuedeCompartirReporte_Retorna200()
    {
        SetupCuidadorOwnership("historial_completo");

        var reporte = new ReporteCompartido
        {
            Id = "rep1",
            PacienteId = PacienteBId,
            TokenAcceso = "share_token",
            FechaExpiracion = DateTime.UtcNow.AddDays(7),
            UsuarioWebId = CuidadorId
        };
        _mockReportesCompartidos.Setup(c => c.InsertOneAsync(
                It.IsAny<ReporteCompartido>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<ReporteCompartido, InsertOneOptions, CancellationToken>((r, _, _) => r.Id = "rep1")
            .Returns(Task.CompletedTask);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "historial_completo"));

        var request = new { PacienteId = PacienteBId, DiasValidez = 7 };
        var response = await _client.PostAsJsonAsync($"/api/Reportes/{PacienteBId}/compartir", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorSoloAlertasNoPuedeExportarPDF_Retorna403()
    {
        SetupCuidadorOwnership();

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "solo_alertas"));

        var response = await _client.GetAsync($"/api/Sensores/lecturas/{PacienteBId}/exportar-pdf");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorSoloAlertasNoPuedeVerTrackingRuta_Retorna403()
    {
        SetupCuidadorOwnership();

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "solo_alertas"));

        var response = await _client.GetAsync($"/api/Sensores/tracking/{PacienteBId}/ruta?desde=2024-01-01T00:00:00Z&hasta=2024-12-31T23:59:59Z");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NivelAcceso_CuidadorSoloAlertasNoPuedeVerMLMetricas_Retorna403()
    {
        SetupCuidadorOwnership();

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", IntegrationTests.TestTokenHelper.GenerateCuidadorToken(CuidadorId, "solo_alertas"));

        var response = await _client.GetAsync("/api/ML/metricas/mod1");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════
    // HEALTH ENDPOINT
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Health_Get_Retorna200ConStatus()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
        doc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }
}
