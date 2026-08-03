using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;

namespace BioGuard.Api.Services;

public class RiesgoMetabolicoService : IRiesgoMetabolicoService
{
    private readonly IMongoDbContext _db;
    private readonly ILogger<RiesgoMetabolicoService> _logger;
    private readonly IRiesgoService _riesgoService;

    private const double W_FC_RELATIVA = 0.25;
    private const double W_HRV_INVERSA = 0.20;
    private const double W_TEMP_RELATIVA = 0.15;
    private const double W_REPOSO_POST_EVENTO = 0.15;
    private const double W_SUENO_RIESGO = 0.10;
    private const double W_HISTORIAL_PERSONAL = 0.10;
    private const double W_CONFIRMACION_USUARIO = 0.05;

    private const int SUEÑO_INICIO_HORA = 22;
    private const int SUEÑO_FIN_HORA = 6;
    private const int VENTANA_REPOSO_MINUTOS = 60;
    private const int VENTANA_HISTORIAL_DIAS = 30;
    private const int MIN_LECTURAS_BASELINE = 50;

    public RiesgoMetabolicoService(IMongoDbContext db, ILogger<RiesgoMetabolicoService> logger, IRiesgoService riesgoService)
    {
        _db = db;
        _logger = logger;
        _riesgoService = riesgoService;
    }

    public async Task<IrmeResult> CalculateAsync(string pacienteId, LecturaSensor lectura, bool isSleepTime = false)
    {
        var baseline = await GetOrCreateBaselineAsync(pacienteId);
        var components = await CalculateComponentsAsync(pacienteId, lectura, baseline, isSleepTime);

        var score = (int)Math.Round(
            W_FC_RELATIVA * components.FcRelativa +
            W_HRV_INVERSA * components.HrvInversa +
            W_TEMP_RELATIVA * components.TempRelativa +
            W_REPOSO_POST_EVENTO * components.ReposoPostEvento +
            W_SUENO_RIESGO * components.SuenoRiesgo +
            W_HISTORIAL_PERSONAL * components.HistorialPersonal +
            W_CONFIRMACION_USUARIO * components.ConfirmacionUsuario
        );

        score = Math.Clamp(score, 0, 100);

        var (nivelRiesgo, recomendacion, horasEstimadas) = GetInterpretation(score, isSleepTime);

        var modeloActivo = await _riesgoService.GetActiveModelVersionAsync();

        _logger.LogInformation("IRME calculated for patient {PacienteId}: Score={Score}, Nivel={Nivel}",
            pacienteId, score, nivelRiesgo);

        return new IrmeResult(score, nivelRiesgo, components, recomendacion, horasEstimadas, modeloActivo);
    }

    private async Task<IrmeComponents> CalculateComponentsAsync(
        string pacienteId,
        LecturaSensor lectura,
        PacienteBaseline baseline,
        bool isSleepTime)
    {
        var fcRelativa = baseline.FcPromedioReposo > 0
            ? Math.Max(0, (lectura.PulsoBpm - baseline.FcPromedioReposo) / baseline.FcPromedioReposo * 100)
            : 0;

        var hrvEstimada = EstimarHrv(lectura);
        var hrvInversa = baseline.HrvPromedio > 0
            ? Math.Max(0, (baseline.HrvPromedio - hrvEstimada) / baseline.HrvPromedio * 100)
            : 0;

        var tempRelativa = baseline.TempPromedio > 0
            ? Math.Abs(lectura.TemperaturaC - baseline.TempPromedio) / baseline.TempPromedio * 100
            : 0;

        var reposoPostEvento = await CalcularReposoPostEventoAsync(pacienteId, lectura.Timestamp);
        var suenoRiesgo = isSleepTime ? CalcularSuenoRiesgo(lectura, baseline) : 0;
        var historialPersonal = await CalcularHistorialPersonalAsync(pacienteId, lectura);
        var confirmacionUsuario = 0.0;

        return new IrmeComponents(
            FcRelativa: Math.Min(fcRelativa, 100),
            HrvInversa: Math.Min(hrvInversa, 100),
            TempRelativa: Math.Min(tempRelativa, 100),
            ReposoPostEvento: reposoPostEvento,
            SuenoRiesgo: suenoRiesgo,
            HistorialPersonal: historialPersonal,
            ConfirmacionUsuario: confirmacionUsuario
        );
    }

    private double EstimarHrv(LecturaSensor lectura)
    {
        var gsrNormalizado = Math.Min(lectura.SudoracionGsr / 10.0, 1.0);
        var fcVariabilidad = lectura.PulsoBpm > 0 ? 60000.0 / lectura.PulsoBpm : 1000;
        return fcVariabilidad * (1 - gsrNormalizado * 0.3);
    }

    private async Task<double> CalcularReposoPostEventoAsync(string pacienteId, DateTime timestamp)
    {
        var desde = timestamp.AddMinutes(-VENTANA_REPOSO_MINUTOS);
        var lecturas = await _db.FindToListAsync(_db.LecturasSensores,
            Builders<LecturaSensor>.Filter.And(
                Builders<LecturaSensor>.Filter.Eq(l => l.Meta.PacienteId, pacienteId),
                Builders<LecturaSensor>.Filter.Gte(l => l.Timestamp, desde),
                Builders<LecturaSensor>.Filter.Lte(l => l.Timestamp, timestamp)
            ),
            Builders<LecturaSensor>.Sort.Ascending(l => l.Timestamp));

        if (lecturas.Count < 2) return 0;

        var maxFc = lecturas.Max(l => l.PulsoBpm);
        var lecturasDespuesPico = lecturas.SkipWhile(l => l.PulsoBpm < maxFc * 0.9).ToList();
        if (lecturasDespuesPico.Count < 2) return 0;

        var fcEnReposo = lecturasDespuesPico.Average(l => l.PulsoBpm);
        var fcBase = lecturas.Take(lecturas.Count - lecturasDespuesPico.Count).DefaultIfEmpty().Average(l => l?.PulsoBpm ?? 0);

        if (fcBase == 0) return 0;

        var elevacionSostenida = (fcEnReposo - fcBase) / fcBase * 100;
        return Math.Max(0, Math.Min(elevacionSostenida, 100));
    }

    private double CalcularSuenoRiesgo(LecturaSensor lectura, PacienteBaseline baseline)
    {
        var riesgo = 0.0;

        if (baseline.FcPromedioReposo > 0)
        {
            var fcRatio = (double)lectura.PulsoBpm / baseline.FcPromedioReposo;
            if (fcRatio > 1.2) riesgo += (fcRatio - 1.2) * 50;
        }

        if (baseline.TempPromedio > 0)
        {
            var tempDiff = Math.Abs(lectura.TemperaturaC - baseline.TempPromedio);
            if (tempDiff > 0.5) riesgo += tempDiff * 20;
        }

        if (lectura.SudoracionGsr > 5) riesgo += lectura.SudoracionGsr * 5;

        return Math.Min(riesgo, 100);
    }

    private async Task<double> CalcularHistorialPersonalAsync(string pacienteId, LecturaSensor lecturaActual)
    {
        var desde = DateTime.UtcNow.AddDays(-VENTANA_HISTORIAL_DIAS);
        var eventos = await _db.FindToListAsync(_db.EventosMetabolicos,
            Builders<EventoMetabolico>.Filter.And(
                Builders<EventoMetabolico>.Filter.Eq(e => e.PacienteId, pacienteId),
                Builders<EventoMetabolico>.Filter.Gte(e => e.FechaEvento, desde)
            ),
            Builders<EventoMetabolico>.Sort.Descending(e => e.FechaEvento));

        if (eventos.Count == 0) return 0;

        var horaActual = lecturaActual.Timestamp.Hour;
        var eventosSimilares = eventos.Count(e =>
            Math.Abs(e.FechaEvento.Hour - horaActual) <= 2 &&
            e.ProbabilidadMl > 0.7);

        var ratio = (double)eventosSimilares / Math.Max(eventos.Count, 1);
        return Math.Min(ratio * 100, 100);
    }

    private (string nivel, string recomendacion, int horas) GetInterpretation(int score, bool isSleepTime)
    {
        return score switch
        {
            <= 24 => ("Bajo", "Estado estable. Sin recomendación urgente.", 24),
            <= 49 => ("Leve", "Cambios pequeños o aislados. Sugerir hidratación o movimiento ligero si inactivo.", 12),
            <= 69 => ("Moderado", "Patrón compatible con carga metabólica, estrés o mala recuperación. Notificación preventiva y solicitud de confirmación (síntoma, medicamento).", 6),
            <= 84 => ("Alto", "Varias señales alteradas durante reposo, sueño o actividad. Recomendar caminata, hidratación, revisión de medicación registrada.", 3),
            _ => ("Crítico", isSleepTime
                ? "Patrón nocturno o falta de respuesta del usuario; posible evento de seguridad. Activar Guardián Nocturno, alarma fuerte y contacto de emergencia si no responde."
                : "Patrón crítico detectado. Requiere atención inmediata.", 1)
        };
    }

    public async Task<PacienteBaseline> GetOrCreateBaselineAsync(string pacienteId)
    {
        var existing = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
        if (existing?.Biometria == null)
            return new PacienteBaseline(pacienteId, 70, 50, 36.5, 0, DateTime.UtcNow, new(), new(), new());

        var hace30Dias = DateTime.UtcNow.AddDays(-30);
        var lecturasReposo = await _db.FindToListAsync(_db.LecturasSensores,
            Builders<LecturaSensor>.Filter.And(
                Builders<LecturaSensor>.Filter.Eq(l => l.Meta.PacienteId, pacienteId),
                Builders<LecturaSensor>.Filter.Gte(l => l.Timestamp, hace30Dias),
                Builders<LecturaSensor>.Filter.Lte(l => l.PulsoBpm, 100)
            ),
            Builders<LecturaSensor>.Sort.Descending(l => l.Timestamp),
            200);

        if (lecturasReposo.Count < MIN_LECTURAS_BASELINE)
        {
            var edad = existing.Biometria.Edad;
            var fcEstimada = Math.Max(60, 220 - edad - 20);
            return new PacienteBaseline(pacienteId, fcEstimada, 50, 36.5, lecturasReposo.Count, DateTime.UtcNow, new(), new(), new());
        }

        var fcPromedio = lecturasReposo.Average(l => l.PulsoBpm);
        var hrvPromedio = lecturasReposo.Average(l => EstimarHrv(l));
        var tempPromedio = lecturasReposo.Average(l => l.TemperaturaC);

        var historialFc = lecturasReposo.Select(l => (double)l.PulsoBpm).ToList();
        var historialHrv = lecturasReposo.Select(l => EstimarHrv(l)).ToList();
        var historialTemp = lecturasReposo.Select(l => l.TemperaturaC).ToList();

        return new PacienteBaseline(pacienteId, fcPromedio, hrvPromedio, tempPromedio, lecturasReposo.Count, DateTime.UtcNow, historialFc, historialHrv, historialTemp);
    }

    public async Task<AlertTrigger?> CheckAlertTriggerAsync(string pacienteId, IrmeResult irmeResult, LecturaSensor? lectura = null)
    {
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
        var timezoneId = paciente?.ZonaHoraria ?? "America/Mexico_City";

        bool isSleepTime = false;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            isSleepTime = localTime.Hour >= SUEÑO_INICIO_HORA || localTime.Hour < SUEÑO_FIN_HORA;
        }
        catch
        {
            var localTime = DateTime.UtcNow.AddHours(-6);
            isSleepTime = localTime.Hour >= SUEÑO_INICIO_HORA || localTime.Hour < SUEÑO_FIN_HORA;
        }

        if (irmeResult.NivelRiesgo == "Crítico" && isSleepTime)
        {
            return new AlertTrigger(
                Tipo: "guardian_nocturno",
                Nivel: "critico",
                Titulo: "Guardián Nocturno Activado",
                Mensaje: "Patrón crítico detectado durante el sueño. Responda 'Estoy bien' en 30 segundos.",
                SensorData: new SensorData
                {
                    PulsoBpm = lectura?.PulsoBpm ?? (int)Math.Round(irmeResult.Components.FcRelativa),
                    TemperaturaC = lectura?.TemperaturaC ?? Math.Round(irmeResult.Components.TempRelativa, 1),
                    SudoracionGsr = lectura?.SudoracionGsr ?? (int)Math.Round(irmeResult.Components.HrvInversa),
                    ProbabilidadPico = irmeResult.Score / 100.0
                },
                EsCriticoNocturno: true
            );
        }

        if (irmeResult.NivelRiesgo is "Alto" or "Crítico")
        {
            return new AlertTrigger(
                Tipo: irmeResult.NivelRiesgo.ToLower(),
                Nivel: irmeResult.NivelRiesgo.ToLower(),
                Titulo: $"Riesgo {irmeResult.NivelRiesgo}",
                Mensaje: irmeResult.Recomendacion,
                SensorData: new SensorData
                {
                    PulsoBpm = lectura?.PulsoBpm ?? (int)Math.Round(irmeResult.Components.FcRelativa),
                    TemperaturaC = lectura?.TemperaturaC ?? Math.Round(irmeResult.Components.TempRelativa, 1),
                    SudoracionGsr = lectura?.SudoracionGsr ?? (int)Math.Round(irmeResult.Components.HrvInversa),
                    ProbabilidadPico = irmeResult.Score / 100.0
                },
                EsCriticoNocturno: false
            );
        }

        return null;
    }
}