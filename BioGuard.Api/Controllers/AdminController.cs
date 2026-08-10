using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.Services;

namespace BioGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = SystemRoles.Administrador)]
public class AdminController : ControllerBase
{
    private readonly IMongoDbContext _db;
    private readonly AuditoriaService _auditoriaService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IMongoDbContext db, AuditoriaService auditoriaService,
        IEmailService emailService, ILogger<AdminController> logger)
    {
        _db = db;
        _auditoriaService = auditoriaService;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpGet("usuarios")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> ListarUsuarios(
        [FromQuery] string? correo = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int porPagina = 20)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        _logger.LogInformation("Admin {AdminId} listing users, page {Pagina}, correo: {Correo}", adminId, pagina, correo);

        var filterDef = string.IsNullOrEmpty(correo)
            ? Builders<UsuarioWeb>.Filter.Empty
            : Builders<UsuarioWeb>.Filter.Regex(u => u.Correo, new MongoDB.Bson.BsonRegularExpression(Regex.Escape(correo), "i"));

        Expression<Func<UsuarioWeb, bool>> countFilter = string.IsNullOrEmpty(correo)
            ? (u => true)
            : (u => u.Correo.ToLower().Contains(correo.ToLower()));

        var sort = Builders<UsuarioWeb>.Sort.Descending(u => u.FechaRegistro);
        var usuarios = await _db.FindToListAsync(_db.UsuariosWeb, filterDef, sort, porPagina, (pagina - 1) * porPagina);
        var total = (int)await _db.CountDocumentsAsync(_db.UsuariosWeb, countFilter);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(adminId, "buscar_usuarios", "usuarios_web", "admin_search", ip);

        var planes = await _db.FindToListAsync(_db.Planes, _ => true);
        var planesDict = planes.ToDictionary(p => p.Id, p => p.Nombre);
        var response = usuarios.Select(u => new
        {
            id = u.Id,
            correo = u.Correo,
            nombre = $"{u.Nombre} {u.ApellidoPaterno} {u.ApellidoMaterno}".Trim(),
            activo = u.Activo,
            plan = planesDict.TryGetValue(u.PlanId, out var nombre) ? nombre : "Sin plan",
            fechaRegistro = u.FechaRegistro
        });

        return Ok(new { usuarios = response, total, pagina, porPagina });
    }

    [HttpGet("usuarios/{id}")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> GetUsuario(string id)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == id);
        if (user == null) return NotFound();

        var plan = await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == user.PlanId);
        return Ok(new
        {
            id = user.Id,
            correo = user.Correo,
            nombre = $"{user.Nombre} {user.ApellidoPaterno} {user.ApellidoMaterno}".Trim(),
            activo = user.Activo,
            plan = plan?.Nombre ?? "Sin plan",
            fechaRegistro = user.FechaRegistro
        });
    }

    [HttpPut("usuarios/{id}/pausar")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> PausarUsuario(string id, [FromBody] PausarUsuarioRequest request)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        _logger.LogInformation("Admin {AdminId} pausing user {UserId}, pausar: {Pausar}", adminId, id, request.Pausar);

        if (request.Pausar && string.IsNullOrWhiteSpace(request.Motivo))
            return BadRequest(new { message = "Motivo es obligatorio al pausar una cuenta" });

        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == id);
        if (user == null) return NotFound();

        var update = request.Pausar
            ? Builders<UsuarioWeb>.Update
                .Set(u => u.Activo, false)
                .Set(u => u.LockedUntil, DateTime.UtcNow.AddDays(365))
            : Builders<UsuarioWeb>.Update
                .Set(u => u.Activo, true)
                .Set(u => u.LockedUntil, null)
                .Set(u => u.FailedLoginAttempts, 0);

        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == id, update);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(adminId,
            request.Pausar ? "pausar_cuenta" : "reactivar_cuenta",
            "usuarios_web", id, ip);

        if (!string.IsNullOrEmpty(user.Correo))
        {
            var notificacion = request.Pausar
                ? $"Cuenta pausada. Motivo: {request.Motivo}"
                : "Cuenta reactivada.";
            _logger.LogInformation("Notificación para {Correo}: {Notificacion}", user.Correo, notificacion);
        }

        return Ok(new { message = request.Pausar ? "Cuenta pausada" : "Cuenta reactivada" });
    }

    [HttpGet("metricas")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> ObtenerMetricas([FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        _logger.LogInformation("Admin {AdminId} fetching metrics from {Desde} to {Hasta}", adminId, desde, hasta);

        var usuariosActivos = await _db.CountDocumentsAsync(_db.UsuariosWeb, u => u.Activo);

        var pacientesActivos = desde.HasValue
            ? (int)await _db.CountDocumentsAsync(_db.Pacientes, (System.Linq.Expressions.Expression<Func<Paciente, bool>>)(p => p.FechaRegistro >= desde.Value))
            : (int)await _db.CountDocumentsAsync(_db.Pacientes, (System.Linq.Expressions.Expression<Func<Paciente, bool>>)(_ => true));

        var hastaFin = hasta ?? DateTime.UtcNow;
        var alertasCriticas = await _db.CountDocumentsAsync(_db.Alertas,
            a => a.Nivel == "critico" && a.FechaCreacion >= (desde ?? DateTime.UtcNow.Date) && a.FechaCreacion <= hastaFin);

        var planes = await _db.FindToListAsync(_db.Planes, p => p.Activo);
        var distribucionPlanes = new Dictionary<string, int>();
        foreach (var plan in planes)
        {
            var count = await _db.CountDocumentsAsync(_db.UsuariosWeb, u => u.PlanId == plan.Id);
            distribucionPlanes[plan.Nombre.Replace(" ", "")] = (int)count;
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(adminId, "ver_metricas", "admin", "metrics", ip);

        return Ok(new
        {
            usuariosActivos,
            pacientesActivos,
            alertasCriticasDesde = alertasCriticas,
            distribucionPlanes
        });
    }

    [HttpGet("tickets")]
    [Authorize(Roles = "administrador")]
    public async Task<IActionResult> ListarTickets(
        [FromQuery] string? estado = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int porPagina = 50)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        _logger.LogInformation("Admin {AdminId} listing tickets, estado: {Estado}, page {Pagina}", adminId, estado ?? "todos", pagina);

        var filter = string.IsNullOrEmpty(estado)
            ? Builders<TicketSoporte>.Filter.Empty
            : Builders<TicketSoporte>.Filter.Eq(t => t.Estado, estado);

        var sort = Builders<TicketSoporte>.Sort.Descending(t => t.FechaCreacion);
        var tickets = await _db.FindToListAsync(_db.TicketsSoporte, filter, sort, porPagina, (pagina - 1) * porPagina);

        var response = tickets.Select(t => new
        {
            id = t.Id,
            usuarioId = t.UsuarioId,
            asunto = t.Asunto,
            estado = t.Estado,
            fechaCreacion = t.FechaCreacion
        });

        return Ok(response);
    }
}

public record PausarUsuarioRequest(
    bool Pausar,
    [StringLength(500)] string? Motivo);
