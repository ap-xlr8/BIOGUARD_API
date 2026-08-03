using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Moq;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.DTOs;

namespace Test1BioGuard.IntegrationTests;

public class TicketsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly Mock<IMongoCollection<TicketSoporte>> _mockTickets;

    public TicketsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _mockDb = factory.MockDbContext;
        _mockTickets = new Mock<IMongoCollection<TicketSoporte>>();
        _mockDb.Setup(db => db.TicketsSoporte).Returns(_mockTickets.Object);
    }

    [Fact]
    public async Task Crear_TicketValido_Retorna200()
    {
        _mockTickets.Setup(c => c.InsertOneAsync(
            It.IsAny<TicketSoporte>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GenerateDuenoToken("user123"));

        var request = new CrearTicketRequest(
            Asunto: "Problema con el reloj",
            Descripcion: "No sincroniza las lecturas",
            Categoria: "problema_dispositivo",
            Prioridad: "alta"
        );

        var response = await _client.PostAsJsonAsync("/api/Tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Ticket creado exitosamente");
    }

    [Fact]
    public async Task ListarMisTickets_ConTickets_Retorna200()
    {
        var tickets = new List<TicketSoporte>
        {
            new() { Id = "t1", UsuarioId = "user123", Asunto = "Reloj", Descripcion = "No sync", Estado = "abierto" }
        };

        _mockDb.Setup(db => db.FindToListAsync(
                It.IsAny<IMongoCollection<TicketSoporte>>(),
                It.IsAny<FilterDefinition<TicketSoporte>>(),
                It.IsAny<SortDefinition<TicketSoporte>>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(tickets);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                TestTokenHelper.GenerateDuenoToken("user123"));

        var response = await _client.GetAsync("/api/Tickets/mis-tickets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }
}
