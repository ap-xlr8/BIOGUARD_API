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
/// MÓDULO 3: Gestión del Paciente (1 por cuenta)
/// ENDPOINT WEB + MÓVIL
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PacientesController : ControllerBase
{
    private readonly PacienteService _pacienteService;
    private readonly SensorService _sensorService;
    private readonly DispositivoService _dispositivoService;
    private readonly IMongoDbContext _db;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<PacientesController> _logger;
    private readonly OwnershipHelper _ownershipHelper;

    public PacientesController(PacienteService pacienteService, SensorService sensorService,
        DispositivoService dispositivoService, IMongoDbContext db, AuditoriaService auditoriaService, ILogger<PacientesController> logger, OwnershipHelper ownershipHelper)
    {
        _pacienteService = pacienteService;
        _sensorService = sensorService;
        _dispositivoService = dispositivoService;
        _db = db;
        _auditoriaService = auditoriaService;
        _logger = logger;
        _ownershipHelper = ownershipHelper;
    }

    // ── Consulta ──────────────────────────────────────────────

    /// <summary>
    /// GET /api/Pacientes/mi-paciente [WEB]
    /// MÓDULO 3: Obtener paciente del usuario logueado
    /// </summary>
    [HttpGet("mi-paciente")]
    public async Task<IActionResult> MiPaciente()
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Fetching mi-paciente for user: {UserId}", usuarioId);
        var pacientes = await _pacienteService.GetAllByUsuarioAsync(usuarioId);
        var paciente = pacientes.FirstOrDefault();
        if (paciente == null)
        {
            _logger.LogWarning("No patient found for user: {UserId}", usuarioId);
            return NotFound(new { message = "No tiene paciente registrado" });
        }

        return Ok(new PacienteResponse(
            paciente.Id, paciente.Nombre, paciente.Biometria?.EsDiabetico ?? false,
            paciente.PerfilCompletado));
    }

    /// <summary>
    /// GET /api/Pacientes/{id} [WEB + MÓVIL]
    /// MÓDULO 3: Obtener paciente por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(id, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed for user: {UserId}, paciente: {PacienteId}, role: {Role}", usuarioId, id, role);
            return Forbid();
        }

        _logger.LogInformation("Fetching paciente by ID: {PacienteId}", id);
        var paciente = await _pacienteService.GetByIdAsync(id);
        if (paciente == null)
        {
            _logger.LogWarning("Paciente not found: {PacienteId}", id);
            return NotFound();
        }

        return Ok(new PacienteResponse(
            paciente.Id, paciente.Nombre, paciente.Biometria?.EsDiabetico ?? false,
            paciente.PerfilCompletado));
    }

    /// <summary>
    /// GET /api/Pacientes/by-usuario/{usuarioWebId} [WEB]
    /// MÓDULO 3: Listar pacientes de un usuario
    /// </summary>
    [HttpGet("by-usuario/{usuarioWebId}")]
    public async Task<IActionResult> GetByUsuario(string usuarioWebId)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

        if (role != "dueno" || currentUserId != usuarioWebId)
        {
            _logger.LogWarning("Ownership check failed listing patients - current user: {UserId}, requested user: {RequestedUserId}, role: {Role}", currentUserId, usuarioWebId, role);
            return Forbid();
        }

        _logger.LogInformation("Listing patients for user: {UserId}", usuarioWebId);
        var pacientes = await _pacienteService.GetAllByUsuarioAsync(usuarioWebId);
        var response = pacientes.Select(p => new PacienteResponse(
            p.Id, p.Nombre, p.Biometria?.EsDiabetico ?? false,
            p.PerfilCompletado)).ToList();
        return Ok(response);
    }

    // ── Alta / Edición ────────────────────────────────────────

    /// <summary>
    /// POST /api/Pacientes [WEB]
    /// MÓDULO 3: Crear paciente + generar QR y código
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Crear([FromBody] CrearPacienteRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Creating paciente for user: {UserId}, name: {Nombre}", usuarioId, request.Nombre);
        var (success, codigo, pacienteId, error) = await _pacienteService.CrearPacienteAsync(usuarioId, InputSanitizer.StripHtml(request.Nombre));
        if (!success)
        {
            _logger.LogWarning("Paciente creation failed for user: {UserId}: {Error}", usuarioId, error);
            return BadRequest(new { message = error });
        }
        _logger.LogInformation("Paciente created successfully for user: {UserId}", usuarioId);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "crear", "pacientes", codigo!, ip);
        return Ok(new { message = "Paciente creado", CodigoAccesoQr = codigo, PacienteId = pacienteId });
    }

    /// <summary>
    /// PUT /api/Pacientes/{id} [WEB]
    /// MÓDULO 3: Editar nombre del paciente
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Editar(string id, [FromBody] UpdateNombreRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(id, usuarioId, "dueno"))
        {
            _logger.LogWarning("Ownership check failed editing paciente - user: {UserId}, paciente: {PacienteId}", usuarioId, id);
            return Forbid();
        }

        _logger.LogInformation("Editing paciente: {PacienteId}, new name: {Nombre}", id, request.Nombre);
        var result = await _pacienteService.UpdateNombreAsync(id, InputSanitizer.StripHtml(request.Nombre));
        if (!result)
        {
            _logger.LogWarning("Paciente not found for edit: {PacienteId}", id);
            return NotFound();
        }
        _logger.LogInformation("Paciente edited successfully: {PacienteId}", id);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "editar", "pacientes", id, ip);
        return Ok(new { message = "Paciente actualizado" });
    }

    /// <summary>
    /// DELETE /api/Pacientes/{id} [WEB]
    /// MÓDULO 3: Desvincular paciente + borrar datos
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Eliminar(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(id, usuarioId, "dueno"))
        {
            _logger.LogWarning("Ownership check failed deleting paciente - user: {UserId}, paciente: {PacienteId}", usuarioId, id);
            return Forbid();
        }

        _logger.LogInformation("Deleting paciente: {PacienteId}", id);
        var result = await _pacienteService.EliminarAsync(id);
        if (!result)
        {
            _logger.LogWarning("Paciente not found for deletion: {PacienteId}", id);
            return NotFound();
        }
        _logger.LogInformation("Paciente deleted successfully: {PacienteId}", id);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "eliminar", "pacientes", id, ip);
        return NoContent();
    }

    // ── Calibración (Móvil) ───────────────────────────────────

    /// <summary>
    /// PUT /api/Pacientes/{id}/biometria [MÓVIL]
    /// MÓDULO 3: Guardar fecha_nacimiento, sexo, peso, estatura, actividad, diabetes
    /// </summary>
    [HttpPut("{id}/biometria")]
    public async Task<IActionResult> UpdateBiometria(string id, [FromBody] UpdateBiometriaRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(id, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed updating biometria - user: {UserId}, paciente: {PacienteId}", usuarioId, id);
            return Forbid();
        }

        _logger.LogInformation("Updating biometria for paciente: {PacienteId}", id);
        await _pacienteService.UpdateBiometriaAsync(id, request);
        _logger.LogInformation("Biometria updated for paciente: {PacienteId}", id);
        return Ok(new { message = "Biometría actualizada" });
    }

    // ── QR y Vinculación ──────────────────────────────────────

    /// <summary>
    /// GET /api/Pacientes/{id}/qr [WEB]
    /// MÓDULO 3: Retornar imagen QR + código alfanumérico
    /// </summary>
    [HttpGet("{id}/qr")]
    public async Task<IActionResult> ObtenerQR(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(id, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching QR - user: {UserId}, paciente: {PacienteId}", usuarioId, id);
            return Forbid();
        }

        _logger.LogInformation("Fetching QR for paciente: {PacienteId}", id);
        var paciente = await _pacienteService.GetByIdAsync(id);
        if (paciente == null)
        {
            _logger.LogWarning("Paciente not found for QR: {PacienteId}", id);
            return NotFound();
        }
        return Ok(new { CodigoAccesoQr = paciente.CodigoAccesoQr });
    }

    /// <summary>
    /// POST /api/Pacientes/{id}/regenerar-qr [WEB]
    /// MÓDULO 3: Generar nuevo código (revoca el anterior)
    /// </summary>
    [HttpPost("{id}/regenerar-qr")]
    public async Task<IActionResult> RegenerarQR(string id, [FromQuery] bool revocarSesiones = false)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(id, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed regenerating QR - user: {UserId}, paciente: {PacienteId}", usuarioId, id);
            return Forbid();
        }

        _logger.LogInformation("Regenerating QR for paciente: {PacienteId} (revocarSesiones={Revoke})", id, revocarSesiones);
        var paciente = await _pacienteService.GetByIdAsync(id);
        if (paciente == null)
        {
            _logger.LogWarning("Paciente not found for QR regen: {PacienteId}", id);
            return NotFound();
        }

        var codigo = await _pacienteService.RegenerarCodigoAccesoAsync(id);
        if (codigo == null)
        {
            _logger.LogWarning("QR regeneration failed for paciente: {PacienteId}", id);
            return NotFound();
        }

        if (revocarSesiones)
        {
            var filter = Builders<RefreshToken>.Filter.Where(t => t.UsuarioId == id);
            var update = Builders<RefreshToken>.Update.Set(t => t.RevokedAt, DateTime.UtcNow);
            await _db.RefreshTokens.UpdateManyAsync(filter, update);
            _logger.LogInformation("Sessions revoked for paciente: {PacienteId}", id);
        }

        _logger.LogInformation("QR regenerated for paciente: {PacienteId}", id);
        return Ok(new { CodigoAccesoQr = codigo, message = "QR regenerado" });
    }

    // ── Dispositivo ───────────────────────────────────────────

    /// <summary>
    /// GET /api/Pacientes/{id}/dispositivo [WEB + MÓVIL]
    /// MÓDULO 3: Ver si tiene WearOS vinculado y estado
    /// </summary>
    [HttpGet("{id}/dispositivo")]
    public async Task<IActionResult> ObtenerDispositivo(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(id, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching device - user: {UserId}, paciente: {PacienteId}", usuarioId, id);
            return Forbid();
        }

        _logger.LogInformation("Fetching device for paciente: {PacienteId}", id);
        var dispositivo = await _dispositivoService.ObtenerPorPacienteAsync(id);
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

    /// <summary>
    /// GET /api/Pacientes/{id}/dashboard-summary [WEB + MÓVIL]
    /// MÓDULO 3: Obtener estado consolidado del paciente para renderizado del Dashboard
    /// </summary>
    [HttpGet("{id}/dashboard-summary")]
    public async Task<IActionResult> ObtenerDashboardSummary(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(id, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed fetching dashboard summary - user: {UserId}, paciente: {PacienteId}", usuarioId, id);
            return Forbid();
        }

        _logger.LogInformation("Generating dashboard summary for patient {PacienteId}", id);

        var paciente = await _pacienteService.GetByIdAsync(id);
        if (paciente == null) return NotFound();

        // Obtener última lectura fisiológica
        var lecturas = await _sensorService.ObtenerLecturasAsync(id, 1);
        var ultimaLectura = lecturas.FirstOrDefault();

        // Obtener última geolocalización
        var ultimaUbicacion = await _sensorService.ObtenerUltimaUbicacionAsync(id);

        // Obtener estado de dispositivo WearOS
        var dispositivo = await _dispositivoService.ObtenerPorPacienteAsync(id);

        // Obtener alertas pendientes e historial reciente de alertas (últimas 5)
        var alertasPendientesCount = await _db.CountDocumentsAsync(_db.Alertas, a => a.PacienteId == id && !a.Atendida);
        var alertasRecientes = await _db.FindToListAsync(_db.Alertas, Builders<Alerta>.Filter.Eq(a => a.PacienteId, id), Builders<Alerta>.Sort.Descending(a => a.FechaCreacion), 5);

        // Obtener historial reciente de eventos metabólicos (últimos 5)
        var eventosRecientes = await _sensorService.ObtenerEventosAsync(id, 5);

        object dispositivoRes;
        if (dispositivo != null)
        {
            dispositivoRes = new
            {
                Vinculado = true,
                dispositivo.NombreDispositivo,
                dispositivo.MacAddress,
                dispositivo.Conectado,
                dispositivo.FechaVinculacion,
                dispositivo.Bateria,
                dispositivo.UltimaSincronizacion
            };
        }
        else
        {
            dispositivoRes = new
            {
                Vinculado = false
            };
        }

        return Ok(new
        {
            Paciente = new
            {
                paciente.Id,
                paciente.Nombre,
                EsDiabetico = paciente.Biometria?.EsDiabetico ?? false,
                paciente.PerfilCompletado
            },
            UltimaLectura = ultimaLectura != null ? new
            {
                ultimaLectura.Timestamp,
                ultimaLectura.PulsoBpm,
                ultimaLectura.TemperaturaC,
                ultimaLectura.SudoracionGsr,
                ultimaLectura.Hrv,
                ultimaLectura.Spo2,
                ultimaLectura.ProbabilidadPico,
                NivelRiesgo = ultimaLectura.NivelRiesgo ?? "Bajo"
            } : null,
            UltimaUbicacion = ultimaUbicacion != null ? new
            {
                Longitud = ultimaUbicacion.Ubicacion.Coordinates.Length > 0 ? (double?)ultimaUbicacion.Ubicacion.Coordinates[0] : null,
                Latitud = ultimaUbicacion.Ubicacion.Coordinates.Length > 1 ? (double?)ultimaUbicacion.Ubicacion.Coordinates[1] : null,
                ultimaUbicacion.Timestamp,
                ultimaUbicacion.EsEmergencia
            } : null,
            Dispositivo = dispositivoRes,
            AlertasPendientesCount = alertasPendientesCount,
            AlertasRecientes = alertasRecientes.Select(a => new
            {
                a.Id,
                a.Tipo,
                a.Nivel,
                a.Titulo,
                a.Mensaje,
                a.Atendida,
                a.FechaCreacion
            }),
            EventosRecientes = eventosRecientes.Select(e => new
            {
                e.Id,
                e.NivelRiesgo,
                e.ProbabilidadMl,
                e.Descripcion,
                e.FechaEvento,
                e.Atendida
            })
        });
    }

}
