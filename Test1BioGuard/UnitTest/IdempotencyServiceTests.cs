using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.Services;
using MongoDB.Driver;
using Moq;

namespace Test1BioGuard.UnitTest;

public class IdempotencyServiceTests
{
    [Fact]
    public async Task TryAcquire_NewSource_PersistsProcessingLease()
    {
        var context = new Mock<IMongoDbContext>();
        var collection = new Mock<IMongoCollection<EventoProcesado>>();
        context.Setup(db => db.EventosProcesados).Returns(collection.Object);
        collection.Setup(db => db.InsertOneAsync(
                It.IsAny<EventoProcesado>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new IdempotencyService(context.Object);

        var status = await service.TryAcquireAsync("sensor-reading", "patient-1", "source-1");

        Assert.Equal(IdempotencyLeaseStatus.Acquired, status);
        collection.Verify(db => db.InsertOneAsync(
            It.Is<EventoProcesado>(item =>
                item.Id.StartsWith("idem:") && item.Estado == "processing"),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void BuildKey_IsDeterministicAndScoped()
    {
        var first = IdempotencyService.BuildKey("sensor-reading", "patient-1", "source-1");
        var repeated = IdempotencyService.BuildKey("sensor-reading", "patient-1", "source-1");
        var otherPatient = IdempotencyService.BuildKey("sensor-reading", "patient-2", "source-1");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, otherPatient);
        Assert.Equal(69, first.Length);
    }

    [Fact]
    public async Task TryAcquire_MissingSource_DoesNotTouchLedger()
    {
        var context = new Mock<IMongoDbContext>(MockBehavior.Strict);
        var service = new IdempotencyService(context.Object);

        var status = await service.TryAcquireAsync("sensor-reading", "patient-1", null);

        Assert.Equal(IdempotencyLeaseStatus.Acquired, status);
        context.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FindPersistedResult_RecoversReadingAfterInterruptedCompletion()
    {
        var context = new Mock<IMongoDbContext>();
        var readings = new Mock<IMongoCollection<LecturaSensor>>();
        context.Setup(db => db.LecturasSensores).Returns(readings.Object);
        context.Setup(db => db.FindFirstOrDefaultAsync(
                readings.Object,
                It.IsAny<System.Linq.Expressions.Expression<Func<LecturaSensor, bool>>>()))
            .ReturnsAsync(new LecturaSensor { Id = "reading-1", SourceMessageId = "source-1" });
        var service = new IdempotencyService(context.Object);

        var resultId = await service.FindPersistedResultAsync(
            "sensor-reading", "patient-1", "source-1");

        Assert.Equal("reading-1", resultId);
    }

    [Fact]
    public async Task FindPersistedResult_UnknownOperationDoesNotQueryTelemetry()
    {
        var context = new Mock<IMongoDbContext>(MockBehavior.Strict);
        var service = new IdempotencyService(context.Object);

        var resultId = await service.FindPersistedResultAsync(
            "unknown", "patient-1", "source-1");

        Assert.Null(resultId);
        context.VerifyNoOtherCalls();
    }
}
