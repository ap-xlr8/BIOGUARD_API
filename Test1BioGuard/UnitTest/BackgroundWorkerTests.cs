using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using Xunit;
using FluentAssertions;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.Services;

namespace Test1BioGuard.UnitTest;

public class BackgroundWorkerTests
{
    private readonly Mock<IMongoDbContext> _mockDb;
    private readonly Mock<SensorService> _mockSensorService;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScope> _mockServiceScope;
    private readonly Mock<IServiceScopeFactory> _mockServiceScopeFactory;

    public BackgroundWorkerTests()
    {
        _mockDb = new Mock<IMongoDbContext>();
        
        var mockCollection = new Mock<IMongoCollection<EscalamientoPendiente>>();
        _mockDb.Setup(db => db.EscalamientosPendientes).Returns(mockCollection.Object);

        var mockIrmeService = new Mock<IRiesgoMetabolicoService>();
        var mockAlertaService = new Mock<AlertaService>(_mockDb.Object, new Mock<ILogger<AlertaService>>().Object);
        var mockNotificacionService = new Mock<NotificacionService>(_mockDb.Object, new Mock<ILogger<NotificacionService>>().Object);
        var mockFcmService = new Mock<IFCMService>();
        var mockMlService = new Mock<MLService>(_mockDb.Object, new Mock<ILogger<MLService>>().Object);
        var mockCripto = new Mock<CriptoService>(new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object, NullLogger<CriptoService>.Instance);
        
        _mockSensorService = new Mock<SensorService>(
            _mockDb.Object, 
            mockCripto.Object,
            new Mock<ILogger<SensorService>>().Object, 
            mockIrmeService.Object, 
            mockAlertaService.Object, 
            mockNotificacionService.Object, 
            mockFcmService.Object, 
            mockMlService.Object
        );

        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockServiceScope = new Mock<IServiceScope>();
        _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();

        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IMongoDbContext))).Returns(_mockDb.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(SensorService))).Returns(_mockSensorService.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory))).Returns(_mockServiceScopeFactory.Object);
        
        _mockServiceScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockServiceScopeFactory.Setup(sf => sf.CreateScope()).Returns(_mockServiceScope.Object);
    }

    [Fact]
    public async Task Service_Should_Process_Pending_Escalations()
    {
        var ahora = DateTime.UtcNow;
        var pendientes = new List<EscalamientoPendiente>
        {
            new() { Id = "65c3b123f456789abcde0001", PacienteId = "paciente1", AlertaId = "alerta1", FechaEjecucion = ahora.AddSeconds(-10), Procesado = false }
        };

        _mockDb.Setup(db => db.FindToListAsync(
            It.IsAny<IMongoCollection<EscalamientoPendiente>>(),
            It.IsAny<System.Linq.Expressions.Expression<Func<EscalamientoPendiente, bool>>>()
        )).ReturnsAsync(pendientes);

        _mockDb.Setup(db => db.EscalamientosPendientes.UpdateOneAsync(
            It.IsAny<FilterDefinition<EscalamientoPendiente>>(),
            It.IsAny<UpdateDefinition<EscalamientoPendiente>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()
        )).ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

        var logger = new Mock<ILogger<EscalamientoBackgroundService>>();
        var worker = new EscalamientoBackgroundService(_mockServiceProvider.Object, logger.Object);

        using var cts = new CancellationTokenSource();
        var startTask = worker.StartAsync(cts.Token);
        
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        _mockSensorService.Verify(s => s.EjecutarProtocoloEscalamientoAsync("paciente1", "alerta1"), Times.Once);
        _mockDb.Verify(db => db.EscalamientosPendientes.UpdateOneAsync(
            It.IsAny<FilterDefinition<EscalamientoPendiente>>(),
            It.IsAny<UpdateDefinition<EscalamientoPendiente>>(),
            It.IsAny<UpdateOptions>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }
}
