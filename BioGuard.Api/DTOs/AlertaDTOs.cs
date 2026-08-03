using System.ComponentModel.DataAnnotations;

namespace BioGuard.Api.DTOs;

public record AlertaResponse(
    string Id, string PacienteId, string Tipo, string Nivel,
    string Titulo, string Mensaje, bool Atendida,
    DateTime FechaCreacion, DateTime? FechaAtencion);

public record CrearAlertaRequest(
    [Required] string PacienteId,
    string? Tipo,
    string? Nivel,
    string? Titulo,
    string? Mensaje,
    int? PulsoBpm,
    double? TemperaturaC,
    double? SudoracionGsr,
    double? ProbabilidadPico,
    string? TipoAlerta = null,
    string? Descripcion = null);

public record ResolverAlertaRequest(
    [Required] string CuidadorId,
    [StringLength(500)] string? AccionTomada);

public record AtenderAlertaRequest(
    [Required] string NotasAtencion);
