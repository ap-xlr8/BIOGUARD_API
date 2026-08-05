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
            p.GpsContinuo, p.AiConsole, p.Descripcion
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
            plan.GpsContinuo, plan.AiConsole, plan.Descripcion
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
        var planes = new List<Plan>
        {
            new()
            {
                Nombre = "Gratis", Precio = 0m, PrecioMoneda = "MXN",
                LimitePacientes = 1, LimiteCuidadores = 0, DiasHistorial = 30,
                GpsContinuo = false, AiConsole = false, Activo = true, Orden = 1,
                Descripcion = "Plan gratuito actualizado"
            },
            new()
            {
                Nombre = "Familiar", Precio = 10m, PrecioMoneda = "MXN",
                LimitePacientes = 1, LimiteCuidadores = 3, DiasHistorial = 15,
                GpsContinuo = false, AiConsole = false, Activo = true, Orden = 2,
                Descripcion = "Plan familiar con GPS y hasta 3 cuidadores"
            },
            new()
            {
                Nombre = "Pro", Precio = 20m, PrecioMoneda = "MXN",
                LimitePacientes = 1, LimiteCuidadores = 6, DiasHistorial = 30,
                GpsContinuo = true, AiConsole = true, Activo = true, Orden = 3,
                Descripcion = "Plan profesional con AI Console y funciones avanzadas"
            }
        };

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
    // One-time endpoint to update existing plans pricing + limits

    [HttpPost("migrate-prices")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> MigratePrices()
    {
        _logger.LogInformation("Migrating plan prices to MXN");
        var planUpdates = new Dictionary<string, (decimal Precio, int Cuidadores, string Desc)>
        {
            ["Gratis"] = (0m, 0, "Plan gratuito actualizado"),
            ["Familiar"] = (10m, 3, "Plan familiar con GPS y hasta 3 cuidadores"),
            ["Pro"] = (20m, 6, "Plan profesional con AI Console y funciones avanzadas")
        };

        var updated = 0;
        foreach (var (nombre, (precio, cuidadores, desc)) in planUpdates)
        {
            var update = Builders<Plan>.Update
                .Set(p => p.Precio, precio)
                .Set(p => p.PrecioMoneda, "MXN")
                .Set(p => p.LimiteCuidadores, cuidadores)
                .Set(p => p.Descripcion, desc);
            var result = await _db.Planes.UpdateOneAsync(p => p.Nombre == nombre, update);
            updated += (int)result.ModifiedCount;
        }

        _logger.LogInformation("Plan price migration completed, {Updated} plans updated", updated);
        return Ok(new { message = $"Planes actualizados a MXN", updated });
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
    [Required] string Descripcion = "",
    int Orden = 1);

