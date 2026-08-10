using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;
using BioGuard.Api.Services;

namespace BioGuard.Api.Controllers;

/// <summary>
/// MÓDULO 2: Planes de Suscripción (catálogo)
/// ENDPOINT WEB
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PlanesController : ControllerBase
{
    private readonly IMongoDbContext _db;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<PlanesController> _logger;

    public PlanesController(IMongoDbContext db, AuditoriaService auditoriaService, ILogger<PlanesController> logger)
    {
        _db = db;
        _auditoriaService = auditoriaService;
        _logger = logger;
    }

    // ── Consulta ──────────────────────────────────────────────
    // GET /api/Planes [WEB]

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        _logger.LogInformation("Listing active plans");
        var filter = Builders<Plan>.Filter.Eq(p => p.Activo, true);
        var sort = Builders<Plan>.Sort.Ascending(p => p.Orden);
        var planes = await _db.FindToListAsync(_db.Planes, filter, sort);

        var response = planes.Select(p => new PlanResponse(
            p.Id, p.Nombre, p.Precio, p.PrecioMoneda,
            p.LimitePacientes, p.LimiteCuidadores, p.DiasHistorial,
            p.GpsContinuo, p.AiConsole, p.Descripcion,
            p.GuardianNocturnoDisponible, p.ExportacionReportesDisponible
        )).ToList();

        return Ok(response);
    }

    // GET /api/Planes/{id} [WEB]

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        _logger.LogInformation("Getting plan {Id}", id);
        var plan = await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == id);
        if (plan == null)
        {
            _logger.LogWarning("Plan {Id} not found", id);
            return NotFound();
        }

        return Ok(new PlanResponse(
            plan.Id, plan.Nombre, plan.Precio, plan.PrecioMoneda,
            plan.LimitePacientes, plan.LimiteCuidadores, plan.DiasHistorial,
            plan.GpsContinuo, plan.AiConsole, plan.Descripcion,
            plan.GuardianNocturnoDisponible, plan.ExportacionReportesDisponible
        ));
    }

    // ── Alta / Edición ────────────────────────────────────────
    // POST /api/Planes [WEB] - Admin

    [HttpPost]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> Crear([FromBody] CrearPlanRequest request)
    {
        _logger.LogInformation("Creating plan {Nombre}", request.Nombre);
        var plan = new Plan
        {
            Nombre = request.Nombre,
            Precio = request.Precio,
            PrecioMoneda = request.PrecioMoneda,
            LimitePacientes = request.LimitePacientes,
            LimiteCuidadores = request.LimiteCuidadores,
            DiasHistorial = request.DiasHistorial,
            GpsContinuo = request.GpsContinuo,
            AiConsole = request.AiConsole,
            GuardianNocturnoDisponible = request.GuardianNocturnoDisponible,
            ExportacionReportesDisponible = request.ExportacionReportesDisponible,
            Descripcion = request.Descripcion,
            Activo = true,
            Orden = request.Orden
        };

        await _db.Planes.InsertOneAsync(plan);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync("admin", "crear_plan", "planes", plan.Id, ip);
        return Ok(new { planId = plan.Id, message = "Plan creado" });
    }

    // PUT /api/Planes/{id} [WEB] - Admin

    [HttpPut("{id}")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> Editar(string id, [FromBody] CrearPlanRequest request)
    {
        _logger.LogInformation("Updating plan {Id}", id);
        var update = Builders<Plan>.Update
            .Set(p => p.Nombre, request.Nombre)
            .Set(p => p.Precio, request.Precio)
            .Set(p => p.PrecioMoneda, request.PrecioMoneda)
            .Set(p => p.LimitePacientes, request.LimitePacientes)
            .Set(p => p.LimiteCuidadores, request.LimiteCuidadores)
            .Set(p => p.DiasHistorial, request.DiasHistorial)
            .Set(p => p.GpsContinuo, request.GpsContinuo)
            .Set(p => p.AiConsole, request.AiConsole)
            .Set(p => p.GuardianNocturnoDisponible, request.GuardianNocturnoDisponible)
            .Set(p => p.ExportacionReportesDisponible, request.ExportacionReportesDisponible)
            .Set(p => p.Descripcion, request.Descripcion)
            .Set(p => p.Orden, request.Orden);

        var result = await _db.Planes.UpdateOneAsync(p => p.Id == id, update);
        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning("Plan {Id} not found when attempting to update", id);
            return NotFound();
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync("admin", "editar_plan", "planes", id, ip);
        return Ok(new { message = "Plan actualizado" });
    }

    // DELETE /api/Planes/{id} [WEB] - Admin

    [HttpDelete("{id}")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> Eliminar(string id)
    {
        _logger.LogInformation("Deactivating plan {Id}", id);
        var update = Builders<Plan>.Update.Set(p => p.Activo, false);
        var result = await _db.Planes.UpdateOneAsync(p => p.Id == id, update);
        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning("Plan {Id} not found when attempting to deactivate", id);
            return NotFound();
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync("admin", "desactivar_plan", "planes", id, ip);
        return Ok(new { message = "Plan desactivado" });
    }

    // POST /api/Planes/seed [WEB] - Admin

    [HttpPost("seed")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> Seed()
    {
        var exists = await _db.FindToListAsync(_db.Planes, p => p.Activo == true);
        if (exists.Any())
        {
            _logger.LogWarning("Seed aborted: active plans already exist");
            return BadRequest(new { message = "Ya existen planes activos" });
        }

        _logger.LogInformation("Seeding default plans");
        var planes = PlanCatalog.CreateDefaultPlans();

        foreach (var plan in planes)
        {
            await _db.Planes.InsertOneAsync(plan);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync("admin", "seed_planes", "planes", "seed", ip);
        _logger.LogInformation("Seeded {Count} plans", planes.Count);
        return Ok(new { message = "Planes sembrados", total = planes.Count });
    }

    // POST /api/Planes/migrate-prices [WEB] - Admin
    // Idempotent endpoint that reconciles legacy names without changing plan IDs.

    [HttpPost("migrate-prices")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> MigratePrices()
    {
        _logger.LogInformation("Reconciling production plan catalog");
        var updated = 0;
        var inserted = 0;

        foreach (var canonical in PlanCatalog.CreateDefaultPlans())
        {
            var aliases = PlanCatalog.Aliases(canonical.Nombre);
            var existing = await _db.FindFirstOrDefaultAsync(
                _db.Planes,
                plan => aliases.Contains(plan.Nombre));

            if (existing == null)
            {
                await _db.Planes.InsertOneAsync(canonical);
                inserted++;
                continue;
            }

            var update = Builders<Plan>.Update
                .Set(p => p.Nombre, canonical.Nombre)
                .Set(p => p.Precio, canonical.Precio)
                .Set(p => p.PrecioMoneda, canonical.PrecioMoneda)
                .Set(p => p.LimitePacientes, canonical.LimitePacientes)
                .Set(p => p.LimiteCuidadores, canonical.LimiteCuidadores)
                .Set(p => p.DiasHistorial, canonical.DiasHistorial)
                .Set(p => p.GpsContinuo, canonical.GpsContinuo)
                .Set(p => p.AiConsole, canonical.AiConsole)
                .Set(p => p.GuardianNocturnoDisponible, canonical.GuardianNocturnoDisponible)
                .Set(p => p.ExportacionReportesDisponible, canonical.ExportacionReportesDisponible)
                .Set(p => p.Descripcion, canonical.Descripcion)
                .Set(p => p.Activo, true)
                .Set(p => p.Orden, canonical.Orden);

            var result = await _db.Planes.UpdateOneAsync(p => p.Id == existing.Id, update);
            updated += (int)result.ModifiedCount;
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync("admin", "migrar_catalogo_planes", "planes", "catalogo-produccion", ip);
        _logger.LogInformation(
            "Plan catalog reconciliation completed: {Updated} updated, {Inserted} inserted",
            updated,
            inserted);
        return Ok(new { message = "Catalogo de planes reconciliado", updated, inserted });
    }
}

public record CrearPlanRequest(
    [Required] string Nombre,
    [Required] decimal Precio,
    string PrecioMoneda = "MXN",
    int LimitePacientes = 1,
    int LimiteCuidadores = 0,
    int DiasHistorial = 30,
    bool GpsContinuo = false,
    bool AiConsole = false,
    bool GuardianNocturnoDisponible = false,
    bool ExportacionReportesDisponible = false,
    [Required] string Descripcion = "",
    int Orden = 1);

