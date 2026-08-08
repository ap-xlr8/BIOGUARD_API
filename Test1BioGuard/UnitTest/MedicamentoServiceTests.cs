using MongoDB.Driver;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using BioGuard.Api.Config;
using BioGuard.Api.Services;
using BioGuard.Api.Models;
using FluentAssertions;

namespace Test1BioGuard.UnitTest;

public class MedicamentoServiceTests
{
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly MedicamentoService _service;
    private readonly Mock<IMongoCollection<Medicamento>> _mockCollection;

    public MedicamentoServiceTests()
    {
        _mockDb = new Mock<IMongoDbContext>();
        _mockCollection = new Mock<IMongoCollection<Medicamento>>();
        _mockDb.Setup(db => db.Medicamentos).Returns(_mockCollection.Object);
        var mockLogger = new Mock<ILogger<MedicamentoService>>();
        var mockCripto = new Mock<CriptoService>(new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object, NullLogger<CriptoService>.Instance);
        mockCripto.Setup(c => c.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        mockCripto.Setup(c => c.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        _service = new MedicamentoService(_mockDb.Object, mockCripto.Object, mockLogger.Object);
    }

    [Fact]
    public async Task ObtenerPorPacienteAsync_ConMedicamentos_RetornaLista()
    {
        var medicamentos = new List<Medicamento>
        {
            new() { Id = "m1", PacienteId = "pac1", Nombre = "Metformina", Dosis = "500mg", Horario = "8:00" },
            new() { Id = "m2", PacienteId = "pac1", Nombre = "Insulina", Dosis = "10u", Horario = "12:00" }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                _mockCollection.Object,
                It.IsAny<FilterDefinition<Medicamento>>(),
                It.IsAny<SortDefinition<Medicamento>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(medicamentos);

        var result = await _service.ObtenerPorPacienteAsync("pac1");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_MedicamentoExiste_RetornaMedicamento()
    {
        var medicamento = new Medicamento
        {
            Id = "m1", PacienteId = "pac1", Nombre = "Metformina",
            Dosis = "500mg", Horario = "8:00", Activo = true
        };

        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                _mockCollection.Object,
                It.IsAny<System.Linq.Expressions.Expression<Func<Medicamento, bool>>>()))
            .ReturnsAsync(medicamento);

        var result = await _service.ObtenerPorIdAsync("m1");

        result.Should().NotBeNull();
        result!.Nombre.Should().Be("Metformina");
    }

    [Fact]
    public async Task ObtenerPorIdAsync_MedicamentoNoExiste_RetornaNull()
    {
        _mockDb.Setup(db => db.FindFirstOrDefaultAsync(
                _mockCollection.Object,
                It.IsAny<System.Linq.Expressions.Expression<Func<Medicamento, bool>>>()))
            .ReturnsAsync((Medicamento?)null);

        var result = await _service.ObtenerPorIdAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CrearAsync_DatosValidos_RetornaMedicamento()
    {
        _mockCollection.Setup(c => c.InsertOneAsync(
            It.IsAny<Medicamento>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _service.CrearAsync("pac1", "Metformina", "500mg", "8:00", "Tomar con comida");

        result.Should().NotBeNull();
        result.Nombre.Should().Be("Metformina");
        result.Dosis.Should().Be("500mg");
        result.Horario.Should().Be("8:00");
        result.Notas.Should().Be("Tomar con comida");
        result.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task ActualizarAsync_DatosValidos_RetornaTrue()
    {
        var mockResult = new Mock<UpdateResult>();
        mockResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockCollection.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<Medicamento>>(),
            It.IsAny<UpdateDefinition<Medicamento>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        var result = await _service.ActualizarAsync("m1", "Metformina XR", "1000mg", "20:00", "Nueva nota");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RegistrarTomaAsync_MedicamentoExiste_RetornaTrue()
    {
        var mockResult = new Mock<UpdateResult>();
        mockResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockCollection.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<Medicamento>>(),
            It.IsAny<UpdateDefinition<Medicamento>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        var result = await _service.RegistrarTomaAsync("m1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ActivarAsync_ActivarMedicamento_RetornaTrue()
    {
        var mockResult = new Mock<UpdateResult>();
        mockResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockCollection.Setup(c => c.UpdateOneAsync(
            It.IsAny<FilterDefinition<Medicamento>>(),
            It.IsAny<UpdateDefinition<Medicamento>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        var result = await _service.ActivarAsync("m1", true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task EliminarAsync_MedicamentoExiste_RetornaTrue()
    {
        var mockResult = new Mock<DeleteResult>();
        mockResult.Setup(r => r.DeletedCount).Returns(1);

        _mockCollection.Setup(c => c.DeleteOneAsync(
            It.IsAny<FilterDefinition<Medicamento>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult.Object);

        var result = await _service.EliminarAsync("m1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task EliminarPorPacienteAsync_DatosValidos_RetornaTrue()
    {
        var mockResult = new Mock<DeleteResult>();
        mockResult.Setup(r => r.DeletedCount).Returns(3);

        _mockDb.Setup(db => db.DeleteManyAsync(
                It.IsAny<IMongoCollection<Medicamento>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<Medicamento, bool>>>()))
            .ReturnsAsync(mockResult.Object);

        var result = await _service.EliminarPorPacienteAsync("pac1");

        result.Should().BeTrue();
    }
}
