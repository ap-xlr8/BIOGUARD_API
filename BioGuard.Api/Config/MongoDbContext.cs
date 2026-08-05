using System.Linq.Expressions;
using MongoDB.Driver;
using BioGuard.Api.Models;

namespace BioGuard.Api.Config;

public class MongoDbConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}

public class MongoDbContext : IMongoDbContext
{
    private readonly IMongoDatabase _database = null!;

    public MongoDbContext(MongoDbConfig config)
    {
        var settings = MongoClientSettings.FromConnectionString(config.ConnectionString);
        settings.SslSettings = new SslSettings
        {
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
        };
        var client = new MongoClient(settings);
        _database = client.GetDatabase(config.DatabaseName);
    }

    protected MongoDbContext() { }

    public virtual IMongoDatabase Database => _database;
    public virtual IMongoCollection<Plan> Planes => _database.GetCollection<Plan>("planes");
    public virtual IMongoCollection<UsuarioWeb> UsuariosWeb => _database.GetCollection<UsuarioWeb>("usuarios_web");
    public virtual IMongoCollection<Paciente> Pacientes => _database.GetCollection<Paciente>("pacientes");
    public virtual IMongoCollection<Cuidador> Cuidadores => _database.GetCollection<Cuidador>("cuidadores");
    public virtual IMongoCollection<Dispositivo> Dispositivos => _database.GetCollection<Dispositivo>("dispositivos");
    public virtual IMongoCollection<LecturaSensor> LecturasSensores => _database.GetCollection<LecturaSensor>("lecturas_sensores");
    public virtual IMongoCollection<EventoMetabolico> EventosMetabolicos => _database.GetCollection<EventoMetabolico>("eventos_metabolicos");
    public virtual IMongoCollection<TrackingGps> TrackingGps => _database.GetCollection<TrackingGps>("tracking_gps");
    public virtual IMongoCollection<Notificacion> Notificaciones => _database.GetCollection<Notificacion>("notificaciones");
    public virtual IMongoCollection<Auditoria> Auditoria => _database.GetCollection<Auditoria>("auditoria");
    public virtual IMongoCollection<Pago> Pagos => _database.GetCollection<Pago>("pagos");
    public virtual IMongoCollection<PrediccionMl> PrediccionesMl => _database.GetCollection<PrediccionMl>("predicciones_ml");
    public virtual IMongoCollection<ModeloMl> ModelosMl => _database.GetCollection<ModeloMl>("modelos_ml");
    public virtual IMongoCollection<FcmToken> FcmTokens => _database.GetCollection<FcmToken>("fcm_tokens");
    public virtual IMongoCollection<RefreshToken> RefreshTokens => _database.GetCollection<RefreshToken>("refresh_tokens");
    public virtual IMongoCollection<Medicamento> Medicamentos => _database.GetCollection<Medicamento>("medicamentos");
    public virtual IMongoCollection<Alerta> Alertas => _database.GetCollection<Alerta>("alertas");
    public virtual IMongoCollection<TokenBlacklist> TokenBlacklist => _database.GetCollection<TokenBlacklist>("token_blacklist");
    public virtual IMongoCollection<DeviceSession> DeviceSessions => _database.GetCollection<DeviceSession>("device_sessions");
    public virtual IMongoCollection<ReporteCompartido> ReportesCompartidos => _database.GetCollection<ReporteCompartido>("reportes_compartidos");
    public virtual IMongoCollection<EventoProcesado> EventosProcesados => _database.GetCollection<EventoProcesado>("eventos_procesados");
    public virtual IMongoCollection<TicketSoporte> TicketsSoporte => _database.GetCollection<TicketSoporte>("tickets_soporte");
    public virtual IMongoCollection<EscalamientoPendiente> EscalamientosPendientes => _database.GetCollection<EscalamientoPendiente>("escalamientos_pendientes");

    public virtual async Task<T?> FindFirstOrDefaultAsync<T>(IMongoCollection<T> collection, Expression<Func<T, bool>> filter)
    {
        return await collection.Find(filter).FirstOrDefaultAsync();
    }

    public virtual async Task<List<T>> FindToListAsync<T>(IMongoCollection<T> collection, Expression<Func<T, bool>> filter)
    {
        return await collection.Find(filter).ToListAsync();
    }

    public virtual async Task<List<T>> FindToListAsync<T>(IMongoCollection<T> collection, FilterDefinition<T> filter, SortDefinition<T>? sort = null, int? limit = null, int? skip = null)
    {
        var fluent = collection.Find(filter);
        if (sort != null) fluent = fluent.Sort(sort);
        if (skip.HasValue) fluent = fluent.Skip(skip.Value);
        if (limit.HasValue) fluent = fluent.Limit(limit.Value);
        return await fluent.ToListAsync();
    }

    public virtual async Task<T?> FindFirstOrDefaultAsync<T>(IMongoCollection<T> collection, FilterDefinition<T> filter, SortDefinition<T>? sort = null)
    {
        var fluent = collection.Find(filter);
        if (sort != null) fluent = fluent.Sort(sort);
        return await fluent.FirstOrDefaultAsync();
    }

    public virtual async Task<DeleteResult> DeleteManyAsync<T>(IMongoCollection<T> collection, Expression<Func<T, bool>> filter)
    {
        return await collection.DeleteManyAsync(filter);
    }

    public virtual async Task<UpdateResult> UpdateOneAsync<T>(IMongoCollection<T> collection, Expression<Func<T, bool>> filter, UpdateDefinition<T> update)
    {
        return await collection.UpdateOneAsync(filter, update);
    }

    public virtual async Task<long> CountDocumentsAsync<T>(IMongoCollection<T> collection, Expression<Func<T, bool>> filter)
    {
        return await collection.CountDocumentsAsync(filter);
    }
}
