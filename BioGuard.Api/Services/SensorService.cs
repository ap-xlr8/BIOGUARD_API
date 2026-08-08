using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using Microsoft.Extensions.Logging;

namespace BioGuard.Api.Services;

public class SensorService
{
    private readonly IMongoDbContext _db;
    private readonly CriptoService _cripto;
    private readonly ILogger<SensorService> _logger;
    private readonly IRiesgoMetabolicoService _irmeService;
    private readonly AlertaService _alertaService;
    private readonly NotificacionService _notificacionService;
    private readonly IFCMService _fcmService;
    private readonly MLService _mlService;

    public SensorService(IMongoDbContext db, CriptoService cripto, ILogger<SensorService> logger, 
        IRiesgoMetabolicoService irmeService, AlertaService alertaService, NotificacionService notificacionService,
        IFCMService fcmService, MLService mlService)
    {
        _db = db;
        _cripto = cripto;
        _logger = logger;
        _irmeService = irmeService;
        _alertaService = alertaService;
        _notificacionService = notificacionService;
        _fcmService = fcmService;
        _mlService = mlService;
    }

    private const int MaxResultadosRango = 5000;
    private static readonly Dictionary<string, DateTime> _lecturaCache = new();
    private static readonly object _lecturaCacheLock = new();
    private static readonly TimeSpan LecturaMinInterval = TimeSpan.FromSeconds(5);

    private static bool EsRateLimited(string pacienteId)
    {
        lock (_lecturaCacheLock)
        {
            if (_lecturaCache.TryGetValue(pacienteId, out var last) &&
                DateTime.UtcNow - last < LecturaMinInterval)
            {
                return true;
            }
            _lecturaCache[pacienteId] = DateTime.UtcNow;
            return false;
        }
    }

    public static string NormalizarNivelRiesgo(string nivel)
    {
        if (string.IsNullOrWhiteSpace(nivel)) return "Bajo";
        var lower = nivel.ToLowerInvariant();
        if (lower.Contains("crit") || lower.Contains("crít")) return "Crítico";
        if (lower.Contains("alto") || lower.Contains("high")) return "Alto";
        if (lower.Contains("mod") || lower.Contains("medium")) return "Moderado";
        if (lower.Contains("leve") || lower.Contains("low")) return "Leve";
        return "Bajo";
    }

    private static bool EsHorarioNocturno(DateTime dt, string? timezoneId = null)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId ?? "America/Mexico_City");
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(dt.ToUniversalTime(), tz);
            return localTime.Hour >= 22 || localTime.Hour < 6;
        }
        catch
        {
            var hora = dt.AddHours(-6); // Ajuste a hora CDMX de fallback
            return hora.Hour >= 22 || hora.Hour < 6;
        }
    }

    private static bool ValidarRangosFisiologicos(int pulsoBpm, double temperaturaC, double sudoracionGsr, int? spo2)
    {
        if (pulsoBpm < 30 || pulsoBpm > 250) return false;
        if (temperaturaC < 32 || temperaturaC > 43) return false;
        if (sudoracionGsr < 0 || sudoracionGsr > 50) return false;
        if (spo2.HasValue && (spo2 < 50 || spo2 > 100)) return false;
        return true;
    }

    private static bool ValidarTimestamp(DateTime timestamp, DateTime now)
    {
        var utc = timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
            : timestamp.ToUniversalTime();
        return utc <= now.AddMinutes(5) && utc >= now.AddDays(-30);
    }

    public async Task<(LecturaSensor lectura, double probabilidadPico, string? nivelRiesgo)?> InsertarLecturaAsync(
        string pacienteId, string dispositivoMac,
        int pulsoBpm, double temperaturaC, double sudoracionGsr,
        double? hrv, int? spo2, DateTime? timestamp = null, int diasHistorial = 30, bool bypassRateLimit = false, bool? esSimulado = null)
    {
        if (!ValidarRangosFisiologicos(pulsoBpm, temperaturaC, sudoracionGsr, spo2))
        {
            _logger.LogWarning("Lectura con valores fuera de rango para paciente {PacienteId}: pulso={Pulso}, temp={Temp}, gsr={Gsr}, spo2={Spo2}",
                pacienteId, pulsoBpm, temperaturaC, sudoracionGsr, spo2);
            return null;
        }

        if (!bypassRateLimit && EsRateLimited(pacienteId))
        {
            _logger.LogDebug("Lectura rate limited for paciente {PacienteId}", pacienteId);
            return null;
        }

        var now = DateTime.UtcNow;
        var lecturaTimestamp = timestamp ?? now;
        if (!ValidarTimestamp(lecturaTimestamp, now))
        {
            _logger.LogWarning("Lectura con timestamp fuera de ventana para paciente {PacienteId}: {Timestamp}",
                pacienteId, lecturaTimestamp);
            return null;
        }

        var lectura = new LecturaSensor
        {
            Meta = new MetaData
            {
                PacienteId = pacienteId,
                DispositivoMac = dispositivoMac
            },
            Timestamp = lecturaTimestamp,
            PulsoBpm = pulsoBpm,
            TemperaturaC = temperaturaC,
            SudoracionGsr = sudoracionGsr,
            Hrv = hrv,
            Spo2 = spo2,
            EsSimulado = esSimulado,
            ProbabilidadPico = 0,
            ExpireAt = lecturaTimestamp.AddDays(diasHistorial)
        };

        await _db.LecturasSensores.InsertOneAsync(lectura);
        _logger.LogInformation("Sensor reading inserted for patient: {PacienteId}", pacienteId);

        double probabilidadPico = 0;
        string? nivelRiesgo = null;

        // Calcular IRME y generar alertas si es necesario
        try
        {
            var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
            var isSleep = EsHorarioNocturno(lecturaTimestamp, paciente?.ZonaHoraria);
            var irmeResult = await _irmeService.CalculateAsync(pacienteId, lectura, isSleep);
            probabilidadPico = irmeResult.Score / 100.0;
            nivelRiesgo = irmeResult.NivelRiesgo;

            // Actualizar la lectura con el resultado del IRME
            var update = Builders<LecturaSensor>.Update
                .Set(l => l.ProbabilidadPico, probabilidadPico)
                .Set(l => l.NivelRiesgo, nivelRiesgo);
            await _db.LecturasSensores.UpdateOneAsync(l => l.Id == lectura.Id, update);

            // Crear EventoMetabolico automático si IRME >= 50
            if (irmeResult.Score >= 50)
            {
                await CrearEventoAsync(pacienteId, irmeResult.Score / 100.0, irmeResult.NivelRiesgo,
                    $"IRME {irmeResult.Score} - {irmeResult.Recomendacion}");
            }

            // Trigger ML medicamento SOS si IRME > 70
            if (irmeResult.Score > 70)
            {
                try
                {
                    var recoms = await _mlService.ObtenerRecomendacionesAsync(pacienteId);
                    if (recoms.Count > 0)
                    {
                        var alertaMl = new AlertTrigger(
                            Tipo: "ml_recomendacion",
                            Nivel: "preventivo",
                            Titulo: "Recomendación ML por IRME elevado",
                            Mensaje: string.Join(". ", recoms),
                            SensorData: new SensorData
                            {
                                PulsoBpm = lectura.PulsoBpm,
                                TemperaturaC = lectura.TemperaturaC,
                                SudoracionGsr = lectura.SudoracionGsr,
                                ProbabilidadPico = probabilidadPico
                            },
                             EsCriticoNocturno: EsHorarioNocturno(lecturaTimestamp, paciente?.ZonaHoraria)
                         );
                        await _notificacionService.CrearAsync(pacienteId, alertaMl.Titulo, alertaMl.Mensaje, "ml_recomendacion");
                    }
                }
                catch (Exception exMl)
                {
                    _logger.LogError(exMl, "Error obteniendo recomendaciones ML para paciente {PacienteId}", pacienteId);
                }
            }

            // Verificar triggers de alerta
            var alertTrigger = await _irmeService.CheckAlertTriggerAsync(pacienteId, irmeResult, lectura);
            if (alertTrigger != null)
            {
                await ProcesarAlertaAsync(pacienteId, alertTrigger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating IRME for patient {PacienteId}", pacienteId);
        }

        return (lectura, probabilidadPico, nivelRiesgo);
    }

    private async Task ProcesarAlertaAsync(string pacienteId, AlertTrigger trigger)
    {
        try
        {
            // Prevenir alertas duplicadas (mismo tipo, no atendida, en últimos 5 min)
            var duplicateFilter = Builders<Alerta>.Filter.And(
                Builders<Alerta>.Filter.Eq(a => a.PacienteId, pacienteId),
                Builders<Alerta>.Filter.Eq(a => a.Tipo, trigger.Tipo),
                Builders<Alerta>.Filter.Eq(a => a.Atendida, false),
                Builders<Alerta>.Filter.Gte(a => a.FechaCreacion, DateTime.UtcNow.AddMinutes(-5))
            );
            var existing = await _db.FindFirstOrDefaultAsync(_db.Alertas, duplicateFilter, null);
            if (existing != null)
            {
                _logger.LogDebug("Duplicate alert suppressed for patient {PacienteId}, type {Tipo}", pacienteId, trigger.Tipo);
                return;
            }

            // Crear alerta en BD
            var alerta = await _alertaService.CrearAsync(
                pacienteId, trigger.Tipo, trigger.Nivel, trigger.Titulo, trigger.Mensaje, trigger.SensorData);

            // Crear notificación para el paciente
            await _notificacionService.CrearAsync(pacienteId, trigger.Titulo, trigger.Mensaje, "alerta");

            // Enviar push FCM al paciente
            await EnviarPacientePushAsync(pacienteId, trigger.Titulo, trigger.Mensaje, trigger.EsCriticoNocturno);

            // Obtener cuidadores con acceso a alertas
            var cuidadores = await _db.FindToListAsync(_db.Cuidadores, c => c.PacienteId == pacienteId);
            var fcmTokensCuidador = new List<string>();

            foreach (var cuidador in cuidadores)
            {
                var puedeRecibirAlertas = cuidador.NivelAcceso switch
                {
                    "solo_alertas" => true,
                    "resumen_semanal" => trigger.EsCriticoNocturno,
                    "historial_completo" => true,
                    _ => false
                };

                if (puedeRecibirAlertas)
                {
                    if (!string.IsNullOrEmpty(cuidador.UsuarioWebId))
                    {
                        await _notificacionService.CrearAsync(
                            pacienteId, trigger.Titulo, trigger.Mensaje, "alerta", cuidador.UsuarioWebId);
                    }

                    var tokens = await _db.FindToListAsync(_db.FcmTokens, t => t.UsuarioId == cuidador.Id);
                    fcmTokensCuidador.AddRange(tokens.Select(t => t.Token));
                }
            }

            // Enviar push FCM a cuidadores
            if (fcmTokensCuidador.Count > 0)
            {
                await _fcmService.EnviarMulticastAsync(fcmTokensCuidador, trigger.Titulo, trigger.Mensaje,
                    new Dictionary<string, string> { ["paciente_id"] = pacienteId, ["tipo"] = "alerta" },
                    trigger.EsCriticoNocturno);
            }

            // Si es crítico nocturno, registrar protocolo de escalamiento pendiente persistente
            if (trigger.EsCriticoNocturno)
            {
                // El push de alta prioridad al paciente ya se envió arriba (EnviarPacientePushAsync
                // con EsCriticoNocturno=true). No reenviar para evitar notificación duplicada.
                var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
                var ventanaSegundos = paciente?.VentanaRespuestaSegundos > 0 ? paciente.VentanaRespuestaSegundos : 30;

                var escalamiento = new EscalamientoPendiente
                {
                    PacienteId = pacienteId,
                    AlertaId = alerta.Id,
                    FechaEjecucion = DateTime.UtcNow.AddSeconds(ventanaSegundos),
                    Procesado = false
                };
                await _db.EscalamientosPendientes.InsertOneAsync(escalamiento);
                _logger.LogInformation("Escalamiento nocturno programado en base de datos para paciente {PacienteId} en {Segundos} segundos", pacienteId, ventanaSegundos);
            }

            _logger.LogInformation("Alert processed for patient {PacienteId}: {Tipo} - {Nivel}", 
                pacienteId, trigger.Tipo, trigger.Nivel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing alert for patient {PacienteId}", pacienteId);
        }
    }

    private async Task EnviarPacientePushAsync(string pacienteId, string titulo, string mensaje, bool altaPrioridad)
    {
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
        if (paciente == null || string.IsNullOrEmpty(paciente.UsuarioWebId)) return;

        var tokens = await _db.FindToListAsync(_db.FcmTokens, t => t.UsuarioId == paciente.UsuarioWebId);
        foreach (var fcmToken in tokens)
        {
            await _fcmService.EnviarNotificacionAsync(fcmToken.Token, titulo, mensaje,
                new Dictionary<string, string> { ["paciente_id"] = pacienteId, ["tipo"] = "alerta" },
                altaPrioridad);
        }
    }

    public virtual async Task EjecutarProtocoloEscalamientoAsync(string pacienteId, string alertaId)
    {
        // Obtener información del paciente y contacto de emergencia
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
        if (paciente == null) return;

        var dueno = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == paciente.UsuarioWebId);
        if (dueno == null) return;

        // Verificar si la alerta fue atendida durante la ventana
        var alertaActual = await _db.FindFirstOrDefaultAsync(_db.Alertas, a => a.Id == alertaId);
        if (alertaActual != null && alertaActual.Atendida)
        {
            _logger.LogInformation("Paciente {PacienteId} respondió/atendió la alerta. Cancelando escalamiento.", pacienteId);
            return;
        }

        // Crear evento de emergencia
        var evento = await CrearEventoAsync(pacienteId, 0.95, "Crítico", 
            "Guardián Nocturno: Sin respuesta del paciente tras alerta crítica nocturna");

        // Notificar al dueño inmediatamente
        await _notificacionService.CrearAsync(
            pacienteId, "EMERGENCIA: Guardián Nocturno", 
            "El paciente no ha respondido a la alerta crítica. Se ha iniciado el protocolo de emergencia.", 
            "emergencia", null, dueno.Id);

        // Enviar push crítico al dueño
        var duenoTokens = await _db.FindToListAsync(_db.FcmTokens, t => t.UsuarioId == dueno.Id);
        foreach (var token in duenoTokens)
        {
            await _fcmService.EnviarNotificacionAsync(token.Token,
                "EMERGENCIA: Guardian Nocturno Activado",
                $"El paciente {paciente.Nombre} no respondio a la alerta critica nocturna.",
                new Dictionary<string, string> { ["paciente_id"] = pacienteId, ["tipo"] = "emergencia", ["alerta_id"] = alertaId },
                true);
        }

        _logger.LogWarning("GUARDIAN NOCTURNO ESCALADO - Paciente: {PacienteId}, Dueño: {DuenoId}", 
            pacienteId, dueno.Id);
    }

    public async Task<List<LecturaSensor>> ObtenerLecturasAsync(string pacienteId, int limite = 100)
    {
        var filter = Builders<LecturaSensor>.Filter.Eq(l => l.Meta.PacienteId, pacienteId);
        var sort = Builders<LecturaSensor>.Sort.Descending(l => l.Timestamp);
        return await _db.FindToListAsync(_db.LecturasSensores, filter, sort, limite);
    }

    public async Task<List<LecturaSensor>> ObtenerLecturasRangoAsync(
        string pacienteId, DateTime desde, DateTime hasta)
    {
        var filter = Builders<LecturaSensor>.Filter.And(
            Builders<LecturaSensor>.Filter.Eq(l => l.Meta.PacienteId, pacienteId),
            Builders<LecturaSensor>.Filter.Gte(l => l.Timestamp, desde),
            Builders<LecturaSensor>.Filter.Lte(l => l.Timestamp, hasta)
        );
        var sort = Builders<LecturaSensor>.Sort.Descending(l => l.Timestamp);
        return await _db.FindToListAsync(_db.LecturasSensores, filter, sort, limit: MaxResultadosRango);
    }

    private void DecryptEventoLocation(EventoMetabolico? e)
    {
        if (e == null) return;
        if (!string.IsNullOrEmpty(e.UbicacionCifrada))
        {
            var decrypted = _cripto.Decrypt(e.UbicacionCifrada);
            var parts = decrypted.Split(',');
            if (parts.Length == 2 && double.TryParse(parts[0], out var lon) && double.TryParse(parts[1], out var lat))
            {
                e.UbicacionGps = new UbicacionGps { Coordinates = new[] { lon, lat } };
            }
        }
    }

    public async Task<EventoMetabolico> CrearEventoAsync(string pacienteId, double probabilidad,
        string nivelRiesgo, string descripcion, Dictionary<string, double>? variablesOrigen = null,
        double? longitud = null, double? latitud = null)
    {
        string? ubicacionCifrada = null;
        if (longitud.HasValue && latitud.HasValue)
        {
            ubicacionCifrada = _cripto.Encrypt($"{longitud.Value},{latitud.Value}");
        }

        var nivelNormalizado = NormalizarNivelRiesgo(nivelRiesgo);

        var evento = new EventoMetabolico
        {
            PacienteId = pacienteId,
            ProbabilidadMl = probabilidad,
            NivelRiesgo = nivelNormalizado,
            Descripcion = descripcion,
            FechaEvento = DateTime.UtcNow,
            UbicacionGps = null,
            UbicacionCifrada = ubicacionCifrada,
            VariablesIrmE = variablesOrigen ?? new Dictionary<string, double>(),
            Atendida = false
        };

        await _db.EventosMetabolicos.InsertOneAsync(evento);
        _logger.LogInformation("Metabolic event created for patient: {PacienteId}", pacienteId);

        if (longitud.HasValue && latitud.HasValue)
        {
            evento.UbicacionGps = new UbicacionGps { Coordinates = new[] { longitud.Value, latitud.Value } };
        }
        return evento;
    }

    public async Task<List<EventoMetabolico>> ObtenerEventosAsync(string pacienteId, int limite = 50)
    {
        var filter = Builders<EventoMetabolico>.Filter.Eq(e => e.PacienteId, pacienteId);
        var sort = Builders<EventoMetabolico>.Sort.Descending(e => e.FechaEvento);
        var list = await _db.FindToListAsync(_db.EventosMetabolicos, filter, sort, limite);
        foreach (var e in list) DecryptEventoLocation(e);
        return list;
    }

    public async Task<EventoMetabolico?> ObtenerEventoPorIdAsync(string eventoId)
    {
        var e = await _db.FindFirstOrDefaultAsync(_db.EventosMetabolicos, ev => ev.Id == eventoId);
        DecryptEventoLocation(e);
        return e;
    }

    public async Task<bool> TieneEmergenciaActivaAsync(string pacienteId)
    {
        var ultima = await ObtenerUltimaUbicacionAsync(pacienteId);
        return ultima is { EsEmergencia: true };
    }

    public async Task<bool> AtenderEventoAsync(string eventoId, string cuidadorId)
    {
        var update = Builders<EventoMetabolico>.Update
            .Set(e => e.Atendida, true)
            .Set(e => e.AtendidoPorId, cuidadorId)
            .Set(e => e.FechaAtencion, DateTime.UtcNow);

        var result = await _db.EventosMetabolicos.UpdateOneAsync(e => e.Id == eventoId, update);
        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning("Event not found or already attended: {EventoId}", eventoId);
        }
        else
        {
            _logger.LogInformation("Event attended: {EventoId} by caregiver: {CuidadorId}", eventoId, cuidadorId);
        }
        return result.ModifiedCount > 0;
    }

    public async Task<bool> AgregarAccionAsync(string eventoId, string accion)
    {
        var evento = await _db.FindFirstOrDefaultAsync(_db.EventosMetabolicos, e => e.Id == eventoId);
        if (evento == null)
        {
            _logger.LogWarning("Add action to non-existent event: {EventoId}", eventoId);
            return false;
        }

        var nuevasAcciones = string.IsNullOrEmpty(evento.AccionesTomadas)
            ? accion
            : $"{evento.AccionesTomadas}; {accion}";

        var update = Builders<EventoMetabolico>.Update.Set(e => e.AccionesTomadas, nuevasAcciones);
        var result = await _db.EventosMetabolicos.UpdateOneAsync(e => e.Id == eventoId, update);
        return result.ModifiedCount > 0;
    }

    private void DecryptTrackingLocation(TrackingGps? t)
    {
        if (t == null) return;
        if (!string.IsNullOrEmpty(t.UbicacionCifrada))
        {
            var decrypted = _cripto.Decrypt(t.UbicacionCifrada);
            var parts = decrypted.Split(',');
            if (parts.Length == 2 && double.TryParse(parts[0], out var lon) && double.TryParse(parts[1], out var lat))
            {
                t.Ubicacion = new UbicacionGps { Coordinates = new[] { lon, lat } };
            }
        }
    }

    public async Task InsertarTrackingAsync(string pacienteId, string mac,
        double longitud, double latitud, bool esEmergencia)
    {
        var tracking = new TrackingGps
        {
            Meta = new MetaData { PacienteId = pacienteId, DispositivoMac = mac },
            Timestamp = DateTime.UtcNow,
            Ubicacion = new UbicacionGps(),
            UbicacionCifrada = _cripto.Encrypt($"{longitud},{latitud}"),
            EsEmergencia = esEmergencia
        };

        await _db.TrackingGps.InsertOneAsync(tracking);
        _logger.LogInformation("GPS tracking inserted for patient: {PacienteId}, emergency: {EsEmergencia}", pacienteId, esEmergencia);
    }

    public async Task<List<TrackingGps>> ObtenerTrackingAsync(string pacienteId, int limite = 100)
    {
        var filter = Builders<TrackingGps>.Filter.Eq(t => t.Meta.PacienteId, pacienteId);
        var sort = Builders<TrackingGps>.Sort.Descending(t => t.Timestamp);
        var list = await _db.FindToListAsync(_db.TrackingGps, filter, sort, limite);
        foreach (var t in list) DecryptTrackingLocation(t);
        return list;
    }

    public async Task<List<TrackingGps>> ObtenerTrackingRangoAsync(
        string pacienteId, DateTime desde, DateTime hasta)
    {
        var filter = Builders<TrackingGps>.Filter.And(
            Builders<TrackingGps>.Filter.Eq(t => t.Meta.PacienteId, pacienteId),
            Builders<TrackingGps>.Filter.Gte(t => t.Timestamp, desde),
            Builders<TrackingGps>.Filter.Lte(t => t.Timestamp, hasta)
        );
        var sort = Builders<TrackingGps>.Sort.Descending(t => t.Timestamp);
        var list = await _db.FindToListAsync(_db.TrackingGps, filter, sort, limit: MaxResultadosRango);
        foreach (var t in list) DecryptTrackingLocation(t);
        return list;
    }

    public async Task<TrackingGps?> ObtenerUltimaUbicacionAsync(string pacienteId)
    {
        var filter = Builders<TrackingGps>.Filter.Eq(t => t.Meta.PacienteId, pacienteId);
        var sort = Builders<TrackingGps>.Sort.Descending(t => t.Timestamp);
        var t = await _db.FindFirstOrDefaultAsync(_db.TrackingGps, filter, sort);
        DecryptTrackingLocation(t);
        return t;
    }
}
