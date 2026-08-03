using System.ComponentModel.DataAnnotations;

namespace BioGuard.Api.DTOs;

public record CrearEventoRequest(
    double? Probabilidad,
    [Required] [StringLength(50)] string NivelRiesgo,
    [Required] [StringLength(500)] string Descripcion,
    Dictionary<string, double>? VariablesOrigen = null,
    double? ProbabilidadMl = null);