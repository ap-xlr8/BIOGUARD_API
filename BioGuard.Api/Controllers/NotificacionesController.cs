using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using MongoDB.Driver;

namespace BioGuard.Api.Controllers;

/// <summary>
/// MÓDULO 5: Notificaciones Push
/// ENDPOINT WEB + MÓVIL
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificacionesController : ControllerBase
{
    private readonly NotificacionService _notificacionService;
    private readonly PacienteService _pacienteService;
    private readonly IMongoDbContext _db;
    private readonly ILogger<NotificacionesController> _logger;
    private readonly OwnershipHelper _ownershipHelper;

    public NotificacionesController(NotificacionService notificacionService, PacienteService pacienteService, IMongoDbContext db, ILogger<NotificacionesController> logger, OwnershipHelper ownershipHelper)
    {
        _notificacionService = notificacionService;
        _pacienteService = pacienteService;
        _db = db;
        _logger = logger;
        _ownershipHelper = ownershipHelper;
    }

    // ── Consulta ──────────────────────────────────────────────

    /// <summary>
    /// GET /api/Notificaciones [WEB]
    /// MÓDULO 5: Obtener todas las notificaciones del usuario logueado
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Listing notifications for user {UsuarioId}", usuarioId);
        var notificaciones = await _notificacionService.ObtenerPorUsuarioAsync(usuarioId);
        var response = notificaciones.Select(n => new NotificacionResponse(
            n.Id, n.Titulo, n.Mensaje, n.Leida, n.FechaEnvio));
        return Ok(response);
    }

    /// <summary>
    /// GET /api/Notificaciones/by-paciente/{pacienteId} [MÓVIL]
    /// MÓDULO 5: Obtener notificaciones del paciente
    /// </summary>
    [HttpGet("by-paciente/{pacienteId}")]
    public async Task<IActionResult> ObtenerPorPaciente(string pacienteId)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, currentUserId, role!))
        {
            _logger.LogWarning("Ownership check failed for patient {PacienteId} requested by user {UsuarioId}", pacienteId, currentUserId);
            return Forbid();
        }

        _logger.LogInformation("Listing notifications for patient {PacienteId}", pacienteId);
        var notificaciones = await _notificacionService.ObtenerPorPacienteAsync(pacienteId);
        var response = notificaciones.Select(n => new NotificacionResponse(
            n.Id, n.Titulo, n.Mensaje, n.Leida, n.FechaEnvio));
        return Ok(response);
    }

    /// <summary>
    /// GET /api/Notificaciones/by-usuario/{usuarioId} [WEB]
    /// MÓDULO 5: Obtener notificaciones por usuario web
    /// </summary>
    [HttpGet("by-usuario/{usuarioId}")]
    public async Task<IActionResult> ObtenerPorUsuario(string usuarioId)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

        if (currentUserId != usuarioId)
        {
            _logger.LogWarning("User {UsuarioId} attempted to access notifications of user {TargetUsuarioId} without permission", currentUserId, usuarioId);
            return Forbid();
        }

        _logger.LogInformation("Listing notifications for user {UsuarioId}", usuarioId);
        var notificaciones = await _notificacionService.ObtenerPorUsuarioAsync(usuarioId);
        var response = notificaciones.Select(n => new NotificacionResponse(
            n.Id, n.Titulo, n.Mensaje, n.Leida, n.FechaEnvio));
        return Ok(response);
    }

    // ── Gestión ───────────────────────────────────────────────

    /// <summary>
    /// PUT /api/Notificaciones/{id}/leer [MÓVIL]
    /// MÓDULO 5: Marcar notificación como leída
    /// </summary>
    [HttpPut("{id}/leer")]
    public async Task<IActionResult> MarcarLeida(string id)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

        _logger.LogInformation("Marking notification {Id} as read", id);
        var notif = await _notificacionService.ObtenerPorIdAsync(id);
        if (notif == null) return NotFound();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(notif.PacienteId, currentUserId, role!))
            return Forbid();

        var result = await _notificacionService.MarcarLeidaAsync(id);
        if (!result)
        {
            _logger.LogWarning("Notification {Id} not found when marking as read", id);
            return NotFound();
        }
        return Ok(new { message = "Notificación marcada como leída" });
    }

    // ── Envío (interno) ──────────────────────────────────────

    /// <summary>
    /// POST /api/Notificaciones [MÓVIL]
    /// MÓDULO 5: Crear notificación + enviar por FCM
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "dueno,paciente")]
    public async Task<IActionResult> Crear([FromBody] CrearNotificacionRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (role == "paciente" && usuarioId != request.PacienteId)
        {
            _logger.LogWarning("Patient {UsuarioId} attempted to create notification for different patient {PacienteId}", usuarioId, request.PacienteId);
            return Forbid();
        }

        _logger.LogInformation("Creating notification for patient {PacienteId} by user {UsuarioId}", request.PacienteId, usuarioId);
        var notificacion = await _notificacionService.CrearAsync(
            request.PacienteId,
            InputSanitizer.StripHtml(request.Titulo),
            InputSanitizer.StripHtml(request.Mensaje),
            InputSanitizer.StripHtml(request.Tipo),
            request.CuidadorId, request.UsuarioWebId);

        return Ok(new { NotificacionId = notificacion.Id, message = "Notificación creada" });
    }

    /// <summary>
    /// DELETE /api/Notificaciones/{id} [WEB]
    /// MÓDULO 5: Eliminar notificación
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Eliminar(string id)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

        _logger.LogInformation("Deleting notification {Id}", id);
        var notif = await _notificacionService.ObtenerPorIdAsync(id);
        if (notif == null) return NotFound();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(notif.PacienteId, currentUserId, role!))
            return Forbid();

        var result = await _notificacionService.EliminarAsync(id);
        if (!result)
        {
            _logger.LogWarning("Notification {Id} not found when attempting to delete", id);
            return NotFound();
        }
        return NoContent();
    }

    // ── FCM Push Tokens ────────────────────────────────────────

    /// <summary>
    /// POST /api/Notificaciones/fcm/registrar-token [MÓVIL]
    /// MÓDULO 5: Registrar o actualizar token FCM del usuario logueado
    /// </summary>
    [HttpPost("fcm/registrar-token")]
    public async Task<IActionResult> RegistrarToken([FromBody] RegisterFcmTokenRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "paciente";
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Registering FCM token for user {UsuarioId}", usuarioId);

        // Buscar si ya existe el token para este usuario
        var existing = await _db.FindFirstOrDefaultAsync(_db.FcmTokens, t => t.Token == request.Token && t.UsuarioId == usuarioId);
        if (existing != null)
        {
            var update = Builders<FcmToken>.Update
                .Set(t => t.FechaRegistro, DateTime.UtcNow)
                .Set(t => t.Activo, true);
            await _db.FcmTokens.UpdateOneAsync(t => t.Id == existing.Id, update);
        }
        else
        {
            var fcmToken = new BioGuard.Api.Models.FcmToken
            {
                UsuarioId = usuarioId,
                Rol = role,
                Token = request.Token,
                Activo = true,
                FechaRegistro = DateTime.UtcNow
            };
            await _db.FcmTokens.InsertOneAsync(fcmToken);
        }

        return Ok(new { message = "Token FCM registrado exitosamente" });
    }

    /// <summary>
    /// DELETE /api/Notificaciones/fcm/token/{token} [MÓVIL]
    /// MÓDULO 5: Eliminar o desactivar un token FCM
    /// </summary>
    [HttpDelete("fcm/token/{token}")]
    public async Task<IActionResult> EliminarToken(string token)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Deleting FCM token for user {UsuarioId}", usuarioId);

        var result = await _db.FcmTokens.DeleteManyAsync(t => t.Token == token && t.UsuarioId == usuarioId);
        return Ok(new { message = $"Se eliminaron {result.DeletedCount} registros de token FCM" });
    }

}
