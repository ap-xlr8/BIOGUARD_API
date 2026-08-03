using System.ComponentModel.DataAnnotations;

namespace BioGuard.Api.DTOs;

public record CrearTicketRequest(
    [Required] [StringLength(100)] string Asunto,
    [Required] [StringLength(2000)] string Descripcion,
    [StringLength(50)] string? Categoria = null,
    [StringLength(20)] string? Prioridad = null);
