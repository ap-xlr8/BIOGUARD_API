using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;
using BioGuard.Api.Config;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BioGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SensoresController : ControllerBase
{
    private readonly SensorService _sensorService;
    private readonly PacienteService _pacienteService;
    private readonly IMongoDbContext _db;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<SensoresController> _logger;
    private readonly OwnershipHelper _ownershipHelper;
    private readonly IPlanLimiteService _planLimiteService;

    public SensoresController(SensorService sensorService, PacienteService pacienteService,
        IMongoDbContext db, AuditoriaService auditoriaService, ILogger<SensoresController> logger,
        OwnershipHelper ownershipHelper, IPlanLimiteService planLimiteService)
    {
        _sensorService = sensorService;
        _pacienteService = pacienteService;
        _db = db;
        _auditoriaService = auditoriaService;
        _logger = logger;
        _ownershipHelper = ownershipHelper;
        _planLimiteService = planLimiteService;
    }

    // Resuelve la MAC del dispositivo: prioriza la enviada por el cliente (request o header),
    // luego variable de entorno, y por último un identificador por paciente (no global).
    private string ResolverMac(string? macRequest, string pacienteId)
    {
        if (!string.IsNullOrWhiteSpace(macRequest)) return macRequest;
        var header = Request.Headers["X-Device-Mac"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header)) return header;
        return Environment.GetEnvironmentVariable("BIOMETRIC_MAC_ADDRESS")
            ?? $"dev-{pacienteId[..Math.Min(8, pacienteId.Length)]}";
    }

    // ── Lecturas (Envío de datos) ─────────────────────────────

    [HttpPost("lectura")]
    public async Task<IActionResult> RecibirLectura([FromBody] LecturaSensorRequest request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        _logger.LogInformation("Receiving sensor reading for paciente: {PacienteId}", pacienteId);
        var macAddress = ResolverMac(request.DispositivoMac, pacienteId);
        var result = await _sensorService.InsertarLecturaAsync(
            pacienteId, macAddress, request.PulsoBpm, request.TemperaturaC,
            request.SudoracionGsr, request.Hrv, request.Spo2, request.Timestamp);

        if (result == null)
            return BadRequest(new { message = "Lectura rechazada (rate limit o valores fuera de rango)" });

        var (lectura, probabilidadPico, nivelRiesgo) = result.Value;

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "insertar_lectura", "lecturas_sensores", lectura.Id, ip);

        return Ok(new
        {
            lecturaId = lectura.Id,
            probabilidadPico,
            nivelRiesgo,
            message = "Lectura recibida"
        });
    }

    [HttpPost("lectura-batch")]
    [HttpPost("lecturas")]
    [RequestSizeLimit(10485760)]
    public async Task<IActionResult> RecibirLecturaBatch([FromBody] List<LecturaSensorRequest> request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        if (request.Count > 500)
            return BadRequest(new { message = "Máximo 500 lecturas por lote" });

        _logger.LogInformation("Receiving batch of {Count} sensor readings for paciente: {PacienteId}", request.Count, pacienteId);
        var count = 0;
        foreach (var lectura in request)
        {
            var macAddress = ResolverMac(lectura.DispositivoMac, pacienteId);
            var result = await _sensorService.InsertarLecturaAsync(
                pacienteId, macAddress, lectura.PulsoBpm, lectura.TemperaturaC,
                lectura.SudoracionGsr, lectura.Hrv, lectura.Spo2, lectura.Timestamp,
                bypassRateLimit: true);
            if (result != null) count++;
        }

        return Ok(new { procesadas = count, message = "Lote procesado" });
    }

    // ── Lecturas (Consulta) ───────────────────────────────────

    [HttpGet("lecturas/{pacienteId}")]
    public async Task<IActionResult> ObtenerLecturas(string pacienteId, [FromQuery] int limite = 100)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching readings - user: {UserId}, paciente: {PacienteId}", usuarioId, pacienteId);
            return Forbid();
        }

        limite = Math.Min(limite, 1000);
        _logger.LogInformation("Fetching {Limite} readings for paciente: {PacienteId}", limite, pacienteId);
        var lecturas = await _sensorService.ObtenerLecturasAsync(pacienteId, limite);
        var response = lecturas.Select(l => new
        {
            l.Id,
            l.PulsoBpm,
            l.TemperaturaC,
            l.SudoracionGsr,
            l.ProbabilidadPico,
            l.Timestamp
        });
        return Ok(response);
    }

    [HttpGet("lecturas/{pacienteId}/rango")]
    public async Task<IActionResult> ObtenerLecturasRango(
        string pacienteId, [FromQuery] DateTime desde, [FromQuery] DateTime hasta)
    {
        if (desde > hasta)
            return BadRequest(new { message = "El parámetro 'desde' debe ser anterior o igual a 'hasta'" });

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching readings range - user: {UserId}, paciente: {PacienteId}", usuarioId, pacienteId);
            return Forbid();
        }

        var diasSolicitados = (int)(hasta - desde).TotalDays;
        var paciente = await _pacienteService.GetByIdAsync(pacienteId);
        if (paciente != null)
        {
            var planCheck = await _planLimiteService.VerificarDiasHistorialAsync(paciente.UsuarioWebId, diasSolicitados);
            if (!planCheck.Permitido)
                return BadRequest(new { message = planCheck.Motivo });
        }

        _logger.LogInformation("Fetching readings range for paciente: {PacienteId} from {Desde} to {Hasta}", pacienteId, desde, hasta);
        var lecturas = await _sensorService.ObtenerLecturasRangoAsync(pacienteId, desde, hasta);
        var response = lecturas.Select(l => new
        {
            l.Id,
            l.PulsoBpm,
            l.TemperaturaC,
            l.SudoracionGsr,
            l.ProbabilidadPico,
            l.Timestamp
        });
        return Ok(response);
    }

    // ── Estadísticas (Dashboard) ──────────────────────────────

    [HttpGet("estadisticas/{pacienteId}")]
    public async Task<IActionResult> Estadisticas(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching stats - user: {UserId}, paciente: {PacienteId}", usuarioId, pacienteId);
            return Forbid();
        }

        _logger.LogInformation("Fetching statistics for paciente: {PacienteId}", pacienteId);
        var lecturas = await _sensorService.ObtenerLecturasAsync(pacienteId, 100);
        if (!lecturas.Any())
        {
            _logger.LogWarning("No sensor data found for paciente: {PacienteId}", pacienteId);
            return Ok(new { message = "Sin datos" });
        }

        var ultima = lecturas.First();
        var estadoActual = ultima.NivelRiesgo ?? (ultima.ProbabilidadPico >= 0.85 ? "Crítico" : ultima.ProbabilidadPico >= 0.7 ? "Alto" : ultima.ProbabilidadPico >= 0.5 ? "Moderado" : ultima.ProbabilidadPico >= 0.25 ? "Leve" : "Bajo");

        return Ok(new
        {
            ultimoPulso = ultima.PulsoBpm,
            ultimaTemperatura = ultima.TemperaturaC,
            ultimaSudoracion = ultima.SudoracionGsr,
            promedioPulso = lecturas.Average(l => l.PulsoBpm),
            promedioTemperatura = lecturas.Average(l => l.TemperaturaC),
            estadoActual,
            totalLecturas = lecturas.Count
        });
    }

    [HttpGet("estadisticas/{pacienteId}/tendencia")]
    public async Task<IActionResult> Tendencia(string pacienteId, [FromQuery] string periodo = "diario")
    {
        var periodoValidos = new[] { "diario", "semanal", "mensual" };
        if (!periodoValidos.Contains(periodo))
            return BadRequest(new { message = "Periodo inválido. Use: diario, semanal o mensual" });

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching trend - user: {UserId}, paciente: {PacienteId}", usuarioId, pacienteId);
            return Forbid();
        }

        _logger.LogInformation("Fetching trend for paciente: {PacienteId}, period: {Periodo}", pacienteId, periodo);
        var desde = periodo switch
        {
            "semanal" => DateTime.UtcNow.AddDays(-7),
            "mensual" => DateTime.UtcNow.AddDays(-30),
            _ => DateTime.UtcNow.AddDays(-1)
        };

        var lecturas = await _sensorService.ObtenerLecturasRangoAsync(pacienteId, desde, DateTime.UtcNow);
        var response = lecturas.Select(l => new
        {
            l.Timestamp,
            l.PulsoBpm,
            l.TemperaturaC,
            l.ProbabilidadPico
        }).Reverse();

        return Ok(response);
    }

    // ── Eventos ───────────────────────────────────────────────

    [HttpPost("evento")]
    public async Task<IActionResult> CrearEvento([FromBody] CrearEventoRequest request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        _logger.LogInformation("Creating metabolic event for paciente: {PacienteId}, risk: {NivelRiesgo}", pacienteId, request.NivelRiesgo);
        var probabilidad = request.Probabilidad ?? request.ProbabilidadMl ?? 0.0;
        var nivelNormalizado = SensorService.NormalizarNivelRiesgo(request.NivelRiesgo);
        var evento = await _sensorService.CrearEventoAsync(
            pacienteId, probabilidad, nivelNormalizado, request.Descripcion,
            request.VariablesOrigen);

        var nivelesNotificables = new[] { "Moderado", "Alto", "Crítico" };
        if (Array.Exists(nivelesNotificables, n => n.Equals(nivelNormalizado, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                await _auditoriaService.RegistrarAsync(pacienteId, "evento_metabolico", "eventos_metabolicos", evento.Id, ip);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al notificar evento metabólico");
            }
        }

        return Ok(new { eventoId = evento.Id, message = "Evento creado" });
    }

    [HttpPost("evento-sos")]
    public async Task<IActionResult> CrearEventoSos([FromBody] CrearEventoRequest request)
    {
        return BadRequest(new { message = "Funcionalidad en mantenimiento. Usa el endpoint /evento en su lugar." });
    }

    [HttpGet("eventos/{pacienteId}")]
    public async Task<IActionResult> ObtenerEventos(string pacienteId, [FromQuery] int limite = 50)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching events - user: {UserId}, paciente: {PacienteId}", usuarioId, pacienteId);
            return Forbid();
        }

        _logger.LogInformation("Fetching {Limite} events for paciente: {PacienteId}", limite, pacienteId);
        var eventos = await _sensorService.ObtenerEventosAsync(pacienteId, limite);
        var response = eventos.Select(e => new EventoMetabolicoResponse(
            e.Id, e.NivelRiesgo, e.ProbabilidadMl, e.Descripcion,
            e.FechaEvento, e.Atendida));
        return Ok(response);
    }

    [HttpGet("eventos/{pacienteId}/resumen")]
    public async Task<IActionResult> ResumenEventos(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching event summary - user: {UserId}, paciente: {PacienteId}", usuarioId, pacienteId);
            return Forbid();
        }

        _logger.LogInformation("Fetching event summary for paciente: {PacienteId}", pacienteId);
        var eventos = await _sensorService.ObtenerEventosAsync(pacienteId, 100);
        return Ok(new
        {
            Total = eventos.Count,
            Criticos = eventos.Count(e => e.NivelRiesgo == "Crítico"),
            PrePico = eventos.Count(e => e.NivelRiesgo == "Pre-Pico" || e.NivelRiesgo == "Alto"),
            Normal = eventos.Count(e => e.NivelRiesgo is "Normal" or "Bajo" or "Leve"),
            Atendidos = eventos.Count(e => e.Atendida)
        });
    }

    [HttpPut("eventos/{eventoId}/atender")]
    public async Task<IActionResult> AtenderEvento(string eventoId, [FromBody] AtenderEventoRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var evento = await _sensorService.ObtenerEventoPorIdAsync(eventoId);
        if (evento == null) return NotFound();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(evento.PacienteId, usuarioId, role!))
            return Forbid();

        _logger.LogInformation("Marking event as attended: {EventoId}, cuidador: {CuidadorId}", eventoId, request.CuidadorId);
        var result = await _sensorService.AtenderEventoAsync(eventoId, request.CuidadorId);
        if (!result)
        {
            _logger.LogWarning("Event not found for attending: {EventoId}", eventoId);
            return NotFound();
        }
        return Ok(new { message = "Evento atendido" });
    }

    // ── Exportación ───────────────────────────────────────────

    [HttpGet("lecturas/{pacienteId}/exportar-pdf")]
    public async Task<IActionResult> ExportarPDF(string pacienteId,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
            return Forbid();

        if (role == "cuidador")
        {
            var nivelAcceso = User.FindFirst("nivel_acceso")?.Value;
            if (nivelAcceso != "historial_completo")
                return Forbid();
        }

        var hastaDt = hasta ?? DateTime.UtcNow;
        var desdeDt = desde ?? hastaDt.AddDays(-30);

        var difDias = (int)(hastaDt - desdeDt).TotalDays;
        if (difDias > 365)
            return BadRequest(new { message = "El rango máximo de exportación es 365 días" });

        var paciente = await _pacienteService.GetByIdAsync(pacienteId);
        if (paciente != null)
        {
            var planCheck = await _planLimiteService.VerificarDiasHistorialAsync(paciente.UsuarioWebId, difDias);
            if (!planCheck.Permitido)
                return BadRequest(new { message = planCheck.Motivo });
        }

        _logger.LogInformation("Exporting PDF for paciente: {PacienteId}", pacienteId);
        var lecturas = await _sensorService.ObtenerLecturasRangoAsync(pacienteId, desdeDt, hastaDt);

        var pdfBytes = GenerateLecturaPdf(lecturas, paciente?.Nombre ?? pacienteId, desdeDt, hastaDt);
        return File(pdfBytes, "application/pdf", $"lecturas_{pacienteId}_{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    private static byte[] GenerateLecturaPdf(List<BioGuard.Api.Models.LecturaSensor> lecturas, string pacienteNombre, DateTime desde, DateTime hasta)
    {
        using var ms = new MemoryStream();
        QuestPDF.Fluent.Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(40);
                page.Size(QuestPDF.Helpers.PageSizes.A4);

                page.Header().Element(c => c.Column(col =>
                {
                    col.Item().Text("BioGuard - Reporte de Lecturas").FontSize(18).Bold();
                    col.Item().Text($"Paciente: {pacienteNombre}").FontSize(12);
                    col.Item().Text($"Periodo: {desde:dd/MM/yyyy HH:mm} - {hasta:dd/MM/yyyy HH:mm}").FontSize(10);
                    col.Item().PaddingBottom(10);
                }));

                page.Content().Element(c => c.Column(col =>
                {
                    if (lecturas.Count == 0)
                    {
                        col.Item().Text("Sin lecturas en el periodo seleccionado.").FontSize(12);
                        return;
                    }

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(120);
                            cols.ConstantColumn(60);
                            cols.ConstantColumn(70);
                            cols.ConstantColumn(70);
                            cols.ConstantColumn(70);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background("#2563EB").Padding(5).Text("Timestamp").FontColor("#FFFFFF").FontSize(9).Bold();
                            header.Cell().Background("#2563EB").Padding(5).Text("Pulso (bpm)").FontColor("#FFFFFF").FontSize(9).Bold();
                            header.Cell().Background("#2563EB").Padding(5).Text("Temp (°C)").FontColor("#FFFFFF").FontSize(9).Bold();
                            header.Cell().Background("#2563EB").Padding(5).Text("GSR").FontColor("#FFFFFF").FontSize(9).Bold();
                            header.Cell().Background("#2563EB").Padding(5).Text("Probabilidad").FontColor("#FFFFFF").FontSize(9).Bold();
                        });

                        foreach (var l in lecturas.Take(500))
                        {
                            var bg = l.ProbabilidadPico >= 0.7 ? "#FEE2E2" :
                                     l.ProbabilidadPico >= 0.5 ? "#FEF3C7" : "#FFFFFF";
                            table.Cell().Background(bg).Padding(3).Text(l.Timestamp.ToString("dd/MM HH:mm")).FontSize(8);
                            table.Cell().Background(bg).Padding(3).Text(l.PulsoBpm.ToString()).FontSize(8);
                            table.Cell().Background(bg).Padding(3).Text(l.TemperaturaC.ToString("F1")).FontSize(8);
                            table.Cell().Background(bg).Padding(3).Text(l.SudoracionGsr.ToString("F1")).FontSize(8);
                            table.Cell().Background(bg).Padding(3).Text($"{l.ProbabilidadPico:P1}").FontSize(8);
                        }
                    });

                    if (lecturas.Count > 500)
                    {
                        col.Item().PaddingTop(5).Text($"Mostrando 500 de {lecturas.Count} lecturas.").FontSize(9).FontColor("#666666");
                    }

                    col.Item().PaddingTop(15).Table(summary =>
                    {
                        summary.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(120);
                            cols.ConstantColumn(80);
                            cols.ConstantColumn(80);
                        });

                        summary.Header(header =>
                        {
                            header.Cell().Background("#059669").Padding(5).Text("Métrica").FontColor("#FFFFFF").FontSize(9).Bold();
                            header.Cell().Background("#059669").Padding(5).Text("Promedio").FontColor("#FFFFFF").FontSize(9).Bold();
                            header.Cell().Background("#059669").Padding(5).Text("Rango").FontColor("#FFFFFF").FontSize(9).Bold();
                        });

                        var avgPulso = lecturas.Average(l => l.PulsoBpm);
                        var avgTemp = lecturas.Average(l => l.TemperaturaC);
                        var avgGsr = lecturas.Average(l => l.SudoracionGsr);
                        var minPulso = lecturas.Min(l => l.PulsoBpm);
                        var maxPulso = lecturas.Max(l => l.PulsoBpm);

                        summary.Cell().Padding(3).Text("Pulso").FontSize(8);
                        summary.Cell().Padding(3).Text($"{avgPulso:F0} bpm").FontSize(8);
                        summary.Cell().Padding(3).Text($"{minPulso}-{maxPulso} bpm").FontSize(8);

                        summary.Cell().Padding(3).Text("Temperatura").FontSize(8);
                        summary.Cell().Padding(3).Text($"{avgTemp:F1} °C").FontSize(8);
                        summary.Cell().Padding(3).Text($"{lecturas.Min(l => l.TemperaturaC):F1}-{lecturas.Max(l => l.TemperaturaC):F1} °C").FontSize(8);

                        summary.Cell().Padding(3).Text("GSR").FontSize(8);
                        summary.Cell().Padding(3).Text($"{avgGsr:F1}").FontSize(8);
                        summary.Cell().Padding(3).Text($"{lecturas.Min(l => l.SudoracionGsr):F1}-{lecturas.Max(l => l.SudoracionGsr):F1}").FontSize(8);

                        summary.Cell().Padding(3).Text("Total lecturas").FontSize(8);
                        summary.Cell().Padding(3).Text(lecturas.Count.ToString()).FontSize(8);
                        summary.Cell().Padding(3).Text("").FontSize(8);
                    });
                }));

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Generado por BioGuard - ").FontSize(8).FontColor("#999999");
                    t.Span($"{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC").FontSize(8).FontColor("#999999");
                });
            });
        }).GeneratePdf(ms);

        return ms.ToArray();
    }

    // ── Tracking GPS ──────────────────────────────────────────

    [HttpPost("tracking")]
    public async Task<IActionResult> InsertarTracking([FromBody] TrackingGpsRequest request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        if (!request.EsEmergencia)
        {
            var paciente = await _pacienteService.GetByIdAsync(pacienteId);
            if (paciente != null)
            {
                var planCheck = await _planLimiteService.VerificarGpsContinuoAsync(paciente.UsuarioWebId);
                if (!planCheck.Permitido)
                    return BadRequest(new { message = planCheck.Motivo });
            }
        }

        _logger.LogInformation("Inserting GPS tracking for paciente: {PacienteId}, emergency: {EsEmergencia}", pacienteId, request.EsEmergencia);
        var macAddress = ResolverMac(request.DispositivoMac, pacienteId);
        await _sensorService.InsertarTrackingAsync(
            pacienteId, macAddress, request.Longitud, request.Latitud, request.EsEmergencia);

        if (request.EsEmergencia)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _auditoriaService.RegistrarAsync(pacienteId, "tracking_emergencia", "tracking_gps", pacienteId, ip);
        }

        return Ok(new { message = "Tracking insertado" });
    }

    [HttpPost("tracking-batch")]
    [RequestSizeLimit(10485760)]
    public async Task<IActionResult> InsertarTrackingBatch([FromBody] List<TrackingGpsRequest> request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        if (request.Count > 500)
            return BadRequest(new { message = "Máximo 500 registros GPS por lote" });

        var tieneEmergencia = request.Any(r => r.EsEmergencia);
        if (!tieneEmergencia)
        {
            var paciente = await _pacienteService.GetByIdAsync(pacienteId);
            if (paciente != null)
            {
                var planCheck = await _planLimiteService.VerificarGpsContinuoAsync(paciente.UsuarioWebId);
                if (!planCheck.Permitido)
                    return BadRequest(new { message = planCheck.Motivo });
            }
        }

        _logger.LogInformation("Inserting GPS batch of {Count} records for paciente: {PacienteId}", request.Count, pacienteId);
        foreach (var track in request)
        {
            var macAddress = ResolverMac(track.DispositivoMac, pacienteId);
            await _sensorService.InsertarTrackingAsync(
                pacienteId, macAddress, track.Longitud, track.Latitud, track.EsEmergencia);
        }

        return Ok(new { procesadas = request.Count, message = "Lote GPS procesado" });
    }

    [HttpGet("tracking/{pacienteId}/actual")]
    public async Task<IActionResult> TrackingActual(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        // Verificar propiedad ANTES de consultar estado de emergencia (evita information leakage)
        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
            return Forbid();

        if (role == "cuidador")
        {
            var nivelAcceso = User.FindFirst("nivel_acceso")?.Value;
            if (nivelAcceso != "historial_completo")
            {
                var tieneEmergenciaActiva = await _sensorService.TieneEmergenciaActivaAsync(pacienteId);
                if (!tieneEmergenciaActiva)
                    return Forbid();
            }
        }

        _logger.LogInformation("Fetching current GPS location for paciente: {PacienteId}", pacienteId);
        var ubicacion = await _sensorService.ObtenerUltimaUbicacionAsync(pacienteId);
        if (ubicacion?.Ubicacion?.Coordinates == null || ubicacion.Ubicacion.Coordinates.Length < 2)
        {
            _logger.LogWarning("No GPS location found for paciente: {PacienteId}", pacienteId);
            return NotFound(new { message = "Sin ubicación" });
        }

        return Ok(new TrackingResponse(
            ubicacion.Ubicacion.Coordinates[0],
            ubicacion.Ubicacion.Coordinates[1],
            ubicacion.Timestamp,
            ubicacion.EsEmergencia));
    }

    [HttpGet("tracking/{pacienteId}/ruta")]
    public async Task<IActionResult> TrackingRuta(
        string pacienteId, [FromQuery] DateTime desde, [FromQuery] DateTime hasta)
    {
        if (desde > hasta)
            return BadRequest(new { message = "El parámetro 'desde' debe ser anterior o igual a 'hasta'" });

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (role == "cuidador")
        {
            var nivelAcceso = User.FindFirst("nivel_acceso")?.Value;
            if (nivelAcceso != "historial_completo")
                return Forbid();
        }

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
            return Forbid();

        _logger.LogInformation("Fetching GPS route for paciente: {PacienteId} from {Desde} to {Hasta}", pacienteId, desde, hasta);
        var puntos = await _sensorService.ObtenerTrackingRangoAsync(pacienteId, desde, hasta);
        var response = puntos
            .Where(p => p.Ubicacion?.Coordinates != null && p.Ubicacion.Coordinates.Length >= 2)
            .Select(p => new TrackingResponse(
                p.Ubicacion.Coordinates[0],
                p.Ubicacion.Coordinates[1],
                p.Timestamp,
                p.EsEmergencia));
        return Ok(response);
    }
}
