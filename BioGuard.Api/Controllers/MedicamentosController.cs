using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;
using BioGuard.Api.Config;

namespace BioGuard.Api.Controllers;

/// <summary>
/// MÓDULO 5: Medicamentos (trigger-based por ML)
/// ENDPOINT WEB + MÓVIL
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MedicamentosController : ControllerBase
{
    private readonly MedicamentoService _medicamentoService;
    private readonly PacienteService _pacienteService;
    private readonly IMongoDbContext _db;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<MedicamentosController> _logger;
    private readonly OwnershipHelper _ownershipHelper;

    public MedicamentosController(MedicamentoService medicamentoService, PacienteService pacienteService, IMongoDbContext db, AuditoriaService auditoriaService, ILogger<MedicamentosController> logger, OwnershipHelper ownershipHelper)
    {
        _medicamentoService = medicamentoService;
        _pacienteService = pacienteService;
        _db = db;
        _auditoriaService = auditoriaService;
        _logger = logger;
        _ownershipHelper = ownershipHelper;
    }

    /// <summary>
    /// GET /api/Medicamentos/by-paciente/{pacienteId} [WEB + MÓVIL]
    /// </summary>
    [HttpGet("by-paciente/{pacienteId}")]
    public async Task<IActionResult> ObtenerPorPaciente(string pacienteId)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteAccessAsync(pacienteId, usuarioId, role!, OwnershipHelper.NivelHistorialCompleto))
        {
            _logger.LogWarning("Ownership check failed fetching medications - user: {UserId}, paciente: {PacienteId}", usuarioId, pacienteId);
            return Forbid();
        }

        _logger.LogInformation("Fetching medications for paciente: {PacienteId}", pacienteId);
        var medicamentos = await _medicamentoService.ObtenerPorPacienteAsync(pacienteId);
        var response = medicamentos.Select(m => new MedicamentoResponse(
            m.Id, m.PacienteId, m.Nombre, m.Dosis, m.Horario,
            m.Notas, m.Activo, m.FechaCreacion, m.UltimaToma));
        return Ok(response);
    }

    /// <summary>
    /// GET /api/Medicamentos/by-paciente/{pacienteId}/adherencia [WEB]
    /// </summary>
    [HttpGet("by-paciente/{pacienteId}/adherencia")]
    public async Task<IActionResult> Adherencia(string pacienteId, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteAccessAsync(pacienteId, usuarioId, role!, OwnershipHelper.NivelResumenSemanal, User.FindFirst("nivel_acceso")?.Value))
            return Forbid();

        _logger.LogInformation("Fetching adherence for paciente: {PacienteId}", pacienteId);
        var result = await _medicamentoService.CalcularAdherenciaAsync(pacienteId, desde, hasta);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/Medicamentos/{id} [WEB + MÓVIL]
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        _logger.LogInformation("Fetching medication by ID: {MedicamentoId}", id);
        var medicamento = await _medicamentoService.ObtenerPorIdAsync(id);
        if (medicamento == null)
        {
            _logger.LogWarning("Medication not found: {MedicamentoId}", id);
            return NotFound();
        }

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteAccessAsync(medicamento.PacienteId, usuarioId, role!, OwnershipHelper.NivelHistorialCompleto))
        {
            _logger.LogWarning("Ownership check failed fetching medication - user: {UserId}, paciente: {PacienteId}", usuarioId, medicamento.PacienteId);
            return Forbid();
        }

        return Ok(new MedicamentoResponse(
            medicamento.Id, medicamento.PacienteId, medicamento.Nombre,
            medicamento.Dosis, medicamento.Horario, medicamento.Notas,
            medicamento.Activo, medicamento.FechaCreacion, medicamento.UltimaToma));
    }

    /// <summary>
    /// POST /api/Medicamentos [WEB]
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Crear([FromBody] CrearMedicamentoRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var paciente = await _pacienteService.GetByIdAsync(request.PacienteId);
        if (paciente == null)
        {
            _logger.LogWarning("Patient not found for medication creation - paciente: {PacienteId}", request.PacienteId);
            return NotFound(new { message = "Paciente no encontrado" });
        }
        if (paciente.UsuarioWebId != usuarioId)
        {
            _logger.LogWarning("Ownership check failed creating medication - user: {UserId}, paciente: {PacienteId}", usuarioId, request.PacienteId);
            return Forbid();
        }

        _logger.LogInformation("Creating medication for paciente: {PacienteId}, name: {Nombre}", request.PacienteId, request.Nombre);
        var medicamento = await _medicamentoService.CrearAsync(
            request.PacienteId,
            InputSanitizer.StripHtml(request.Nombre),
            InputSanitizer.StripHtml(request.Dosis),
            InputSanitizer.StripHtml(request.Horario),
            InputSanitizer.StripHtml(request.Notas));

        _logger.LogInformation("Medication created successfully: {MedicamentoId}", medicamento.Id);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "crear", "medicamentos", medicamento.Id, ip);
        return Ok(new { medicamentoId = medicamento.Id, message = "Medicamento creado" });
    }

    /// <summary>
    /// POST /api/Medicamentos/trigger [MÓVIL/ML]
    /// ML crea "toma tu medicamento" al detectar pico crítico
    /// </summary>
    [HttpPost("trigger")]
    [Authorize(Roles = "dueno,paciente")]
    public async Task<IActionResult> TriggerMedicamento([FromBody] CrearMedicamentoRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!await _ownershipHelper.VerifyPacienteOwnershipAsync(request.PacienteId, usuarioId, role!))
        {
            _logger.LogWarning("Ownership check failed on ML trigger - user: {UserId}, paciente: {PacienteId}", usuarioId, request.PacienteId);
            return Forbid();
        }

        _logger.LogInformation("ML trigger medication for paciente: {PacienteId}, name: {Nombre}", request.PacienteId, request.Nombre);
        var medicamento = await _medicamentoService.CrearAsync(
            request.PacienteId,
            InputSanitizer.StripHtml(request.Nombre),
            InputSanitizer.StripHtml(request.Dosis),
            InputSanitizer.StripHtml(request.Horario),
            InputSanitizer.StripHtml(request.Notas));

        _logger.LogInformation("ML trigger medication created: {MedicamentoId}", medicamento.Id);
        return Ok(new { medicamentoId = medicamento.Id, message = "Medicamento registrado por ML" });
    }

    /// <summary>
    /// PUT /api/Medicamentos/{id} [WEB]
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Editar(string id, [FromBody] ActualizarMedicamentoRequest request)
    {
        _logger.LogInformation("Editing medication: {MedicamentoId}", id);
        var medicamento = await _medicamentoService.ObtenerPorIdAsync(id);
        if (medicamento == null)
        {
            _logger.LogWarning("Medication not found for edit: {MedicamentoId}", id);
            return NotFound();
        }

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var paciente = await _pacienteService.GetByIdAsync(medicamento.PacienteId);
        if (paciente?.UsuarioWebId != usuarioId)
        {
            _logger.LogWarning("Ownership check failed editing medication - user: {UserId}, medicamento: {MedicamentoId}", usuarioId, id);
            return Forbid();
        }

        var result = await _medicamentoService.ActualizarAsync(
            id,
            InputSanitizer.StripHtml(request.Nombre),
            InputSanitizer.StripHtml(request.Dosis),
            InputSanitizer.StripHtml(request.Horario),
            InputSanitizer.StripHtml(request.Notas));
        if (!result)
        {
            _logger.LogWarning("Medication update failed: {MedicamentoId}", id);
            return NotFound();
        }
        _logger.LogInformation("Medication updated successfully: {MedicamentoId}", id);
        return Ok(new { message = "Medicamento actualizado" });
    }

    /// <summary>
    /// PUT /api/Medicamentos/{id}/toma [MÓVIL]
    /// Registrar que el paciente tomó el medicamento
    /// </summary>
    [HttpPut("{id}/toma")]
    public async Task<IActionResult> RegistrarToma(string id)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Registering medication intake: {MedicamentoId}", id);
        var medicamento = await _medicamentoService.ObtenerPorIdAsync(id);
        if (medicamento == null) return NotFound();

        if (!await _ownershipHelper.VerifyPacienteAccessAsync(medicamento.PacienteId, usuarioId, role!, OwnershipHelper.NivelHistorialCompleto))
            return Forbid();

        var result = await _medicamentoService.RegistrarTomaAsync(id);
        _logger.LogInformation("Medication intake registered: {MedicamentoId}", id);
        return Ok(new { message = "Toma registrada" });
    }

    /// <summary>
    /// PUT /api/Medicamentos/{id}/activo [WEB]
    /// </summary>
    [HttpPut("{id}/activo")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> CambiarActivo(string id, [FromBody] bool activo)
    {
        _logger.LogInformation("Changing medication active status: {MedicamentoId}, active: {Activo}", id, activo);
        var medicamento = await _medicamentoService.ObtenerPorIdAsync(id);
        if (medicamento == null)
        {
            _logger.LogWarning("Medication not found for status change: {MedicamentoId}", id);
            return NotFound();
        }

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var paciente = await _pacienteService.GetByIdAsync(medicamento.PacienteId);
        if (paciente?.UsuarioWebId != usuarioId)
        {
            _logger.LogWarning("Ownership check failed changing medication active status - user: {UserId}", usuarioId);
            return Forbid();
        }

        var result = await _medicamentoService.ActivarAsync(id, activo);
        if (!result)
        {
            _logger.LogWarning("Medication activation failed: {MedicamentoId}", id);
            return NotFound();
        }
        _logger.LogInformation("Medication active status changed: {MedicamentoId}, active: {Activo}", id, activo);
        return Ok(new { message = activo ? "Medicamento activado" : "Medicamento desactivado" });
    }

    /// <summary>
    /// DELETE /api/Medicamentos/{id} [WEB]
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "dueno")]
    public async Task<IActionResult> Eliminar(string id)
    {
        _logger.LogInformation("Deleting medication: {MedicamentoId}", id);
        var medicamento = await _medicamentoService.ObtenerPorIdAsync(id);
        if (medicamento == null)
        {
            _logger.LogWarning("Medication not found for deletion: {MedicamentoId}", id);
            return NotFound();
        }

        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var paciente = await _pacienteService.GetByIdAsync(medicamento.PacienteId);
        if (paciente?.UsuarioWebId != usuarioId)
        {
            _logger.LogWarning("Ownership check failed deleting medication - user: {UserId}, medicamento: {MedicamentoId}", usuarioId, id);
            return Forbid();
        }

        var result = await _medicamentoService.EliminarAsync(id);
        if (!result)
        {
            _logger.LogWarning("Medication deletion failed: {MedicamentoId}", id);
            return NotFound();
        }
        _logger.LogInformation("Medication deleted successfully: {MedicamentoId}", id);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "eliminar", "medicamentos", id, ip);
        return NoContent();
    }

}
