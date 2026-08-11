using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Moq;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Controllers;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;

namespace Test1BioGuard.IntegrationTests;

public class FlujosCompletosTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly Mock<IMongoCollection<UsuarioWeb>> _mockUsuarios;
    private readonly Mock<IMongoCollection<Paciente>> _mockPacientes;
    private readonly Mock<IMongoCollection<LecturaSensor>> _mockLecturas;
    private readonly Mock<IMongoCollection<EventoMetabolico>> _mockEventos;
    private readonly Mock<IMongoCollection<Alerta>> _mockAlertas;
    private readonly Mock<IMongoCollection<Plan>> _mockPlanes;
    private readonly Mock<IMongoCollection<Cuidador>> _mockCuidadores;
    private readonly Mock<IMongoCollection<RefreshToken>> _mockRefreshTokens;
    private readonly Mock<IMongoCollection<Notificacion>> _mockNotificaciones;
    private readonly Mock<IMongoCollection<Pago>> _mockPagos;
    private readonly Mock<IMongoCollection<EventoProcesado>> _mockEventosProcesados;
    private readonly Mock<IMongoCollection<PrediccionMl>> _mockPredicciones;
    private readonly Mock<IMongoCollection<Medicamento>> _mockMedicamentos;
    private readonly Mock<IMongoCollection<Dispositivo>> _mockDispositivos;
    private readonly Mock<IMongoCollection<TrackingGps>> _mockTrackingGps;

    public FlujosCompletosTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _mockDb = factory.MockDbContext;

        _mockUsuarios = new Mock<IMongoCollection<UsuarioWeb>>();
        _mockPacientes = new Mock<IMongoCollection<Paciente>>();
        _mockLecturas = new Mock<IMongoCollection<LecturaSensor>>();
        _mockEventos = new Mock<IMongoCollection<EventoMetabolico>>();
        _mockAlertas = new Mock<IMongoCollection<Alerta>>();
        _mockPlanes = new Mock<IMongoCollection<Plan>>();
        _mockCuidadores = new Mock<IMongoCollection<Cuidador>>();
        _mockRefreshTokens = new Mock<IMongoCollection<RefreshToken>>();
        _mockNotificaciones = new Mock<IMongoCollection<Notificacion>>();
        _mockPagos = new Mock<IMongoCollection<Pago>>();
        _mockEventosProcesados = new Mock<IMongoCollection<EventoProcesado>>();
        _mockPredicciones = new Mock<IMongoCollection<PrediccionMl>>();
        _mockMedicamentos = new Mock<IMongoCollection<Medicamento>>();
        _mockDispositivos = new Mock<IMongoCollection<Dispositivo>>();
        _mockTrackingGps = new Mock<IMongoCollection<TrackingGps>>();

        _mockDb.Setup(db => db.UsuariosWeb).Returns(_mockUsuarios.Object);
        _mockDb.Setup(db => db.Pacientes).Returns(_mockPacientes.Object);
        _mockDb.Setup(db => db.LecturasSensores).Returns(_mockLecturas.Object);
        _mockDb.Setup(db => db.EventosMetabolicos).Returns(_mockEventos.Object);
        _mockDb.Setup(db => db.Alertas).Returns(_mockAlertas.Object);
        _mockDb.Setup(db => db.Planes).Returns(_mockPlanes.Object);
        _mockDb.Setup(db => db.Cuidadores).Returns(_mockCuidadores.Object);
        _mockDb.Setup(db => db.RefreshTokens).Returns(_mockRefreshTokens.Object);
        _mockDb.Setup(db => db.Notificaciones).Returns(_mockNotificaciones.Object);
        _mockDb.Setup(db => db.Pagos).Returns(_mockPagos.Object);
        _mockDb.Setup(db => db.EventosProcesados).Returns(_mockEventosProcesados.Object);
        _mockDb.Setup(db => db.PrediccionesMl).Returns(_mockPredicciones.Object);
        _mockDb.Setup(db => db.Medicamentos).Returns(_mockMedicamentos.Object);
        _mockDb.Setup(db => db.Dispositivos).Returns(_mockDispositivos.Object);
        _mockDb.Setup(db => db.TrackingGps).Returns(_mockTrackingGps.Object);
    }

    private void SetupDefaultMockResults()
    {
        var mockUpdateResult = new Mock<UpdateResult>();
        mockUpdateResult.Setup(r => r.ModifiedCount).Returns(1);
        _mockUsuarios.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateDefinition<UsuarioWeb>>(),
            It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        _mockPacientes.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<Paciente>>(),
            It.IsAny<UpdateDefinition<Paciente>>(),
            It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        _mockCuidadores.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<Cuidador>>(),
            It.IsAny<UpdateDefinition<Cuidador>>(),
            It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        _mockLecturas.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<LecturaSensor>>(),
            It.IsAny<UpdateDefinition<LecturaSensor>>(),
            It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpdateResult.Object);

        var mockDeleteResult = new Mock<DeleteResult>();
        mockDeleteResult.Setup(r => r.DeletedCount).Returns(1);
        _mockPacientes.Setup(c => c.DeleteOneAsync(
            It.IsAny<FilterDefinition<Paciente>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockDeleteResult.Object);

        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<LecturaSensor>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<LecturaSensor, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);
        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<EventoMetabolico>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<EventoMetabolico, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);
        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<Notificacion>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Notificacion, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);
        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<Alerta>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);
        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<Medicamento>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Medicamento, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);
        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<Pago>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Pago, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);
        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<Cuidador>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);
        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<Dispositivo>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Dispositivo, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);
        _mockDb.Setup(db => db.DeleteManyAsync(
            It.IsAny<IMongoCollection<TrackingGps>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<TrackingGps, bool>>>()))
            .ReturnsAsync(mockDeleteResult.Object);

        _mockDb.Setup(db => db.CountDocumentsAsync(
            It.IsAny<IMongoCollection<UsuarioWeb>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(1L);
        _mockDb.Setup(db => db.CountDocumentsAsync(
            It.IsAny<IMongoCollection<Paciente>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(1L);
        _mockDb.Setup(db => db.CountDocumentsAsync(
            It.IsAny<IMongoCollection<Alerta>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync(1L);
        _mockDb.Setup(db => db.FindToListAsync(
            It.IsAny<IMongoCollection<Plan>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(new List<Plan> { new() { Id = "plan_free", Nombre = "Gratis", Precio = 0m, Activo = true } });

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Pago>>(),
                It.IsAny<FilterDefinition<Pago>>(),
                It.IsAny<SortDefinition<Pago>>()))
            .ReturnsAsync(new List<Pago>());
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Medicamento>>(),
                It.IsAny<FilterDefinition<Medicamento>>(),
                It.IsAny<SortDefinition<Medicamento>>()))
            .ReturnsAsync(new List<Medicamento>());
    }

    // ═══════════════════════════════════════════════════════════════
    // FLUJO 1: Registro Completo (Dueño → Paciente → Lecturas → Stats)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Flujo1_RegistroCompleto_Exitoso()
    {
        SetupDefaultMockResults();

        // -- Step 1: Register new user --
        var planGratis = new Plan { Id = "plan_free", Nombre = "BioGuard Free", Precio = 0m, AiConsole = false, LimitePacientes = 5 };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(planGratis);

        _mockUsuarios.Setup(c => c.InsertOneAsync(
                It.IsAny<UsuarioWeb>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Callback<UsuarioWeb, InsertOneOptions, CancellationToken>((user, _, _) =>
            {
                if (string.IsNullOrEmpty(user.Id)) user.Id = "user_new";
            })
            .Returns(Task.CompletedTask);

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var registerReq = new RegisterWebRequest("Juan", "Perez", "juan@test.com", "Password123!", "Free");
        var registerResponse = await _client.PostAsJsonAsync("/api/Auth/register", registerReq);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var registerJson = await registerResponse.Content.ReadAsStringAsync();
        var registerDoc = JsonDocument.Parse(registerJson);
        registerDoc.RootElement.GetProperty("requiresVerification").GetBoolean().Should().BeTrue();

        // -- Step 2: Login --
        var usuarioRegistrado = new UsuarioWeb
        {
            Id = "user_new", Nombre = "Juan", ApellidoPaterno = "Perez",
            Correo = "juan@test.com", PlanId = "plan_free", FechaRegistro = DateTime.UtcNow,
            PasswordHash = BioGuard.Api.Services.PasswordHasher.Hash("Password123!"),
            Activo = true
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(usuarioRegistrado);

        var loginReq = new LoginWebRequest("juan@test.com", "Password123!");
        var loginResponse = await _client.PostAsJsonAsync("/api/Auth/login-web", loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 3: Get profile --

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken("user_new"));

        var perfilResponse = await _client.GetAsync("/api/UsuariosWeb/mi-perfil");
        perfilResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var perfilJson = await perfilResponse.Content.ReadAsStringAsync();
        var perfilDoc = JsonDocument.Parse(perfilJson);
        perfilDoc.RootElement.GetProperty("nombre").GetString().Should().Be("Juan");

        // -- Step 4: Update profile --
        var updateReq = new UpdatePerfilRequest("Juan Carlos", "Perez", "Lopez");
        var updateResponse = await _client.PutAsJsonAsync("/api/UsuariosWeb/mi-perfil", updateReq);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 5: Create patient --
        _mockPacientes.Setup(c => c.InsertOneAsync(
                It.IsAny<Paciente>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Callback<Paciente, InsertOneOptions, CancellationToken>((pac, _, _) =>
            {
                if (string.IsNullOrEmpty(pac.Id)) pac.Id = "pac_flujo1";
            })
            .Returns(Task.CompletedTask);

        var pacienteCreado = new Paciente
        {
            Id = "pac_flujo1", UsuarioWebId = "user_new", Nombre = "Ana Perez",
            CodigoAccesoQr = "QR-FLUJO1"
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(pacienteCreado);

        var crearPacienteReq = new CrearPacienteRequest("Ana Perez");
        var pacienteResponse = await _client.PostAsJsonAsync("/api/Pacientes", crearPacienteReq);
        pacienteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 6: Update biometry --
        var bioReq = new UpdateBiometriaRequest(new DateTime(DateTime.Today.Year - 30, 1, 1), "M", 70.5, 170, false, false, "moderada");
        var bioResponse = await _client.PutAsJsonAsync($"/api/Pacientes/pac_flujo1/biometria", bioReq);
        bioResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 7: List patients --
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(new List<Paciente> { pacienteCreado });

        var listResponse = await _client.GetAsync("/api/Pacientes/by-usuario/user_new");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 8: Add sensor reading (as paciente) --
        _mockLecturas.Setup(c => c.InsertOneAsync(
                It.IsAny<LecturaSensor>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GeneratePacienteToken("pac_flujo1"));

        var lecturaReq = new LecturaSensorRequest(75, 36.5, 15.0);
        var lecturaResponse = await _client.PostAsJsonAsync("/api/Sensores/lectura", lecturaReq);
        lecturaResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 9: Get stats (as dueno) --
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken("user_new"));

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<LecturaSensor>>(),
                It.IsAny<FilterDefinition<LecturaSensor>>(),
                It.IsAny<SortDefinition<LecturaSensor>>(),
                It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<LecturaSensor>
            {
                new() { Id = "l1", PulsoBpm = 75, TemperaturaC = 36.5, SudoracionGsr = 15.0, ProbabilidadPico = 0.2, Timestamp = DateTime.UtcNow, Meta = new MetaData { PacienteId = "pac_flujo1" } }
            });

        var statsResponse = await _client.GetAsync("/api/Sensores/estadisticas/pac_flujo1");
        statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statsJson = await statsResponse.Content.ReadAsStringAsync();
        var statsDoc = JsonDocument.Parse(statsJson);
        statsDoc.RootElement.GetProperty("totalLecturas").GetInt32().Should().Be(1);
        statsDoc.RootElement.GetProperty("ultimoPulso").GetInt32().Should().Be(75);
    }

    // ═══════════════════════════════════════════════════════════════
    // FLUJO 2: Gestión de Cuidadores (Crear → QR → Login → Acceso → Nivel)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Flujo2_GestionCuidadores_Exitoso()
    {
        SetupDefaultMockResults();

        var duenoId = "user_dueno2";
        var pacienteId = "pac_flujo2";
        var cuidadorId = "cuid_flujo2";

        var plan = new Plan { Id = "plan_prem", Nombre = "Premium", Precio = 199m, LimiteCuidadores = 5, AiConsole = true };
        var dueno = new UsuarioWeb { Id = duenoId, Nombre = "Maria", PlanId = "plan_prem" };
        var paciente = new Paciente { Id = pacienteId, UsuarioWebId = duenoId, Nombre = "Luis", CodigoAccesoQr = "QR123" };
        var cuidador = new Cuidador
        {
            Id = cuidadorId, UsuarioWebId = duenoId, PacienteId = pacienteId,
            Nombre = "Pedro", Parentesco = "Hermano", NivelAcceso = "resumen_semanal",
            CodigoAccesoQr = "CUID-QR-ABC"
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(plan);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(dueno);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Cuidador>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync(cuidador);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        // -- Step 1: Create caregiver --
        _mockCuidadores.Setup(c => c.InsertOneAsync(
                It.IsAny<Cuidador>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Callback<Cuidador, InsertOneOptions, CancellationToken>((cuid, _, _) =>
            {
                if (string.IsNullOrEmpty(cuid.Id)) cuid.Id = cuidadorId;
            })
            .Returns(Task.CompletedTask);

        _mockDb.Setup(db => db.CountDocumentsAsync(
                It.IsAny<IMongoCollection<Cuidador>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync(0L);

        // Sin cuidadores existentes con ese correo para el paciente (si no, el servicio
        // responde "Ya existe un cuidador con ese correo para este paciente").
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Cuidador>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync((Cuidador?)null);

        var crearReq = new CrearCuidadorRequest(pacienteId, "Pedro", "Hermano", "555-1234", "pedro@test.com");
        var crearResponse = await _client.PostAsJsonAsync("/api/Cuidadores", crearReq);
        crearResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var crearJson = await crearResponse.Content.ReadAsStringAsync();
        var crearDoc = JsonDocument.Parse(crearJson);
        crearDoc.RootElement.GetProperty("cuidadorId").GetString().Should().NotBeNullOrEmpty();

        // El cuidador creado ahora existe: restaurar el mock para el login por QR.
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Cuidador>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync(cuidador);

        // -- Step 2: Get QR code --
        var qrResponse = await _client.GetAsync($"/api/Cuidadores/{cuidadorId}/qr");
        qrResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var qrJson = await qrResponse.Content.ReadAsStringAsync();
        var qrDoc = JsonDocument.Parse(qrJson);
        qrDoc.RootElement.GetProperty("codigoAccesoQr").GetString().Should().Be("CUID-QR-ABC");

        // -- Step 3: Login as cuidador via QR code --
        // Make Paciente search return null so service falls through to Cuidador search
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync((Paciente?)null);

        _mockRefreshTokens.Setup(c => c.InsertOneAsync(
                It.IsAny<RefreshToken>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loginCodigoReq = new LoginCodigoRequest("CUID-QR-ABC");
        var loginResponse = await _client.PostAsJsonAsync("/api/Auth/login-codigo", loginCodigoReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginJson = await loginResponse.Content.ReadAsStringAsync();
        var loginDoc = JsonDocument.Parse(loginJson);
        loginDoc.RootElement.GetProperty("rol").GetString().Should().Be("cuidador");

        // El login por codigo necesita Paciente=null para caer al buscador de cuidadores;
        // restaurar el mock porque el control de acceso consulta el paciente por Id.
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);

        // -- Step 4: Access patient data as cuidador --
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<LecturaSensor>>(),
                It.IsAny<FilterDefinition<LecturaSensor>>(),
                It.IsAny<SortDefinition<LecturaSensor>>(),
                It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<LecturaSensor>
            {
                new() { Id = "l1", PulsoBpm = 80, ProbabilidadPico = 0.3, Timestamp = DateTime.UtcNow, Meta = new MetaData { PacienteId = pacienteId } }
            });

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateCuidadorToken(cuidadorId, "resumen_semanal"));

        var lecturasResponse = await _client.GetAsync($"/api/Sensores/lecturas/{pacienteId}");
        lecturasResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var lecturasJson = await lecturasResponse.Content.ReadAsStringAsync();
        var lecturasDoc = JsonDocument.Parse(lecturasJson);
        lecturasDoc.RootElement.GetArrayLength().Should().Be(1);

        // -- Step 5: Update access level (as dueno) --
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        var nivelReq = new ActualizarNivelAccesoRequest("historial_completo");
        var nivelResponse = await _client.PatchAsJsonAsync($"/api/Cuidadores/{cuidadorId}/nivel-acceso", nivelReq);
        nivelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var nivelJson = await nivelResponse.Content.ReadAsStringAsync();
        var nivelDoc = JsonDocument.Parse(nivelJson);
        nivelDoc.RootElement.GetProperty("nivelAcceso").GetString().Should().Be("historial_completo");
    }

    // ═══════════════════════════════════════════════════════════════
    // FLUJO 3: Ciclo de Pago (Sesión → Webhook Stripe → Verificar Plan → Cancelar)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Flujo3_CicloPago_Exitosa()
    {
        SetupDefaultMockResults();

        var duenoId = "user_pago";
        var planPremium = new Plan { Id = "plan_premium", Nombre = "Premium", Precio = 199m };
        var planGratis = new Plan { Id = "plan_free", Nombre = "BioGuard Free", Precio = 0m };
        var dueno = new UsuarioWeb { Id = duenoId, Nombre = "Carlos", PlanId = "plan_free" };
        var pago = new Pago
        {
            Id = "pago_123", UsuarioWebId = duenoId, Monto = 199m, Moneda = "MXN",
            Estado = "pendiente", MetodoPago = "stripe", StripeSessionId = "cs_test_123",
            FechaPago = DateTime.UtcNow
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(planPremium);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(dueno);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Pago>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Pago, bool>>>()))
            .ReturnsAsync(pago);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        // -- Step 1: Create payment session --
        _mockPagos.Setup(c => c.InsertOneAsync(
                It.IsAny<Pago>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Callback<Pago, InsertOneOptions, CancellationToken>((pag, _, _) =>
            {
                if (string.IsNullOrEmpty(pag.Id)) pag.Id = "pago_123";
            })
            .Returns(Task.CompletedTask);

        var sesionReq = new CrearSesionPagoRequest("Premium", "stripe");
        var sesionResponse = await _client.PostAsJsonAsync("/api/Pagos/crear-sesion", sesionReq);
        sesionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sesionJson = await sesionResponse.Content.ReadAsStringAsync();
        var sesionDoc = JsonDocument.Parse(sesionJson);
        sesionDoc.RootElement.GetProperty("pagoId").GetString().Should().NotBeNullOrEmpty();

        // -- Step 2: Stripe webhook -- checkout.session.completed --
        _mockEventosProcesados.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<EventoProcesado>>(),
                It.IsAny<UpdateDefinition<EventoProcesado>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<UpdateResult>().Object);

        // Mock RegistrarEventoIdSiNoExisteAsync -> InsertOneAsync (non-upsert)
        _mockEventosProcesados.Setup(c => c.InsertOneAsync(
                It.IsAny<EventoProcesado>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Mock EventoYaProcesadoAsync (FindFirstOrDefault on EventosProcesados returns null)
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<EventoProcesado>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<EventoProcesado, bool>>>()))
            .ReturnsAsync((EventoProcesado?)null);

        // Mock ActualizarPagoCompletadoAsync
        var mockUpsert = new Mock<UpdateResult>();
        mockUpsert.Setup(r => r.ModifiedCount).Returns(1);
        mockUpsert.Setup(r => r.UpsertedId).Returns(new MongoDB.Bson.BsonString("evt_test"));
        _mockPagos.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Pago>>(),
                It.IsAny<UpdateDefinition<Pago>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUpsert.Object);

        // Mock ObtenerPagoYUsuarioPorSessionIdAsync
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Pago>>(),
                It.IsAny<FilterDefinition<Pago>>(),
                It.IsAny<SortDefinition<Pago>>()))
            .ReturnsAsync(pago);

        var stripePayload = "{\"id\":\"evt_test\",\"type\":\"checkout.session.completed\",\"data\":{\"object\":{\"id\":\"cs_test_123\",\"customer\":\"cus_test\",\"subscription\":\"sub_test\",\"metadata\":{\"plan_id\":\"plan_premium\",\"usuario_id\":\"user_pago\"}}}}";
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Stripe-Signature", "test_sig");
        var webhookResponse = await _client.PostAsync("/api/Pagos/webhook/stripe",
            new StringContent(stripePayload, System.Text.Encoding.UTF8, "application/json"));
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 3: Get payment history --
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Pago>>(),
                It.IsAny<FilterDefinition<Pago>>(),
                It.IsAny<SortDefinition<Pago>>()))
            .ReturnsAsync(new List<Pago>
            {
                new() { Id = "pago_123", UsuarioWebId = duenoId, Monto = 199m, Moneda = "MXN", Estado = "completado", MetodoPago = "stripe", FechaPago = DateTime.UtcNow }
            });

        var historialResponse = await _client.GetAsync("/api/Pagos/historial");
        historialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var historialJson = await historialResponse.Content.ReadAsStringAsync();
        var historialDoc = JsonDocument.Parse(historialJson);
        historialDoc.RootElement.GetArrayLength().Should().Be(1);

        // -- Step 4: Cancel subscription --
        var cancelResponse = await _client.PostAsync("/api/Pagos/cancelar", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════════════════════════════
    // FLUJO 4: ML Pipeline (Lecturas → Predicción → Medicamento → Toma → Adherencia)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Flujo4_MLPipeline_Exitosa()
    {
        SetupDefaultMockResults();

        var duenoId = "user_ml";
        var pacienteId = "pac_ml";
        var medicamentoId = "med_ml1";

        var plan = new Plan { Id = "plan_ai", Nombre = "Pro Salud", Precio = 399m, AiConsole = true };
        var dueno = new UsuarioWeb { Id = duenoId, Nombre = "Elena", PlanId = "plan_ai" };
        var paciente = new Paciente { Id = pacienteId, UsuarioWebId = duenoId, Nombre = "Sofia" };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(plan);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(dueno);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        // -- Step 1: Get ML predictions (empty initially) --
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<PrediccionMl>>(),
                It.IsAny<FilterDefinition<PrediccionMl>>(),
                It.IsAny<SortDefinition<PrediccionMl>>(),
                It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<PrediccionMl>());

        var predResponse = await _client.GetAsync($"/api/ML/predicciones/{pacienteId}");
        predResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var predJson = await predResponse.Content.ReadAsStringAsync();
        var predDoc = JsonDocument.Parse(predJson);
        predDoc.RootElement.GetArrayLength().Should().Be(0);

        // -- Step 2: Insert sensor readings --
        _mockLecturas.Setup(c => c.InsertOneAsync(
                It.IsAny<LecturaSensor>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // As paciente
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GeneratePacienteToken(pacienteId));

        var lecturaAlta = new LecturaSensorRequest(120, 38.5, 25.0);
        var lecturaResponse = await _client.PostAsJsonAsync("/api/Sensores/lectura", lecturaAlta);
        lecturaResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 3: Run ML diagnosis --
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<PrediccionMl>>(),
                It.IsAny<FilterDefinition<PrediccionMl>>(),
                It.IsAny<SortDefinition<PrediccionMl>>(),
                It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<PrediccionMl>
            {
                new() { Id = "pred_1", PacienteId = pacienteId, ProbabilidadPico = 0.85, NivelRiesgo = "Alto", Recomendacion = "Monitorear", FechaPrediccion = DateTime.UtcNow, ModeloVersion = "v2.0" }
            });

        var diagReq = new DiagnosticarRequest(pacienteId);
        var diagResponse = await _client.PostAsJsonAsync("/api/ML/diagnosticar", diagReq);
        diagResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var diagJson = await diagResponse.Content.ReadAsStringAsync();
        var diagDoc = JsonDocument.Parse(diagJson);
        diagDoc.RootElement.GetProperty("nivelRiesgo").GetString().Should().Be("Alto");

        // -- Step 4: Create medication --
        _mockMedicamentos.Setup(c => c.InsertOneAsync(
                It.IsAny<Medicamento>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Medicamento>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Medicamento, bool>>>()))
            .ReturnsAsync((Medicamento?)null);

        var medReq = new CrearMedicamentoRequest(pacienteId, "Paracetamol", "500mg", "Cada 8 horas", "Tomar con comida");
        var medResponse = await _client.PostAsJsonAsync("/api/Medicamentos", medReq);
        medResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 5: List medications --
        var medicamento = new Medicamento
        {
            Id = medicamentoId, PacienteId = pacienteId, Nombre = "Paracetamol",
            Dosis = "500mg", Horario = "Cada 8 horas", Activo = true, FechaCreacion = DateTime.UtcNow
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Medicamento>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Medicamento, bool>>>()))
            .ReturnsAsync(medicamento);

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Medicamento>>(),
                It.IsAny<FilterDefinition<Medicamento>>(),
                It.IsAny<SortDefinition<Medicamento>>()))
            .ReturnsAsync(new List<Medicamento> { medicamento });

        var listMedResponse = await _client.GetAsync($"/api/Medicamentos/by-paciente/{pacienteId}");
        listMedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listMedJson = await listMedResponse.Content.ReadAsStringAsync();
        var listMedDoc = JsonDocument.Parse(listMedJson);
        listMedDoc.RootElement.GetArrayLength().Should().Be(1);

        // -- Step 6: Register medication intake --
        _mockMedicamentos.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Medicamento>>(),
                It.IsAny<UpdateDefinition<Medicamento>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<UpdateResult>().Object);

        var tomaResponse = await _client.PutAsJsonAsync($"/api/Medicamentos/{medicamentoId}/toma", new { });
        tomaResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 7: Check adherence --
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Medicamento>>(),
                It.IsAny<FilterDefinition<Medicamento>>(),
                It.IsAny<SortDefinition<Medicamento>>(),
                It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<Medicamento>
            {
                new() { Id = medicamentoId, PacienteId = pacienteId, Nombre = "Paracetamol", Activo = true, FechaCreacion = DateTime.UtcNow.AddDays(-3), UltimaToma = DateTime.UtcNow }
            });

        var adhResponse = await _client.GetAsync($"/api/Medicamentos/by-paciente/{pacienteId}/adherencia?desde={DateTime.UtcNow.AddDays(-7):yyyy-MM-dd}&hasta={DateTime.UtcNow:yyyy-MM-dd}");
        adhResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════════════════════════════
    // FLUJO 5: Emergencia Completa (Lectura Crítica → Alerta → Escalar → GPS → Notificación)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Flujo5_EmergenciaCompleta_Exitosa()
    {
        SetupDefaultMockResults();

        var duenoId = "user_emerg";
        var pacienteId = "pac_emerg";
        var alertaId = "alerta_emerg1";
        var cuidadorId = "cuid_emerg1";

        var dueno = new UsuarioWeb { Id = duenoId, Nombre = "Laura", ApellidoPaterno = "Gomez" };
        var paciente = new Paciente { Id = pacienteId, UsuarioWebId = duenoId, Nombre = "Miguel" };
        var cuidador = new Cuidador { Id = cuidadorId, UsuarioWebId = duenoId, PacienteId = pacienteId, Nombre = "Carlos" };
        var ubicacion = new TrackingGps
        {
            Id = "gps_1", Timestamp = DateTime.UtcNow, EsEmergencia = true,
            Ubicacion = new UbicacionGps { Type = "Point", Coordinates = new[] { -99.1332, 19.4326 } },
            Meta = new MetaData { PacienteId = pacienteId }
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(dueno);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Cuidador>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Cuidador, bool>>>()))
            .ReturnsAsync(cuidador);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        // -- Step 1: Create critical alert --
        _mockAlertas.Setup(c => c.InsertOneAsync(
                It.IsAny<Alerta>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync((Alerta?)null);

        var alerta = new Alerta
        {
            Id = alertaId, PacienteId = pacienteId, Tipo = "biometrico",
            Nivel = "Crítico", Titulo = "Pulso crítico", Mensaje = "Pulso 140 bpm",
            SensorData = new SensorData { PulsoBpm = 140, ProbabilidadPico = 0.95 },
            Atendida = false, FechaCreacion = DateTime.UtcNow
        };

        var crearAlertaReq = new CrearAlertaRequest(pacienteId, "biometrico", "Crítico", "Pulso crítico", "Pulso 140 bpm", 140, null, null, 0.95);
        var alertaResponse = await _client.PostAsJsonAsync("/api/Alertas", crearAlertaReq);
        alertaResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 2: Insert GPS tracking --
        _mockTrackingGps.Setup(c => c.InsertOneAsync(
                It.IsAny<TrackingGps>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GeneratePacienteToken(pacienteId));

        var gpsReq = new TrackingGpsRequest(-99.1332, 19.4326, true);
        var gpsResponse = await _client.PostAsJsonAsync("/api/Sensores/tracking", gpsReq);
        gpsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Step 3: Get current location --
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<TrackingGps>>(),
                It.IsAny<FilterDefinition<TrackingGps>>(),
                It.IsAny<SortDefinition<TrackingGps>>()))
            .ReturnsAsync(ubicacion);

        var ubicacionResponse = await _client.GetAsync($"/api/Sensores/tracking/{pacienteId}/actual");
        ubicacionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ubicacionJson = await ubicacionResponse.Content.ReadAsStringAsync();
        var ubicacionDoc = JsonDocument.Parse(ubicacionJson);
        ubicacionDoc.RootElement.GetProperty("esEmergencia").GetBoolean().Should().BeTrue();

        // Mock alert exists for escalation
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync(alerta);

        // -- Step 4: Escalate emergency --
        _mockNotificaciones.Setup(c => c.InsertOneAsync(
                It.IsAny<Notificacion>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockAlertas.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Alerta>>(),
                It.IsAny<UpdateDefinition<Alerta>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<UpdateResult>().Object);

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<TrackingGps>>(),
                It.IsAny<FilterDefinition<TrackingGps>>(),
                It.IsAny<SortDefinition<TrackingGps>>()))
            .ReturnsAsync(ubicacion);

        var escalarResponse = await _client.PostAsync($"/api/Alertas/{alertaId}/escalar-emergencia", null);
        escalarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var escalarJson = await escalarResponse.Content.ReadAsStringAsync();
        var escalarDoc = JsonDocument.Parse(escalarJson);
        escalarDoc.RootElement.GetProperty("message").GetString().Should().Contain("Emergencia");

        // -- Step 5: Verify alert was resolved --
        var alertaResuelta = new Alerta
        {
            Id = alertaId, PacienteId = pacienteId, Nivel = "Crítico",
            Atendida = true, AtendidaPorId = duenoId, FechaCreacion = DateTime.UtcNow, FechaAtencion = DateTime.UtcNow
        };
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Alerta>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Alerta, bool>>>()))
            .ReturnsAsync(alertaResuelta);

        var getAlertaResponse = await _client.GetAsync($"/api/Alertas/{alertaId}");
        getAlertaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getAlertaJson = await getAlertaResponse.Content.ReadAsStringAsync();
        var getAlertaDoc = JsonDocument.Parse(getAlertaJson);
        getAlertaDoc.RootElement.GetProperty("atendida").GetBoolean().Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // FLUJO 6: Ciclo de Vida del Paciente (Crear → Editar → QR → Regenerar QR → Eliminar)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Flujo6_CicloVidaPaciente_Exitoso()
    {
        SetupDefaultMockResults();

        var duenoId = "user_vida";
        var pacienteIdOld = "pac_vida_old";

        var paciente = new Paciente
        {
            Id = pacienteIdOld, UsuarioWebId = duenoId, Nombre = "Paciente Original",
            CodigoAccesoQr = "OLDQR", FechaRegistro = DateTime.UtcNow, PerfilCompletado = false
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        // -- Step 1: List existing patients --
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(new List<Paciente> { paciente });

        var listResponse = await _client.GetAsync("/api/Pacientes/by-usuario/user_vida");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        var listDoc = JsonDocument.Parse(listJson);
        listDoc.RootElement.GetArrayLength().Should().Be(1);

        // -- Step 2: Get QR code --
        var qrResponse = await _client.GetAsync($"/api/Pacientes/{pacienteIdOld}/qr");
        qrResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var qrJson = await qrResponse.Content.ReadAsStringAsync();
        var qrDoc = JsonDocument.Parse(qrJson);
        qrDoc.RootElement.GetProperty("codigoAccesoQr").GetString().Should().Be("OLDQR");

        // -- Step 3: Regenerate QR code --
        var mockRegenUpdate = new Mock<UpdateResult>();
        mockRegenUpdate.Setup(r => r.ModifiedCount).Returns(1);
        _mockPacientes.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Paciente>>(),
                It.IsAny<UpdateDefinition<Paciente>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockRegenUpdate.Object);

        var regenerarResponse = await _client.PostAsync($"/api/Pacientes/{pacienteIdOld}/regenerar-qr", null);
        regenerarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var regenerarJson = await regenerarResponse.Content.ReadAsStringAsync();
        var regenerarDoc = JsonDocument.Parse(regenerarJson);
        regenerarDoc.RootElement.GetProperty("codigoAccesoQr").GetString().Should().NotBe("OLDQR");

        // -- Step 4: Check by email (existing user) --
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(new UsuarioWeb { Id = "existing", Correo = "dueno@test.com" });

        var emailResponse = await _client.GetAsync("/api/UsuariosWeb/by-email/dueno@test.com");
        emailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emailJson = await emailResponse.Content.ReadAsStringAsync();
        var emailDoc = JsonDocument.Parse(emailJson);
        emailDoc.RootElement.GetProperty("existe").GetBoolean().Should().BeTrue();

        // -- Step 5: Delete patient --
        var deleteResponse = await _client.DeleteAsync($"/api/Pacientes/{pacienteIdOld}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ═══════════════════════════════════════════════════════════════
    // FLUJO 7: Error Handling (permisos denegados, validaciones, etc.)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Flujo7_ManejoErrores_Seguridad()
    {
        SetupDefaultMockResults();

        var pacienteId = "pac_seg";
        var otroUsuarioId = "user_otro";
        var duenoId = "user_seg";

        var dueno = new UsuarioWeb { Id = duenoId, Nombre = "Seguridad", PlanId = "plan_free" };
        var otroDueno = new UsuarioWeb { Id = otroUsuarioId, Nombre = "Otro", PlanId = "plan_free" };
        var paciente = new Paciente { Id = pacienteId, UsuarioWebId = otroUsuarioId, Nombre = "Paciente Ajeno" };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync(dueno);
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Paciente>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
            .ReturnsAsync(paciente);

        // -- Test 1: Access patient not owned by you (should 403) --
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<LecturaSensor>>(),
                It.IsAny<FilterDefinition<LecturaSensor>>(),
                It.IsAny<SortDefinition<LecturaSensor>>(),
                It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<LecturaSensor>());

        var lecturasResponse = await _client.GetAsync($"/api/Sensores/lecturas/{pacienteId}");
        lecturasResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // -- Test 2: Admin endpoint is admin-only (dueno debe recibir 403) --
        var metricasResponse = await _client.GetAsync("/api/Admin/metricas");
        metricasResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // -- Test 2b: Token de administrador sí accede a metricas --
        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<Plan>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
            .ReturnsAsync(new List<Plan>
            {
                new() { Id = "plan_free", Nombre = "Gratis", Activo = true }
            });

        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateToken("admin_seg", "administrador"));
        var metricasAdminResponse = await _client.GetAsync("/api/Admin/metricas");
        metricasAdminResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- Test 3: Unauthenticated access (should 401) --
        _client.DefaultRequestHeaders.Clear();
        var sinAuthResponse = await _client.GetAsync("/api/Pacientes/mi-paciente");
        sinAuthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // -- Test 4: Invalid email in by-email (non-existent) --
        _client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokenHelper.GenerateDuenoToken(duenoId));

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<UsuarioWeb>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
            .ReturnsAsync((UsuarioWeb?)null);

        var emailResponse = await _client.GetAsync("/api/UsuariosWeb/by-email/noexiste@test.com");
        emailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emailJson = await emailResponse.Content.ReadAsStringAsync();
        var emailDoc = JsonDocument.Parse(emailJson);
        emailDoc.RootElement.GetProperty("existe").GetBoolean().Should().BeFalse();

        // -- Test 5: Access medication with bad ID (should 404) --
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<Medicamento>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Medicamento, bool>>>()))
            .ReturnsAsync((Medicamento?)null);

        var medResponse = await _client.GetAsync("/api/Medicamentos/id_inexistente");
        medResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
