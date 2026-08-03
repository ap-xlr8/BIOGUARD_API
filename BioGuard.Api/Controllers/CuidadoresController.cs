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
/// MÓDULO 4: Gestión de Cuidadores (varios por cuenta)
/// ENDPOINT WEB + MÓVIL
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CuidadoresController : ControllerBase
{
    private readonly CuidadorService _cuidadorService;
    private readonly PacienteService _pacienteService;
    private readonly IMongoDbContext _db;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<CuidadoresController> _logger;
    private readonly OwnershipHelper _ownershipHelper;

    public CuidadoresController(CuidadorService cuidadorService, PacienteService pacienteService,
        IMongoDbContext db, AuditoriaService auditoriaService, ILogger<CuidadoresController> logger, OwnershipHelper ownershipHelper)
    {
        _cuidadorService = cuidadorService;
        _pacienteService = pacienteService;
        _db = db;
        _auditoriaService = auditoriaService;
        _logger = logger;
        _ownershipHelper = ownershipHelper;
    }

    // ── Consulta ──────────────────────────────────────────────

    /// <summary>
    /// GET /api/Cuidadores [WEB]
    /// MÓDULO 4: Listar todos los cuidadores del usuario
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Listing cuidadores for user: {UserId}", usuarioId);
        var cuidadores = await _cuidadorService.ObtenerPorUsuarioAsync(usuarioId);
        var response = cuidadores.Select(c => new CuidadorResponse(
            c.Id, c.Nombre, c.Parentesco, c.PacienteId, c.NivelAcceso)).ToList();
        return Ok(response);
    }

    /// <summary>
    /// GET /api/Cuidadores/disponibles [WEB]
    /// MÓDULO 4: Cuántos puede agregar según plan (ej: "2/3")
    /// </summary>
    [HttpGet("disponibles")]
    public async Task<IActionResult> Disponibles()
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Checking available slots for user: {UserId}", usuarioId);
        var pacientes = await _pacienteService.GetAllByUsuarioAsync(usuarioId);
        var paciente = pacientes.FirstOrDefault();
        if (paciente == null)
        {
            _logger.LogWarning("No patient found for user: {UserId} when checking available slots", usuarioId);
            return Ok(new { Disponibles = 0, Total = 0 });
        }

        var dueno = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == usuarioId);
        var plan = dueno != null ? await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == dueno.PlanId) : null;
        var limite = plan?.LimiteCuidadores ?? 3;
        var count = await _cuidadorService.ContarPorPacienteAsync(paciente.Id);
        return Ok(new { Usados = count, Total = limite, Disponibles = limite - count });
    }

    /// <summary>
    /// GET /api/Cuidadores/{id} [WEB + MÓVIL]
    /// MÓDULO 4: Detalle de un cuidador
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Fetching cuidador by ID: {CuidadorId}", id);
        var cuidador = await _cuidadorService.ObtenerPorIdAsync(id);
        if (cuidador == null)
        {
            _logger.LogWarning("Cuidador not found: {CuidadorId}", id);
            return NotFound();
        }
        // El cuidador autenticado por QR tiene como 'sub' su Cuidador.Id, no UsuarioWebId.
        if (cuidador.UsuarioWebId != usuarioId && cuidador.Id != usuarioId)
        {
            _logger.LogWarning("Ownership check failed fetching cuidador - user: {UserId}, cuidador: {CuidadorId}", usuarioId, id);
            return Forbid();
        }

        return Ok(new CuidadorResponse(
            cuidador.Id, cuidador.Nombre, cuidador.Parentesco,
            cuidador.PacienteId, cuidador.NivelAcceso));
    }

    /// <summary>
    /// GET /api/Cuidadores/by-paciente/{pacienteId} [WEB + MÓVIL]
    /// MÓDULO 4: Cuidador(es) de un paciente específico
    /// </summary>
    [HttpGet("by-paciente/{pacienteId}")]
    public async Task<IActionResult> GetByPaciente(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(pacienteId, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching cuidadores by paciente - user: {UserId}, paciente: {PacienteId}", usuarioId, pacienteId);
            return Forbid();
        }

        _logger.LogInformation("Fetching cuidadores for paciente: {PacienteId}", pacienteId);
        var cuidadores = await _cuidadorService.ObtenerPorPacienteAsync(pacienteId);
        var response = cuidadores.Select(c => new CuidadorResponse(
            c.Id, c.Nombre, c.Parentesco, c.PacienteId, c.NivelAcceso)).ToList();
        return Ok(response);
    }

    // ── Alta / Edición ────────────────────────────────────────

    /// <summary>
    /// POST /api/Cuidadores [WEB]
    /// MÓDULO 4: Crear cuidador + generar QR
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearCuidadorRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var paciente = await _pacienteService.GetByIdAsync(request.PacienteId);
        if (paciente == null) return NotFound(new { message = "Paciente no encontrado" });

        var dueno = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == paciente.UsuarioWebId);
        if (dueno == null) return NotFound(new { message = "Dueño no encontrado" });

        var plan = await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == dueno.PlanId);
        var limiteCuidadores = plan?.LimiteCuidadores ?? 3;

        var count = await _cuidadorService.ContarPorPacienteAsync(request.PacienteId);
        if (count >= limiteCuidadores)
        {
            _logger.LogWarning("Cuidador limit {Limit} reached for paciente: {PacienteId}, user: {UserId}", limiteCuidadores, request.PacienteId, usuarioId);
            return BadRequest(new { message = $"Límite de cuidadores alcanzado ({limiteCuidadores})" });
        }

        _logger.LogInformation("Creating cuidador for user: {UserId}, paciente: {PacienteId}", usuarioId, request.PacienteId);
        var (success, cuidador, codigo, error) = await _cuidadorService.CrearAsync(
            usuarioId, request.PacienteId, InputSanitizer.StripHtml(request.Nombre), InputSanitizer.StripHtml(request.Parentesco),
            request.Telefono, request.Correo, nivelAcceso: request.NivelAcceso ?? "solo_alertas", limiteCuidadores);

        if (!success)
        {
            _logger.LogWarning("Cuidador creation failed: {Error}", error);
            return BadRequest(new { message = error ?? "Error creando cuidador" });
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "crear_cuidador", "cuidadores", cuidador?.Id ?? "", ip);
        _logger.LogInformation("Cuidador created successfully for user: {UserId}", usuarioId);
        return Ok(new { CuidadorId = cuidador?.Id ?? "", CodigoAccesoQr = codigo, message = "Cuidador creado" });
    }

    /// <summary>
    /// PUT /api/Cuidadores/{id} [MÓVIL]
    /// MÓDULO 4: Editar nombre, parentesco y nivel de acceso
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Editar(string id, [FromBody] ActualizarCuidadorRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Editing cuidador: {CuidadorId}", id);
        var cuidador = await _cuidadorService.ObtenerPorIdAsync(id);
        if (cuidador == null)
        {
            _logger.LogWarning("Cuidador not found for edit: {CuidadorId}", id);
            return NotFound();
        }
        if (cuidador.UsuarioWebId != usuarioId)
        {
            _logger.LogWarning("Ownership check failed editing cuidador - user: {UserId}, cuidador: {CuidadorId}", usuarioId, id);
            return Forbid();
        }

        var result = await _cuidadorService.ActualizarAsync(id, InputSanitizer.StripHtml(request.Nombre), InputSanitizer.StripHtml(request.Parentesco), request.NivelAcceso);
        if (!result)
        {
            _logger.LogWarning("Cuidador update failed: {CuidadorId}", id);
            return NotFound();
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "editar_cuidador", "cuidadores", id, ip);
        _logger.LogInformation("Cuidador updated successfully: {CuidadorId}", id);
        return Ok(new { message = "Cuidador actualizado" });
    }

    /// <summary>
    /// PATCH /api/Cuidadores/{id}/nivel-acceso [WEB]
    /// MÓDULO 4: Actualizar nivel de acceso, revoca sesiones si se reduce
    /// </summary>
    [HttpPatch("{id}/nivel-acceso")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> ActualizarNivelAcceso(string id, [FromBody] ActualizarNivelAccesoRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var cuidador = await _cuidadorService.ObtenerPorIdAsync(id);
        if (cuidador == null) return NotFound();
        if (cuidador.UsuarioWebId != usuarioId) return Forbid();

        var niveles = new[] { "solo_alertas", "resumen_semanal", "historial_completo" };
        var oldIdx = Array.IndexOf(niveles, cuidador.NivelAcceso);
        var newIdx = Array.IndexOf(niveles, request.NivelAcceso);
        var isDowngrade = newIdx >= 0 && newIdx < oldIdx;

        var result = await _cuidadorService.ActualizarNivelAccesoAsync(id, request.NivelAcceso);
        if (!result) return BadRequest(new { message = "Nivel de acceso no válido" });

        if (isDowngrade)
        {
            var filter = Builders<RefreshToken>.Filter.Where(t => t.UsuarioId == id);
            var update = Builders<RefreshToken>.Update.Set(t => t.RevokedAt, DateTime.UtcNow);
            await _db.RefreshTokens.UpdateManyAsync(filter, update);
            _logger.LogInformation("Sessions revoked for downgraded cuidador: {CuidadorId}", id);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "cambiar_nivel_acceso", "cuidadores", id, ip);
        return Ok(new { message = "Nivel de acceso actualizado", NivelAcceso = request.NivelAcceso });
    }

    /// <summary>
    /// DELETE /api/Cuidadores/{id} [WEB]
    /// MÓDULO 4: Revocar acceso del cuidador + revocar sesiones
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Eliminar(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Deleting cuidador: {CuidadorId}", id);
        var cuidador = await _cuidadorService.ObtenerPorIdAsync(id);
        if (cuidador == null)
        {
            _logger.LogWarning("Cuidador not found for deletion: {CuidadorId}", id);
            return NotFound();
        }
        if (cuidador.UsuarioWebId != usuarioId)
        {
            _logger.LogWarning("Ownership check failed deleting cuidador - user: {UserId}, cuidador: {CuidadorId}", usuarioId, id);
            return Forbid();
        }

        var result = await _cuidadorService.EliminarAsync(id);
        if (!result)
        {
            _logger.LogWarning("Cuidador deletion failed: {CuidadorId}", id);
            return NotFound();
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "eliminar_cuidador", "cuidadores", id, ip);
        _logger.LogInformation("Cuidador deleted and sessions revoked: {CuidadorId}", id);
        return NoContent();
    }

    // ── QR y Vinculación ──────────────────────────────────────

    /// <summary>
    /// GET /api/Cuidadores/{id}/qr [WEB]
    /// MÓDULO 4: Retornar QR y código para vinculación
    /// </summary>
    [HttpGet("{id}/qr")]
    public async Task<IActionResult> ObtenerQR(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (usuarioId == null) return Unauthorized();
        var cuidador = await _cuidadorService.ObtenerPorIdAsync(id);
        if (cuidador == null) return NotFound();
        if (cuidador.UsuarioWebId != usuarioId) return Forbid();

        _logger.LogInformation("Fetching QR for cuidador: {CuidadorId}", id);
        return Ok(new { CodigoAccesoQr = cuidador.CodigoAccesoQr });
    }

    /// <summary>
    /// POST /api/Cuidadores/{id}/regenerar-qr [WEB]
    /// MÓDULO 4: Nuevo código (revoca el anterior)
    /// </summary>
    [HttpPost("{id}/regenerar-qr")]
    public async Task<IActionResult> RegenerarQR(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (usuarioId == null) return Unauthorized();
        var cuidador = await _cuidadorService.ObtenerPorIdAsync(id);
        if (cuidador == null) return NotFound();
        if (cuidador.UsuarioWebId != usuarioId) return Forbid();

        _logger.LogInformation("Regenerating QR for cuidador: {CuidadorId}", id);

        var codigo = await _cuidadorService.RegenerarQRAsync(id);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "regenerar_qr_cuidador", "cuidadores", id, ip);
        _logger.LogInformation("QR regenerated for cuidador: {CuidadorId}", id);
        return Ok(new { CodigoAccesoQr = codigo, message = "QR regenerado" });
    }

}
