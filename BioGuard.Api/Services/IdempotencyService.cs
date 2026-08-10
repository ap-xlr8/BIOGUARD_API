using System.Security.Cryptography;
using System.Text;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using MongoDB.Driver;

namespace BioGuard.Api.Services;

public enum IdempotencyLeaseStatus
{
    Acquired,
    Completed,
    InProgress
}

public sealed class IdempotencyService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly IMongoDbContext _db;

    public IdempotencyService(IMongoDbContext db)
    {
        _db = db;
    }

    public async Task<IdempotencyLeaseStatus> TryAcquireAsync(
        string operation,
        string patientId,
        string? sourceMessageId)
    {
        if (string.IsNullOrWhiteSpace(sourceMessageId))
            return IdempotencyLeaseStatus.Acquired;

        var key = BuildKey(operation, patientId, sourceMessageId);
        var now = DateTime.UtcNow;
        try
        {
            await _db.EventosProcesados.InsertOneAsync(new EventoProcesado
            {
                Id = key,
                Fecha = now,
                Estado = "processing"
            });
            return IdempotencyLeaseStatus.Acquired;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await _db.FindFirstOrDefaultAsync(
                _db.EventosProcesados,
                item => item.Id == key);
            if (existing?.Estado == "completed")
                return IdempotencyLeaseStatus.Completed;

            var staleBefore = now - LeaseDuration;
            var filter = Builders<EventoProcesado>.Filter.And(
                Builders<EventoProcesado>.Filter.Eq(item => item.Id, key),
                Builders<EventoProcesado>.Filter.Eq(item => item.Estado, "processing"),
                Builders<EventoProcesado>.Filter.Lt(item => item.Fecha, staleBefore));
            var update = Builders<EventoProcesado>.Update.Set(item => item.Fecha, now);
            var takeover = await _db.EventosProcesados.UpdateOneAsync(filter, update);
            if (takeover.ModifiedCount != 1) return IdempotencyLeaseStatus.InProgress;

            var recoveredResultId = await FindPersistedResultAsync(operation, patientId, sourceMessageId);
            if (recoveredResultId == null) return IdempotencyLeaseStatus.Acquired;

            await CompleteAsync(operation, patientId, sourceMessageId, recoveredResultId);
            return IdempotencyLeaseStatus.Completed;
        }
    }

    public async Task CompleteAsync(
        string operation,
        string patientId,
        string? sourceMessageId,
        string? resultId = null)
    {
        if (string.IsNullOrWhiteSpace(sourceMessageId)) return;

        var key = BuildKey(operation, patientId, sourceMessageId);
        var update = Builders<EventoProcesado>.Update
            .Set(item => item.Estado, "completed")
            .Set(item => item.ResultadoId, resultId)
            .Set(item => item.Fecha, DateTime.UtcNow);
        await _db.EventosProcesados.UpdateOneAsync(item => item.Id == key, update);
    }

    public async Task AbortAsync(string operation, string patientId, string? sourceMessageId)
    {
        if (string.IsNullOrWhiteSpace(sourceMessageId)) return;
        var key = BuildKey(operation, patientId, sourceMessageId);
        await _db.EventosProcesados.DeleteOneAsync(item => item.Id == key && item.Estado == "processing");
    }

    internal static string BuildKey(string operation, string patientId, string sourceMessageId)
    {
        var canonical = $"{operation.Trim()}\n{patientId.Trim()}\n{sourceMessageId.Trim()}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"idem:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    internal async Task<string?> FindPersistedResultAsync(
        string operation,
        string patientId,
        string sourceMessageId)
    {
        if (operation == "sensor-reading")
        {
            var reading = await _db.FindFirstOrDefaultAsync(
                _db.LecturasSensores,
                item => item.Meta.PacienteId == patientId && item.SourceMessageId == sourceMessageId);
            return reading?.Id;
        }

        if (operation == "gps-tracking")
        {
            var tracking = await _db.FindFirstOrDefaultAsync(
                _db.TrackingGps,
                item => item.Meta.PacienteId == patientId && item.SourceMessageId == sourceMessageId);
            return tracking?.Id;
        }

        return null;
    }
}
