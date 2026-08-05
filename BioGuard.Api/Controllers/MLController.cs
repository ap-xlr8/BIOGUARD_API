using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;
using BioGuard.Api.Config;

namespace BioGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MLController : ControllerBase
{
    private readonly MLService _mlService;
    private readonly IMongoDbContext _db;
    private readonly OwnershipHelper _ownershipHelper;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<MLController> _logger;

    public MLController(MLService mlService, IMongoDbContext db,
        OwnershipHelper ownershipHelper, AuditoriaService auditoriaService,
        ILogger<MLController> logger)
    {
        _mlService = mlService;
        _db = db;
        _ownershipHelper = ownershipHelper;
        _auditoriaService = auditoriaService;
        _logger = logger;
    }

    private string? GetUsuarioId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string? GetRole() => User.FindFirst(ClaimTypes.Role)?.Value;

    private string? GetNivelAcceso() => User.FindFirst("nivel_acceso")?.Value;

    private async Task<bool> VerifyAiConsoleAccessAsync(string usuarioId)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == usuarioId);
        if (user == null) return false;
        var plan = await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == user.PlanId);
        return plan?.AiConsole == true;
    }

    // GET /api/ML/predicciones/{pacienteId}

    [HttpGet("predicciones/{pacienteId}")]
    public async Task<IActionResult> ObtenerPredicciones(string pacienteId)
    {
        var usuarioId = GetUsuarioId();
        var role = GetRole();
        if (string.IsNullOrEmpty(usuarioId) || string.IsNullOrEmpty(role)) return Unauthorized();
        if (!await _ownershipHelper.VerifyPacienteAccessAsync(pacienteId, usuarioId, role, OwnershipHelper.NivelResumenSemanal, GetNivelAcceso())) return Forbid();

        _logger.LogInformation("Getting ML predictions for patient {PacienteId}", pacienteId);
        var predicciones = await _mlService.ObtenerPrediccionesAsync(pacienteId);
        var response = predicciones.Select(p => new
        {
            id = p.Id,
            probabilidad = p.ProbabilidadPico,
            nivelRiesgo = p.NivelRiesgo,
            recomendacion = p.Recomendacion,
            fechaPrediccion = p.FechaPrediccion,
            horasEstimadas = p.HorasEstimadas,
            modeloVersion = p.ModeloVersion
        });
        return Ok(response);
    }

    // GET /api/ML/predicciones/{pacienteId}/actual

    [HttpGet("predicciones/{pacienteId}/actual")]
    public async Task<IActionResult> PrediccionActual(string pacienteId)
    {
        var usuarioId = GetUsuarioId();
        var role = GetRole();
        if (string.IsNullOrEmpty(usuarioId) || string.IsNullOrEmpty(role)) return Unauthorized();
        if (!await _ownershipHelper.VerifyPacienteAccessAsync(pacienteId, usuarioId, role, OwnershipHelper.NivelResumenSemanal, GetNivelAcceso())) return Forbid();

        _logger.LogInformation("Getting current ML prediction for patient {PacienteId}", pacienteId);
        var prediccion = await _mlService.ObtenerPrediccionActualAsync(pacienteId);
        if (prediccion == null)
        {
            _logger.LogWarning("No active prediction for patient {PacienteId}", pacienteId);
            return Ok(new { message = "Sin predicción activa" });
        }
        return Ok(new
        {
            id = prediccion.Id,
            probabilidad = prediccion.ProbabilidadPico,
            nivelRiesgo = prediccion.NivelRiesgo,
            recomendacion = prediccion.Recomendacion,
            fechaPrediccion = prediccion.FechaPrediccion,
            horasEstimadas = prediccion.HorasEstimadas
        });
    }

    // GET /api/ML/recomendaciones/{pacienteId}

    [HttpGet("recomendaciones/{pacienteId}")]
    public async Task<IActionResult> Recomendaciones(string pacienteId)
    {
        var usuarioId = GetUsuarioId();
        var role = GetRole();
        if (string.IsNullOrEmpty(usuarioId) || string.IsNullOrEmpty(role)) return Unauthorized();
        if (!await _ownershipHelper.VerifyPacienteAccessAsync(pacienteId, usuarioId, role, OwnershipHelper.NivelResumenSemanal, GetNivelAcceso())) return Forbid();

        _logger.LogInformation("Getting recommendations for patient {PacienteId}", pacienteId);
        var recomendaciones = await _mlService.ObtenerRecomendacionesAsync(pacienteId);
        return Ok(new { recomendaciones });
    }

    // GET /api/ML/modelos

    [HttpGet("modelos")]
    public async Task<IActionResult> ListarModelos()
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        var role = GetRole();
        if (role != "dueno") return Forbid();
        if (!await VerifyAiConsoleAccessAsync(usuarioId)) return Forbid();

        _logger.LogInformation("Listing ML models");
        var modelos = await _mlService.ObtenerModelosAsync();
        var response = modelos.Select(m => new
        {
            id = m.Id,
            version = m.Version,
            accuracy = m.Accuracy,
            precision = m.Precision,
            recall = m.Recall,
            f1Score = m.F1Score,
            activo = m.Activo,
            totalMuestras = m.TotalMuestras,
            fechaEntrenamiento = m.FechaEntrenamiento,
            descripcion = m.Descripcion
        });
        return Ok(response);
    }

    // POST /api/ML/entrenar

    [HttpPost("entrenar")]
    public async Task<IActionResult> EntrenarModelo([FromBody] EntrenarModeloRequest request)
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        var role = GetRole();
        if (role != "dueno") return Forbid();
        if (!await VerifyAiConsoleAccessAsync(usuarioId)) return Forbid();

        _logger.LogInformation("Starting ML model training, version {Version}", request.Version);
        var modelo = new ModeloMl
        {
            Version = request.Version,
            Accuracy = 0.0,
            Activo = false,
            FechaEntrenamiento = DateTime.UtcNow,
            Descripcion = request.Descripcion
        };

        var result = await _mlService.CrearModeloAsync(modelo);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "entrenar_modelo", "modelos_ml", result.Id, ip);
        return Ok(new { modeloId = result.Id, message = "Entrenamiento iniciado" });
    }

    // POST /api/ML/reentrenar

    [HttpPost("reentrenar")]
    public async Task<IActionResult> ReentrenarModelo([FromBody] EntrenarModeloRequest request)
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        var role = GetRole();
        if (role != "dueno") return Forbid();
        if (!await VerifyAiConsoleAccessAsync(usuarioId)) return Forbid();

        _logger.LogInformation("Starting ML model retraining, version {Version}", request.Version);
        var modeloActivo = await _mlService.ObtenerModeloActivoAsync();

        var modelo = new ModeloMl
        {
            Version = request.Version,
            Accuracy = modeloActivo?.Accuracy ?? 0.0,
            Precision = modeloActivo?.Precision ?? 0.0,
            Recall = modeloActivo?.Recall ?? 0.0,
            F1Score = modeloActivo?.F1Score ?? 0.0,
            Activo = false,
            FechaEntrenamiento = DateTime.UtcNow,
            Descripcion = request.Descripcion
        };

        var result = await _mlService.CrearModeloAsync(modelo);

        if (modeloActivo != null)
        {
            await _mlService.DesactivarModeloAsync(modeloActivo.Id);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "reentrenar_modelo", "modelos_ml", result.Id, ip);
        return Ok(new { modeloId = result.Id, message = "Re-entrenamiento iniciado" });
    }

    // POST /api/ML/diagnosticar

    [HttpPost("diagnosticar")]
    public async Task<IActionResult> Diagnosticar([FromBody] DiagnosticarRequest request)
    {
        var usuarioId = GetUsuarioId();
        var role = GetRole();
        if (string.IsNullOrEmpty(usuarioId) || string.IsNullOrEmpty(role)) return Unauthorized();
        if (!await _ownershipHelper.VerifyPacienteAccessAsync(request.PacienteId, usuarioId, role, OwnershipHelper.NivelResumenSemanal, GetNivelAcceso())) return Forbid();

        _logger.LogInformation("Running ML diagnosis for patient {PacienteId}", request.PacienteId);
        var predicciones = await _mlService.ObtenerPrediccionesAsync(request.PacienteId);
        var prediccion = predicciones.FirstOrDefault();

        if (prediccion == null)
        {
            _logger.LogWarning("Insufficient data for diagnosis on patient {PacienteId}", request.PacienteId);
            return Ok(new { message = "Sin datos suficientes para diagnóstico" });
        }

        return Ok(new
        {
            pacienteId = request.PacienteId,
            nivelRiesgo = prediccion.NivelRiesgo,
            probabilidad = prediccion.ProbabilidadPico,
            recomendacion = prediccion.Recomendacion,
            horasEstimadas = prediccion.HorasEstimadas,
            fechaPrediccion = prediccion.FechaPrediccion,
            modeloVersion = prediccion.ModeloVersion
        });
    }

    // GET /api/ML/metricas/{modeloId}

    [HttpGet("metricas/{modeloId}")]
    public async Task<IActionResult> MetricasModelo(string modeloId)
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        var role = GetRole();
        if (role != "dueno") return Forbid();
        if (!await VerifyAiConsoleAccessAsync(usuarioId)) return Forbid();

        _logger.LogInformation("Getting metrics for model {ModeloId}", modeloId);
        var modelo = await _mlService.ObtenerMetricasAsync(modeloId);
        if (modelo == null)
        {
            _logger.LogWarning("Model {ModeloId} not found when getting metrics", modeloId);
            return NotFound();
        }

        return Ok(new
        {
            version = modelo.Version,
            accuracy = modelo.Accuracy,
            precision = modelo.Precision,
            recall = modelo.Recall,
            f1 = modelo.F1Score,
            totalMuestras = modelo.TotalMuestras,
            fechaEntrenamiento = modelo.FechaEntrenamiento
        });
    }
}
