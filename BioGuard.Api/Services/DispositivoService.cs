using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;

namespace BioGuard.Api.Services;

public class DispositivoService
{
    private readonly IMongoDbContext _db;
    private readonly ILogger<DispositivoService> _logger;
    private static readonly Dictionary<string, DateTime> _heartbeatCache = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan HeartbeatMinInterval = TimeSpan.FromSeconds(60);

    public DispositivoService(IMongoDbContext db, ILogger<DispositivoService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Dispositivo?> VincularAsync(string pacienteId, string nombre, string macAddress)
    {
        _logger.LogInformation("Vinculando dispositivo para paciente {PacienteId}", pacienteId);
        var existente = await _db.FindFirstOrDefaultAsync(_db.Dispositivos, d => d.PacienteId == pacienteId);
        if (existente != null)
        {
            _logger.LogWarning("Paciente {PacienteId} ya tiene un dispositivo vinculado", pacienteId);
            return null;
        }

        var dispositivo = new Dispositivo
        {
            PacienteId = pacienteId,
            NombreDispositivo = nombre,
            MacAddress = macAddress,
            Conectado = true,
            FechaVinculacion = DateTime.UtcNow
        };

        await _db.Dispositivos.InsertOneAsync(dispositivo);
        _logger.LogInformation("Dispositivo vinculado con ID {DispositivoId} para paciente {PacienteId}", dispositivo.Id, pacienteId);
        return dispositivo;
    }

    public async Task<(bool success, bool rateLimited)> HeartbeatAsync(
        string pacienteId, int? bateria = null, List<string>? sensoresActivos = null)
    {
        lock (_cacheLock)
        {
            if (_heartbeatCache.TryGetValue(pacienteId, out var lastHeartbeat) &&
                DateTime.UtcNow - lastHeartbeat < HeartbeatMinInterval)
            {
                _logger.LogDebug("Heartbeat rate limited for paciente {PacienteId}", pacienteId);
                return (true, true);
            }
            _heartbeatCache[pacienteId] = DateTime.UtcNow;
        }

        _logger.LogInformation("Heartbeat recibido para paciente {PacienteId}", pacienteId);
        var update = Builders<Dispositivo>.Update
            .Set(d => d.Conectado, true)
            .Set(d => d.UltimaSincronizacion, DateTime.UtcNow);

        if (bateria.HasValue)
            update = update.Set(d => d.Bateria, bateria.Value);
        if (sensoresActivos != null)
            update = update.Set(d => d.SensoresDisponibles, sensoresActivos);

        var result = await _db.Dispositivos.UpdateOneAsync(d => d.PacienteId == pacienteId, update);
        if (result.ModifiedCount == 0)
            _logger.LogWarning("No se encontró dispositivo para paciente {PacienteId}", pacienteId);
        return (result.ModifiedCount > 0, false);
    }

    public async Task<Dispositivo?> ObtenerPorPacienteAsync(string pacienteId)
    {
        _logger.LogInformation("Buscando dispositivo para paciente {PacienteId}", pacienteId);
        return await _db.FindFirstOrDefaultAsync(_db.Dispositivos, d => d.PacienteId == pacienteId);
    }

    public async Task<Dispositivo?> ObtenerPorIdAsync(string id)
    {
        return await _db.FindFirstOrDefaultAsync(_db.Dispositivos, d => d.Id == id);
    }

    public async Task<bool> ActualizarAsync(string id, string nombre)
    {
        _logger.LogInformation("Actualizando dispositivo {DispositivoId}", id);
        var update = Builders<Dispositivo>.Update.Set(d => d.NombreDispositivo, nombre);
        var result = await _db.Dispositivos.UpdateOneAsync(d => d.Id == id, update);
        if (result.ModifiedCount == 0)
            _logger.LogWarning("Dispositivo no encontrado para actualizar: {DispositivoId}", id);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> EliminarAsync(string id)
    {
        _logger.LogInformation("Eliminando dispositivo {DispositivoId}", id);
        var result = await _db.Dispositivos.DeleteOneAsync(d => d.Id == id);
        if (result.DeletedCount == 0)
            _logger.LogWarning("Dispositivo no encontrado para eliminar: {DispositivoId}", id);
        return result.DeletedCount > 0;
    }

    public async Task<object?> ObtenerInfoCompletaAsync(string pacienteId)
    {
        var dispositivo = await ObtenerPorPacienteAsync(pacienteId);
        if (dispositivo == null) return null;

        var deviceSession = await _db.FindFirstOrDefaultAsync(_db.DeviceSessions, s =>
            s.UsuarioId == pacienteId && s.Activa);

        return new
        {
            Reloj = new
            {
                Modelo = dispositivo.NombreDispositivo,
                Conectado = dispositivo.Conectado,
                Bateria = dispositivo.Bateria,
                UltimaSincronizacion = dispositivo.UltimaSincronizacion,
                SensoresDisponibles = dispositivo.SensoresDisponibles ?? new List<string>()
            },
            Telefono = deviceSession == null ? null : new
            {
                Modelo = deviceSession.ModeloDispositivo ?? "Desconocido",
                SistemaOperativo = deviceSession.SistemaOperativo ?? "Desconocido",
                Bateria = deviceSession.Bateria,
                AhorroEnergia = deviceSession.AhorroEnergia,
                Conectividad = deviceSession.Conectividad ?? "desconocida"
            }
        };
    }

    public async Task RegistrarSesionTelefonoAsync(
        string pacienteId, string modelo, string so, int? bateria, 
        bool ahorroEnergia, string? conectividad, string ip, string userAgent)
    {
        _logger.LogInformation("Registrando sesión de teléfono para paciente {PacienteId}", pacienteId);
        
        // Desactivar sesiones anteriores
        var updateDesactivar = Builders<DeviceSession>.Update.Set(s => s.Activa, false);
        await _db.DeviceSessions.UpdateManyAsync(s => s.UsuarioId == pacienteId && s.Activa, updateDesactivar);

        var sesion = new DeviceSession
        {
            UsuarioId = pacienteId,
            Rol = "paciente",
            ModeloDispositivo = modelo,
            SistemaOperativo = so,
            Bateria = bateria,
            AhorroEnergia = ahorroEnergia,
            Conectividad = conectividad,
            Ip = ip,
            UserAgent = userAgent,
            UltimoAcceso = DateTime.UtcNow,
            Activa = true
        };

        await _db.DeviceSessions.InsertOneAsync(sesion);
        _logger.LogInformation("Sesión de teléfono registrada con ID {SesionId} para paciente {PacienteId}", sesion.Id, pacienteId);
    }
}
