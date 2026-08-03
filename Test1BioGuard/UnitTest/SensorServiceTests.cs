using System.Reflection;
using MongoDB.Driver;
using Moq;
using Microsoft.Extensions.Logging;
using BioGuard.Api.Config;
using BioGuard.Api.Services;
using BioGuard.Api.Models;
using FluentAssertions;

namespace Test1BioGuard.UnitTest;

public class SensorServiceTests
{
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly SensorService _service;
    private readonly Mock<IMongoCollection<LecturaSensor>> _mockLecturas;
    private readonly Mock<IMongoCollection<EventoMetabolico>> _mockEventos;
    private readonly Mock<IMongoCollection<TrackingGps>> _mockTracking;

    public SensorServiceTests()
    {
        _mockDb = new Mock<IMongoDbContext>();
        _mockLecturas = new Mock<IMongoCollection<LecturaSensor>>();
        _mockEventos = new Mock<IMongoCollection<EventoMetabolico>>();
        _mockTracking = new Mock<IMongoCollection<TrackingGps>>();

        _mockDb.Setup(db => db.LecturasSensores).Returns(_mockLecturas.Object);
        _mockDb.Setup(db => db.EventosMetabolicos).Returns(_mockEventos.Object);
        _mockDb.Setup(db => db.TrackingGps).Returns(_mockTracking.Object);

        var mockLogger = new Mock<ILogger<SensorService>>();
        var mockIrmeService = new Mock<IRiesgoMetabolicoService>();
        var mockAlertaService = new Mock<AlertaService>(_mockDb.Object, new Mock<ILogger<AlertaService>>().Object);
        var mockNotificacionService = new Mock<NotificacionService>(_mockDb.Object, new Mock<ILogger<NotificacionService>>().Object);
        var mockFcmService = new Mock<IFCMService>();
        var mockMlService = new Mock<MLService>(_mockDb.Object, new Mock<ILogger<MLService>>().Object);
        var mockCripto = new Mock<CriptoService>(new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object);
        mockCripto.Setup(c => c.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        mockCripto.Setup(c => c.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        
        _service = new SensorService(_mockDb.Object, mockCripto.Object, mockLogger.Object, mockIrmeService.Object, mockAlertaService.Object, mockNotificacionService.Object, mockFcmService.Object, mockMlService.Object);

        // Clear static cache between tests
        var cacheField = typeof(SensorService).GetField("_lecturaCache", BindingFlags.Static | BindingFlags.NonPublic);
        cacheField?.GetValue(null)?.GetType().GetMethod("Clear")?.Invoke(cacheField.GetValue(null), null);
    }

    [Fact]
    public async Task InsertarLecturaAsync_DatosValidos_RetornaLectura()
    {
        _mockLecturas.Setup(c => c.InsertOneAsync(
            It.IsAny<LecturaSensor>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var pacienteId = Guid.NewGuid().ToString();

        var resultNullable = await _service.InsertarLecturaAsync(
            pacienteId, "AA:BB:CC:DD:EE:FF", 72, 36.5, 2.5, null, null);

        resultNullable.Should().NotBeNull();
        var (result, _, _) = resultNullable!.Value;
        result.PulsoBpm.Should().Be(72);
        result.TemperaturaC.Should().Be(36.5);
        result.SudoracionGsr.Should().Be(2.5);
        result.Meta.PacienteId.Should().Be(pacienteId);
        result.Meta.DispositivoMac.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public async Task CrearEventoAsync_NivelCritico_RetornaEvento()
    {
        _mockEventos.Setup(c => c.InsertOneAsync(
            It.IsAny<EventoMetabolico>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _service.CrearEventoAsync("123456789012345678901234", 0.95, "Critico", "Pico de glucosa detectado");

        result.Should().NotBeNull();
        result.NivelRiesgo.Should().Be("Crítico");
        result.ProbabilidadMl.Should().Be(0.95);
        result.Descripcion.Should().Be("Pico de glucosa detectado");
        result.Atendida.Should().BeFalse();
    }

    [Fact]
    public async Task AtenderEventoAsync_EventoExiste_RetornaTrue()
    {
        var mockResult = new Mock<UpdateResult>();
        mockResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockEventos.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<EventoMetabolico>>(),
            It.IsAny<UpdateDefinition<EventoMetabolico>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        var result = await _service.AtenderEventoAsync("123456789012345678901234", "cuidador123");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task InsertarTrackingAsync_Emergencia_GuardaCorrectamente()
    {
        _mockTracking.Setup(c => c.InsertOneAsync(
            It.IsAny<TrackingGps>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.InsertarTrackingAsync("123456789012345678901234", "AA:BB:CC:DD:EE:FF", -99.1332, 19.4326, true);

        _mockTracking.Verify(c => c.InsertOneAsync(
            It.Is<TrackingGps>(t =>
                t.Meta.PacienteId == "123456789012345678901234" &&
                t.UbicacionCifrada == "-99.1332,19.4326" &&
                t.EsEmergencia == true),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObtenerLecturasAsync_ConDatos_RetornaLista()
    {
        var lecturas = new List<LecturaSensor>
        {
            new() { PulsoBpm = 72, Timestamp = DateTime.UtcNow },
            new() { PulsoBpm = 85, Timestamp = DateTime.UtcNow.AddMinutes(-10) }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                _mockLecturas.Object,
                It.IsAny<FilterDefinition<LecturaSensor>>(),
                It.IsAny<SortDefinition<LecturaSensor>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(lecturas);

        var result = await _service.ObtenerLecturasAsync("123456789012345678901234", 100);

        result.Should().NotBeEmpty();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ObtenerLecturasRangoAsync_ConLecturas_RetornaLista()
    {
        var lecturas = new List<LecturaSensor>
        {
            new() { PulsoBpm = 72, Timestamp = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc) }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                _mockLecturas.Object,
                It.IsAny<FilterDefinition<LecturaSensor>>(),
                It.IsAny<SortDefinition<LecturaSensor>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(lecturas);

        var result = await _service.ObtenerLecturasRangoAsync(
            "123456789012345678901234",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ObtenerEventosAsync_ConEventos_RetornaLista()
    {
        var eventos = new List<EventoMetabolico>
        {
            new() { Id = "e1", PacienteId = "123456789012345678901234", NivelRiesgo = "Critico", ProbabilidadMl = 0.95, FechaEvento = DateTime.UtcNow },
            new() { Id = "e2", PacienteId = "123456789012345678901234", NivelRiesgo = "Pre-Pico", ProbabilidadMl = 0.7, FechaEvento = DateTime.UtcNow.AddMinutes(-10) }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                _mockEventos.Object,
                It.IsAny<FilterDefinition<EventoMetabolico>>(),
                It.IsAny<SortDefinition<EventoMetabolico>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(eventos);

        var result = await _service.ObtenerEventosAsync("123456789012345678901234", 50);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ObtenerEventosAsync_SinEventos_RetornaListaVacia()
    {
        _mockDb.Setup(db => db.FindToListAsync(
                _mockEventos.Object,
                It.IsAny<FilterDefinition<EventoMetabolico>>(),
                It.IsAny<SortDefinition<EventoMetabolico>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(new List<EventoMetabolico>());

        var result = await _service.ObtenerEventosAsync("nonexistent", 50);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AgregarAccionAsync_EventoExiste_RetornaTrue()
    {
        var evento = new EventoMetabolico
        {
            Id = "e1", PacienteId = "123456789012345678901234",
            AccionesTomadas = ""
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                _mockEventos.Object,
                It.IsAny<System.Linq.Expressions.Expression<Func<EventoMetabolico, bool>>>()))
            .ReturnsAsync(evento);

        var mockResult = new Mock<UpdateResult>();
        mockResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockEventos.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<EventoMetabolico>>(),
                It.IsAny<UpdateDefinition<EventoMetabolico>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        var result = await _service.AgregarAccionAsync("e1", "Medicina administrada");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task AgregarAccionAsync_EventoNoExiste_RetornaFalse()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                _mockEventos.Object,
                It.IsAny<System.Linq.Expressions.Expression<Func<EventoMetabolico, bool>>>()))
            .ReturnsAsync((EventoMetabolico?)null);

        var result = await _service.AgregarAccionAsync("nonexistent", "Accion");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ObtenerTrackingAsync_ConDatos_RetornaLista()
    {
        var tracking = new List<TrackingGps>
        {
            new()
            {
                Id = "t1", Meta = new MetaData { PacienteId = "123456789012345678901234" },
                Timestamp = DateTime.UtcNow,
                Ubicacion = new UbicacionGps { Coordinates = new[] { -99.1, 19.4 } }
            }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                _mockTracking.Object,
                It.IsAny<FilterDefinition<TrackingGps>>(),
                It.IsAny<SortDefinition<TrackingGps>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(tracking);

        var result = await _service.ObtenerTrackingAsync("123456789012345678901234", 100);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ObtenerTrackingRangoAsync_ConDatos_RetornaLista()
    {
        var tracking = new List<TrackingGps>
        {
            new()
            {
                Id = "t1", Meta = new MetaData { PacienteId = "123456789012345678901234" },
                Timestamp = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
                Ubicacion = new UbicacionGps { Coordinates = new[] { -99.1, 19.4 } }
            }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                _mockTracking.Object,
                It.IsAny<FilterDefinition<TrackingGps>>(),
                It.IsAny<SortDefinition<TrackingGps>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(tracking);

        var result = await _service.ObtenerTrackingRangoAsync(
            "123456789012345678901234",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ObtenerUltimaUbicacionAsync_ConUbicacion_RetornaUbicacion()
    {
        var ubicacion = new TrackingGps
        {
            Id = "t1",
            Meta = new MetaData { PacienteId = "123456789012345678901234" },
            Timestamp = DateTime.UtcNow,
            Ubicacion = new UbicacionGps { Coordinates = new[] { -99.1, 19.4 } }
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                _mockTracking.Object,
                It.IsAny<FilterDefinition<TrackingGps>>(),
                It.IsAny<SortDefinition<TrackingGps>>()))
            .ReturnsAsync(ubicacion);

        var result = await _service.ObtenerUltimaUbicacionAsync("123456789012345678901234");

        result.Should().NotBeNull();
        result!.Ubicacion.Coordinates[0].Should().Be(-99.1);
    }

    [Fact]
    public async Task ObtenerUltimaUbicacionAsync_SinUbicacion_RetornaNull()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                _mockTracking.Object,
                It.IsAny<FilterDefinition<TrackingGps>>(),
                It.IsAny<SortDefinition<TrackingGps>>()))
            .ReturnsAsync((TrackingGps?)null);

        var result = await _service.ObtenerUltimaUbicacionAsync("nonexistent");

        result.Should().BeNull();
    }
}
