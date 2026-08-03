using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using BioGuard.Api.Models;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;
using BioGuard.Api.Config;

namespace BioGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportesController : ControllerBase
{
    private readonly SensorService _sensorService;
    private readonly AlertaService _alertaService;
    private readonly MedicamentoService _medicamentoService;
    private readonly PacienteService _pacienteService;
    private readonly IMongoDbContext _db;
    private readonly OwnershipHelper _ownershipHelper;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<ReportesController> _logger;

    public ReportesController(
        SensorService sensorService,
        AlertaService alertaService,
        MedicamentoService medicamentoService,
        PacienteService pacienteService,
        IMongoDbContext db,
        ILogger<ReportesController> logger,
        OwnershipHelper ownershipHelper,
        AuditoriaService auditoriaService)
    {
        _sensorService = sensorService;
        _alertaService = alertaService;
        _medicamentoService = medicamentoService;
        _pacienteService = pacienteService;
        _db = db;
        _logger = logger;
        _ownershipHelper = ownershipHelper;
        _auditoriaService = auditoriaService;
    }

    private async Task<bool> VerifyPacienteAccessAsync(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId) || string.IsNullOrEmpty(role)) return false;
        if (role == "cuidador")
        {
            var nivelAcceso = User.FindFirst("nivel_acceso")?.Value;
            if (nivelAcceso != "resumen_semanal" && nivelAcceso != "historial_completo")
                return false;
        }
        return await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role);
    }

    // GET /api/Reportes/resumen/{pacienteId}

    [HttpGet("resumen/{pacienteId}")]
    public async Task<IActionResult> Resumen(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        if (!await VerifyPacienteAccessAsync(pacienteId)) return Forbid();

        _logger.LogInformation("Generating report summary for paciente: {PacienteId}", pacienteId);
        var lecturas = await _sensorService.ObtenerLecturasAsync(pacienteId, 1000);
        var eventos = await _sensorService.ObtenerEventosAsync(pacienteId, 1000);
        var alertas = await _alertaService.ObtenerPorPacienteAsync(pacienteId, 1000);
        var medicamentos = await _medicamentoService.ObtenerPorPacienteAsync(pacienteId);

        var promedioPulso = lecturas.Count > 0
            ? lecturas.Average(l => l.PulsoBpm)
            : 0.0;

        var response = new ReporteResumenResponse(
            lecturas.Count,
            eventos.Count,
            alertas.Count,
            medicamentos.Count,
            eventos.Count(e => e.NivelRiesgo == "Crítico" || e.NivelRiesgo == "Critico"),
            alertas.Count(a => !a.Atendida),
            promedioPulso,
            lecturas.FirstOrDefault()?.Timestamp);

        return Ok(response);
    }

    // GET /api/Reportes/historial-alertas/{pacienteId}

    [HttpGet("historial-alertas/{pacienteId}")]
    public async Task<IActionResult> HistorialAlertas(string pacienteId, [FromQuery] int limite = 100)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        if (!await VerifyPacienteAccessAsync(pacienteId)) return Forbid();

        _logger.LogInformation("Fetching alert history for paciente: {PacienteId}, limit: {Limite}", pacienteId, limite);
        var alertas = await _alertaService.ObtenerPorPacienteAsync(pacienteId, limite);
        var response = alertas.Select(a => new ReporteAlertaResponse(
            a.Id, a.Tipo, a.Nivel, a.Titulo, a.Mensaje,
            a.Atendida, a.FechaCreacion, a.FechaAtencion));
        return Ok(response);
    }

    // GET /api/Reportes/historial-eventos/{pacienteId}

    [HttpGet("historial-eventos/{pacienteId}")]
    public async Task<IActionResult> HistorialEventos(string pacienteId, [FromQuery] int limite = 100)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        if (!await VerifyPacienteAccessAsync(pacienteId)) return Forbid();

        _logger.LogInformation("Fetching event history for paciente: {PacienteId}, limit: {Limite}", pacienteId, limite);
        var eventos = await _sensorService.ObtenerEventosAsync(pacienteId, limite);
        var response = eventos.Select(e => new ReporteEventoResponse(
            e.Id, e.NivelRiesgo, e.ProbabilidadMl, e.Descripcion,
            e.FechaEvento, e.Atendida));
        return Ok(response);
    }

    // GET /api/Reportes/historial-medicamentos/{pacienteId}

    [HttpGet("historial-medicamentos/{pacienteId}")]
    public async Task<IActionResult> HistorialMedicamentos(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        if (!await VerifyPacienteAccessAsync(pacienteId)) return Forbid();

        _logger.LogInformation("Fetching medication history for paciente: {PacienteId}", pacienteId);
        var medicamentos = await _medicamentoService.ObtenerPorPacienteAsync(pacienteId);
        var response = medicamentos.Select(m => new ReporteMedicamentoResponse(
            m.Id, m.Nombre, m.Dosis, m.Horario, m.Activo, m.UltimaToma));
        return Ok(response);
    }

    // GET /api/Reportes/historial-lecturas/{pacienteId}

    [HttpGet("historial-lecturas/{pacienteId}")]
    public async Task<IActionResult> HistorialLecturas(
        string pacienteId, [FromQuery] int limite = 500)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        if (!await VerifyPacienteAccessAsync(pacienteId)) return Forbid();

        _logger.LogInformation("Fetching reading history for paciente: {PacienteId}, limit: {Limite}", pacienteId, limite);
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

    // GET /api/Reportes/compartido/{token} (publico, sin autenticacion)

    [AllowAnonymous]
    [HttpGet("compartido/{token}")]
    public async Task<IActionResult> VerCompartido(string token)
    {
        var reporte = await _db.FindFirstOrDefaultAsync(_db.ReportesCompartidos, r => r.TokenAcceso == token);
        if (reporte == null)
        {
            _logger.LogWarning("Shared report token not found: {Token}", token);
            return NotFound(new { error = "Reporte no encontrado" });
        }

        if (!reporte.Activo || reporte.FechaExpiracion < DateTime.UtcNow)
        {
            _logger.LogWarning("Shared report token expired: {Token}", token);
            return NotFound(new { error = "Reporte expirado o desactivado" });
        }

        // Incrementar contador de accesos
        var increment = Builders<ReporteCompartido>.Update.Inc(r => r.Accesos, 1);
        await _db.ReportesCompartidos.UpdateOneAsync(r => r.Id == reporte.Id, increment);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(reporte.UsuarioWebId, "acceso_reporte_compartido", "reportes_compartidos", reporte.Id, ip);

        // Recopilar datos del reporte
        var lecturas = reporte.IncluirLecturas
            ? await _sensorService.ObtenerLecturasAsync(reporte.PacienteId, 100)
            : new List<BioGuard.Api.Models.LecturaSensor>();
        var eventos = reporte.IncluirEventos
            ? await _sensorService.ObtenerEventosAsync(reporte.PacienteId, 100)
            : new List<BioGuard.Api.Models.EventoMetabolico>();
        var medicamentos = reporte.IncluirMedicamentos
            ? await _medicamentoService.ObtenerPorPacienteAsync(reporte.PacienteId)
            : new List<BioGuard.Api.Models.Medicamento>();

        return Ok(new
        {
            pacienteId = reporte.PacienteId,
            fechaCreacion = reporte.FechaCreacion,
            fechaExpiracion = reporte.FechaExpiracion,
            accesos = reporte.Accesos + 1,
            lecturas = lecturas.Select(l => new
            {
                l.PulsoBpm,
                l.TemperaturaC,
                l.SudoracionGsr,
                l.ProbabilidadPico,
                l.Timestamp
            }),
            eventos = eventos.Select(e => new
            {
                e.NivelRiesgo, e.ProbabilidadMl, e.Descripcion, e.FechaEvento, e.Atendida
            }),
            medicamentos = medicamentos.Select(m => new
            {
                m.Nombre, m.Dosis, m.Horario, m.Activo, m.UltimaToma
            })
        });
    }

    // POST /api/Reportes/{pacienteId}/compartir

    [HttpPost("{pacienteId}/compartir")]
    public async Task<IActionResult> Compartir(string pacienteId, [FromBody] CompartirReporteRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (string.IsNullOrEmpty(role) || (role != "dueno" && role != "cuidador"))
            return Forbid();

        if (role == "cuidador")
        {
            var nivelAcceso = User.FindFirst("nivel_acceso")?.Value;
            if (nivelAcceso != "historial_completo")
                return Forbid();
        }

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role))
            return Forbid();

        if (request.DiasValidez < 1 || request.DiasValidez > 30)
            return BadRequest(new { message = "Días de validez debe estar entre 1 y 30" });

        if (request.PacienteId != pacienteId)
            return BadRequest(new { message = "El pacienteId en la ruta no coincide con el body" });

        _logger.LogInformation("Generating shareable report link for paciente: {PacienteId}, days: {DiasValidez}", pacienteId, request.DiasValidez);

        var tokenBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        var token = Convert.ToHexString(tokenBytes).ToLower();

        var reporte = new Models.ReporteCompartido
        {
            PacienteId = pacienteId,
            UsuarioWebId = usuarioId,
            TokenAcceso = token,
            FechaCreacion = DateTime.UtcNow,
            FechaExpiracion = DateTime.UtcNow.AddDays(request.DiasValidez),
            IncluirLecturas = request.IncluirLecturas,
            IncluirEventos = request.IncluirEventos,
            IncluirMedicamentos = request.IncluirMedicamentos,
            Activo = true
        };

        await _db.ReportesCompartidos.InsertOneAsync(reporte);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "compartir_reporte", "reportes_compartidos", reporte.Id, ip);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            enlace = $"{baseUrl}/api/reportes/compartido/{token}",
            expira = reporte.FechaExpiracion
        });
    }
}
