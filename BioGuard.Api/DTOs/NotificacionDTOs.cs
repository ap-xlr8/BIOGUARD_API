using System.ComponentModel.DataAnnotations;

namespace BioGuard.Api.DTOs;

public record CrearNotificacionRequest(
    [Required] [StringLength(100)] string PacienteId,
    [Required] [StringLength(200)] string Titulo,
    [Required] [StringLength(1000)] string Mensaje,
    [Required] [StringLength(50)] string Tipo,
    [StringLength(100)] string? CuidadorId = null,
    [StringLength(100)] string? UsuarioWebId = null);

public record RegisterFcmTokenRequest(
    [Required] string Token);