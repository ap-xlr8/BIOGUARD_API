using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.DTOs;
using BioGuard.Api.Services;
using MongoDB.Driver;

namespace BioGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly IMongoDbContext _db;
    private readonly ILogger<TicketsController> _logger;

    public TicketsController(IMongoDbContext db, ILogger<TicketsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearTicketRequest request)
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Creating ticket for user {UsuarioId}, subject: {Asunto}", usuarioId, request.Asunto);

        var ticket = new TicketSoporte
        {
            UsuarioId = usuarioId,
            Asunto = InputSanitizer.StripHtml(request.Asunto),
            Descripcion = InputSanitizer.StripHtml(request.Descripcion),
            Categoria = InputSanitizer.StripHtml(request.Categoria ?? "soporte_general"),
            Prioridad = InputSanitizer.StripHtml(request.Prioridad ?? "normal"),
            Estado = "abierto",
            FechaCreacion = DateTime.UtcNow,
            FechaActualizacion = DateTime.UtcNow,
            Mensajes = new List<MensajeSoporte>
            {
                new MensajeSoporte
                {
                    AutorId = usuarioId,
                    AutorNombre = User.FindFirst(ClaimTypes.Name)?.Value ?? "Usuario",
                    Contenido = InputSanitizer.StripHtml(request.Descripcion),
                    Fecha = DateTime.UtcNow,
                    EsAdmin = false
                }
            }
        };

        await _db.TicketsSoporte.InsertOneAsync(ticket);

        return Ok(new { ticketId = ticket.Id, message = "Ticket creado exitosamente" });
    }

    [HttpGet("mis-tickets")]
    public async Task<IActionResult> ListarMisTickets()
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Listing tickets for user {UsuarioId}", usuarioId);

        var filter = Builders<TicketSoporte>.Filter.Eq(t => t.UsuarioId, usuarioId);
        var sort = Builders<TicketSoporte>.Sort.Descending(t => t.FechaCreacion);
        var tickets = await _db.FindToListAsync(_db.TicketsSoporte, filter, sort, 100, 0);

        var response = tickets.Select(t => new
        {
            id = t.Id,
            asunto = t.Asunto,
            descripcion = t.Descripcion,
            categoria = t.Categoria,
            prioridad = t.Prioridad,
            estado = t.Estado,
            fechaCreacion = t.FechaCreacion,
            fechaActualizacion = t.FechaActualizacion
        });

        return Ok(response);
    }
}
