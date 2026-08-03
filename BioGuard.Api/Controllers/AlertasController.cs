using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;
using BioGuard.Api.Config;
using MongoDB.Driver;

namespace BioGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AlertasController : ControllerBase
{
    private readonly AlertaService _alertaService;
    private readonly PacienteService _pacienteService;
    private readonly IMongoDbContext _db;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<AlertasController> _logger;
    private readonly OwnershipHelper _ownershipHelper;
    private readonly NotificacionService _notificacionService;

    public AlertasController(AlertaService alertaService, PacienteService pacienteService,
        IMongoDbContext db, AuditoriaService auditoriaService, ILogger<AlertasController> logger,
        OwnershipHelper ownershipHelper, NotificacionService notificacionService)
    {
        _alertaService = alertaService;
        _pacienteService = pacienteService;
        _db = db;
        _auditoriaService = auditoriaService;
        _logger = logger;
        _ownershipHelper = ownershipHelper;
        _notificacionService = notificacionService;
    }

    [HttpGet("by-paciente/{pacienteId}")]
    public async Task<IActionResult> ObtenerPorPaciente(string pacienteId, [FromQuery] int limite = 50)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
            return Forbid();

        _logger.LogInformation("Fetching alerts for paciente: {PacienteId}, limit: {Limite}", pacienteId, limite);
        var alertas = await _alertaService.ObtenerPorPacienteAsync(pacienteId, limite);
        var response = alertas.Select(a => new AlertaResponse(
            a.Id, a.PacienteId, a.Tipo, a.Nivel, a.Titulo, a.Mensaje,
            a.Atendida, a.FechaCreacion, a.FechaAtencion));
        return Ok(response);
    }

    [HttpGet("pendientes/{pacienteId}")]
    public async Task<IActionResult> ObtenerPendientes(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
            return Forbid();

        _logger.LogInformation("Fetching pending alerts for paciente: {PacienteId}", pacienteId);
        var alertas = await _alertaService.ObtenerPendientesAsync(pacienteId);
        var response = alertas.Select(a => new AlertaResponse(
            a.Id, a.PacienteId, a.Tipo, a.Nivel, a.Titulo, a.Mensaje,
            a.Atendida, a.FechaCreacion, a.FechaAtencion));
        return Ok(response);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        _logger.LogInformation("Fetching alert by ID: {AlertaId}", id);
        var alerta = await _alertaService.ObtenerPorIdAsync(id);
        if (alerta == null)
        {
            _logger.LogWarning("Alert not found: {AlertaId}", id);
            return NotFound();
        }

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(alerta.PacienteId, usuarioId, role!))
            return Forbid();

        return Ok(new AlertaResponse(
            alerta.Id, alerta.PacienteId, alerta.Tipo, alerta.Nivel,
            alerta.Titulo, alerta.Mensaje, alerta.Atendida,
            alerta.FechaCreacion, alerta.FechaAtencion));
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearAlertaRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(request.PacienteId, usuarioId, role!))
            return Forbid();

        var tipo = request.Tipo ?? request.TipoAlerta ?? "General";
        var nivel = request.Nivel ?? "Moderado";
        var titulo = request.Titulo ?? request.Descripcion ?? "Alerta de Salud";
        var mensaje = request.Mensaje ?? request.Descripcion ?? "Se ha detectado una alerta.";

        _logger.LogInformation("Creating alert for paciente: {PacienteId}, type: {Tipo}, level: {Nivel}", request.PacienteId, tipo, nivel);

        var nivelesValidos = new[] { "Bajo", "Leve", "Moderado", "Alto", "Crítico" };
        if (!Array.Exists(nivelesValidos, n => n.Equals(nivel, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "Nivel de riesgo inválido" });

        var sensorData = new SensorData
        {
            PulsoBpm = request.PulsoBpm,
            TemperaturaC = request.TemperaturaC,
            SudoracionGsr = request.SudoracionGsr,
            ProbabilidadPico = request.ProbabilidadPico
        };

        var alerta = await _alertaService.CrearAsync(
            request.PacienteId,
            InputSanitizer.StripHtml(tipo),
            InputSanitizer.StripHtml(nivel),
            InputSanitizer.StripHtml(titulo),
            InputSanitizer.StripHtml(mensaje), sensorData);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "crear_alerta", "alertas", alerta.Id, ip);

        _logger.LogInformation("Alert created successfully: {AlertaId}", alerta.Id);
        return Ok(new { alertaId = alerta.Id, message = "Alerta creada" });
    }

    [HttpPut("{id}/resolver")]
    public async Task<IActionResult> Resolver(string id, [FromBody] ResolverAlertaRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var alerta = await _alertaService.ObtenerPorIdAsync(id);
        if (alerta == null) return NotFound();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(alerta.PacienteId, usuarioId, role!))
            return Forbid();

        _logger.LogInformation("Resolving alert: {AlertaId}, cuidador: {CuidadorId}", id, request.CuidadorId);
        var result = await _alertaService.ResolverAsync(id, request.CuidadorId, request.AccionTomada);
        if (!result)
        {
            _logger.LogWarning("Alert not found for resolution: {AlertaId}", id);
            return NotFound();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "resolver_alerta", "alertas", id, ip);

        _logger.LogInformation("Alert resolved successfully: {AlertaId}", id);
        return Ok(new { message = "Alerta resuelta" });
    }

    [HttpPost("{id}/atender")]
    public async Task<IActionResult> Atender(string id, [FromBody] AtenderAlertaRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var alerta = await _alertaService.ObtenerPorIdAsync(id);
        if (alerta == null) return NotFound();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(alerta.PacienteId, usuarioId, role!))
            return Forbid();

        _logger.LogInformation("Attending alert: {AlertaId}, user: {UsuarioId}", id, usuarioId);
        var result = await _alertaService.ResolverAsync(id, usuarioId, request.NotasAtencion);
        if (!result)
        {
            _logger.LogWarning("Alert not found for attending: {AlertaId}", id);
            return NotFound();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "atender_alerta", "alertas", id, ip);

        _logger.LogInformation("Alert attended successfully: {AlertaId}", id);
        return Ok(new { message = "Alerta atendida" });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Eliminar(string id)
    {
        _logger.LogInformation("Deleting alert: {AlertaId}", id);
        var alerta = await _alertaService.ObtenerPorIdAsync(id);
        if (alerta == null)
        {
            _logger.LogWarning("Alert not found for deletion: {AlertaId}", id);
            return NotFound();
        }

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var paciente = await _pacienteService.GetByIdAsync(alerta.PacienteId);
        if (paciente?.UsuarioWebId != usuarioId)
            return Forbid();

        var result = await _alertaService.EliminarAsync(id);
        if (!result) return NotFound();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "eliminar_alerta", "alertas", id, ip);

        _logger.LogInformation("Alert deleted successfully: {AlertaId}", id);
        return NoContent();
    }

    [HttpPost("{id}/escalar-emergencia")]
    public async Task<IActionResult> EscalarEmergencia(string id)
    {
        var alerta = await _alertaService.ObtenerPorIdAsync(id);
        if (alerta == null) return NotFound();

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(alerta.PacienteId, usuarioId, role!))
            return Forbid();

        if (alerta.Atendida)
            return Conflict(new { message = "La alerta ya fue atendida por el paciente" });

        if (alerta.Nivel != "Crítico")
            return BadRequest(new { message = "Solo se pueden escalar alertas de nivel Crítico" });

        _logger.LogWarning("Escalando emergencia para alerta: {AlertaId}, paciente: {PacienteId}", id, alerta.PacienteId);

        // Obtener último tracking GPS
        var ultimaUbicacion = await _db.FindFirstOrDefaultAsync(_db.TrackingGps,
            Builders<TrackingGps>.Filter.Eq(t => t.Meta.PacienteId, alerta.PacienteId),
            Builders<TrackingGps>.Sort.Descending(t => t.Timestamp));

        // Obtener contacto de emergencia (dueno)
        var paciente = await _pacienteService.GetByIdAsync(alerta.PacienteId);
        string? contactoNombre = null;
        if (paciente != null)
        {
            var dueno = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == paciente.UsuarioWebId);
            contactoNombre = dueno != null ? $"{dueno.Nombre} {dueno.ApellidoPaterno}" : null;

            // Notificar al dueño
            await _notificacionService.CrearAsync(
                alerta.PacienteId,
                "EMERGENCIA: Guardián Nocturno Activado",
                "El paciente no ha respondido a la alerta crítica. Se ha iniciado el protocolo de escalamiento.",
                "emergencia", paciente.UsuarioWebId);
        }

        // Marcar alerta como escalada
        var update = Builders<Alerta>.Update
            .Set(a => a.Atendida, true)
            .Set(a => a.AtendidaPorId, usuarioId)
            .Set(a => a.FechaAtencion, DateTime.UtcNow)
            .Set(a => a.AccionTomada, "Escalado a contacto de emergencia - Guardián Nocturno");
        await _db.Alertas.UpdateOneAsync(a => a.Id == id, update);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "escalar_emergencia", "alertas", id, ip);

        _logger.LogWarning("EMERGENCIA ESCALADA - Alerta: {AlertaId}, Paciente: {PacienteId}, Contacto: {Contacto}",
            id, alerta.PacienteId, contactoNombre);

        return Ok(new
        {
            message = "Emergencia escalada al contacto autorizado",
            contactoNotificado = contactoNombre ?? "Desconocido",
            ubicacionAdjunta = ultimaUbicacion != null
        });
    }
}
