using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;
using BioGuard.Api.Config;

namespace BioGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DispositivosController : ControllerBase
{
    private readonly DispositivoService _dispositivoService;
    private readonly OwnershipHelper _ownershipHelper;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<DispositivosController> _logger;

    public DispositivosController(DispositivoService dispositivoService,
        OwnershipHelper ownershipHelper, AuditoriaService auditoriaService,
        ILogger<DispositivosController> logger)
    {
        _dispositivoService = dispositivoService;
        _ownershipHelper = ownershipHelper;
        _auditoriaService = auditoriaService;
        _logger = logger;
    }

    // ── Vinculación ───────────────────────────────────────────

    [HttpPost("vincular")]
    public async Task<IActionResult> Vincular([FromBody] VincularDispositivoRequest request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.MacAddress) || request.MacAddress.Length < 8)
            return BadRequest(new { message = "Dirección MAC inválida" });

        _logger.LogInformation("Linking device for patient {PacienteId}", pacienteId);
        var dispositivo = await _dispositivoService.VincularAsync(pacienteId, InputSanitizer.StripHtml(request.Nombre), request.MacAddress);
        if (dispositivo == null)
        {
            _logger.LogWarning("Patient {PacienteId} already has a linked device", pacienteId);
            return BadRequest(new { message = "Ya tiene un dispositivo vinculado" });
        }

        return Ok(new { dispositivoId = dispositivo.Id, message = "Dispositivo vinculado" });
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest? request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        if (request != null && !string.IsNullOrEmpty(request.PacienteId) && request.PacienteId != pacienteId)
            return Unauthorized(new { message = "El PacienteId no corresponde al token" });

        _logger.LogDebug("Heartbeat received for patient {PacienteId}", pacienteId);
        var (_, rateLimited) = await _dispositivoService.HeartbeatAsync(
            pacienteId,
            request?.Bateria,
            request?.SensoresActivos);
        return Ok(new { message = "Heartbeat recibido" });
    }

    // ── Consulta ──────────────────────────────────────────────

    [HttpGet("{pacienteId}")]
    public async Task<IActionResult> ObtenerPorPaciente(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
            return Forbid();

        _logger.LogInformation("Getting device for patient {PacienteId}", pacienteId);
        var dispositivo = await _dispositivoService.ObtenerPorPacienteAsync(pacienteId);
        if (dispositivo == null) return Ok(new { Vinculado = false });

        return Ok(new
        {
            Vinculado = true,
            dispositivo.NombreDispositivo,
            dispositivo.MacAddress,
            dispositivo.Conectado,
            dispositivo.FechaVinculacion
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Actualizar(string id, [FromBody] ActualizarDispositivoRequest request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        var dispositivo = await _dispositivoService.ObtenerPorIdAsync(id);
        if (dispositivo == null) return NotFound();
        if (dispositivo.PacienteId != pacienteId) return Forbid();

        _logger.LogInformation("Updating device {Id} name", id);
        var result = await _dispositivoService.ActualizarAsync(id, InputSanitizer.StripHtml(request.Nombre));
        if (!result)
        {
            _logger.LogWarning("Device {Id} not found when attempting to update", id);
            return NotFound();
        }
        return Ok(new { message = "Dispositivo actualizado" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Desvincular(string id)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        var dispositivo = await _dispositivoService.ObtenerPorIdAsync(id);
        if (dispositivo == null) return NotFound();
        if (dispositivo.PacienteId != pacienteId) return Forbid();

        _logger.LogInformation("Unlinking device {Id}", id);
        var result = await _dispositivoService.EliminarAsync(id);
        if (!result)
        {
            _logger.LogWarning("Device {Id} not found when attempting to unlink", id);
            return NotFound();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(pacienteId, "desvincular", "dispositivos", id, ip);

        return NoContent();
    }

    // ── Información completa ──────────────────────────────────

    [HttpGet("{pacienteId}/info-completa")]
    public async Task<IActionResult> ObtenerInfoCompleta(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
            return Forbid();

        _logger.LogInformation("Getting full device info for patient {PacienteId}", pacienteId);
        var info = await _dispositivoService.ObtenerInfoCompletaAsync(pacienteId);
        if (info == null)
            return Ok(new { Reloj = (object?)null, Telefono = (object?)null });

        return Ok(info);
    }

    [HttpPost("sesion-telefono")]
    public async Task<IActionResult> RegistrarSesionTelefono([FromBody] RegistrarSesionTelefonoRequest request)
    {
        var pacienteId = User.FindFirst("paciente_id")?.Value;
        if (string.IsNullOrEmpty(pacienteId)) return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers["User-Agent"].ToString();

        await _dispositivoService.RegistrarSesionTelefonoAsync(
            pacienteId, 
            InputSanitizer.StripHtml(request.ModeloDispositivo), 
            InputSanitizer.StripHtml(request.SistemaOperativo), 
            request.Bateria, request.AhorroEnergia, 
            InputSanitizer.StripHtml(request.Conectividad), ip, userAgent);

        return Ok(new { message = "Sesión de teléfono registrada" });
    }
}

public record HeartbeatRequest(
    string? PacienteId,
    int? Bateria = null,
    List<string>? SensoresActivos = null);

public record ActualizarDispositivoRequest(
    [Required] [StringLength(200)] string Nombre);
