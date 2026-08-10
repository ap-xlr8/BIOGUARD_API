using System.ComponentModel.DataAnnotations;

namespace BioGuard.Api.DTOs;

// ── Auth ──────────────────────────────────────────────────

public record RegisterWebRequest(
    [Required] [StringLength(100)] string Nombre,
    [Required] [StringLength(100)] string ApellidoPaterno,
    [Required] [EmailAddress] string Correo,
    [Required] [MinLength(8)] [StringLength(128)] string Password,
    [Required] [StringLength(100)] string PlanNombre,
    [StringLength(100)] string ApellidoMaterno = "");

public record LoginWebRequest(
    [Required] [EmailAddress] string Correo,
    [Required] [StringLength(128)] string Password);

public record LoginGoogleRequest(
    [Required] [StringLength(4096)] string IdToken);

public record LoginCodigoRequest(
    [Required] [StringLength(50)] string CodigoAcceso);

public record LoginCodigoResponse(
    string AccessToken, string RefreshToken, string UserId,
    string Nombre, string Rol);

public record AuthResponse(string Token, string UserId, string Nombre, string Rol, string Plan, bool Requires2FA = false, bool RequiresVerification = false, string? RefreshToken = null);

public record RefreshTokenRequest(
    [StringLength(512)] string RefreshToken = "");

public record RefreshTokenResponse(string AccessToken, string RefreshToken);

public record Enviar2FARequest(
    [Required] [EmailAddress] string Correo);

public record Verificar2FARequest(
    [Required] [EmailAddress] string Correo,
    string? Codigo = null,
    string? CodigoOtp = null);

public record ForgotPasswordRequest(
    [Required] [EmailAddress] string Correo);

public record ResetPasswordRequest(
    [Required] string Token,
    [Required] [EmailAddress] string Correo,
    [Required] [MinLength(8)] [StringLength(128)] string NuevaPassword);

public record CambiarPasswordRequest(
    [Required] string PasswordActual,
    [Required] [MinLength(8)] [StringLength(128)] string NuevaPassword);

public record LogoutAllRequest(string? UsuarioObjetivoId = null);

// ── Pacientes ─────────────────────────────────────────────

public record PacienteResponse(
    string Id, string Nombre, bool EsDiabetico,
    bool PerfilCompletado, string CodigoAccesoQr = "");

public record UpdateBiometriaRequest(
    [Required] DateTime FechaNacimiento,
    [Required] [RegularExpression("M|F|Otro")] string Sexo,
    [Range(0.1, 500)] double PesoKg,
    [Range(20, 300)] double EstaturaCm,
    bool EsDiabetico, bool FamiliaresDiabetes,
    [StringLength(50)] string ActividadFisica);

public record CrearPacienteRequest(
    [Required] [StringLength(200)] string Nombre);
public record UpdateNombreRequest(
    [Required] [StringLength(200)] string Nombre);

// ── Cuidadores ────────────────────────────────────────────

public record CuidadorResponse(
    string Id, string Nombre, string Parentesco,
    string PacienteId, string NivelAcceso = "solo_alertas", string CodigoAccesoQr = "");

public record CrearCuidadorRequest(
    [Required] string PacienteId,
    [Required] [StringLength(200)] string Nombre,
    [Required] [StringLength(100)] string Parentesco,
    [Required] [Phone] string Telefono,
    [Required] [EmailAddress] string Correo,
    string? NivelAcceso = null);

public record ActualizarNivelAccesoRequest(
    [Required] [RegularExpression("solo_alertas|resumen_semanal|historial_completo")] string NivelAcceso);

public record ActualizarCuidadorRequest(
    [Required] [StringLength(200)] string Nombre,
    [Required] [StringLength(100)] string Parentesco,
    [RegularExpression("solo_alertas|resumen_semanal|historial_completo")] string? NivelAcceso = null);

// ── Sensores ──────────────────────────────────────────────

public record LecturaSensorRequest(
    [Range(20, 300)] int PulsoBpm,
    [Range(30.0, 45.0)] double TemperaturaC,
    [Range(0.0, 100.0)] double SudoracionGsr,
    [Range(0, 200)] double? Hrv = null,
    [Range(50, 100)] int? Spo2 = null,
    DateTime? Timestamp = null,
    [StringLength(50)] string? DispositivoMac = null,
    bool? EsSimulado = null,
    [StringLength(200)] string? SourceMessageId = null);

public record EventoMetabolicoResponse(
    string Id, string NivelRiesgo, double ProbabilidadMl,
    string Descripcion, DateTime FechaEvento, bool Atendida);

public record AtenderEventoRequest(
    [Required] [StringLength(100)] string CuidadorId);

public record TrackingGpsRequest(
    [Range(-180.0, 180.0)] double Longitud,
    [Range(-90.0, 90.0)] double Latitud,
    bool EsEmergencia,
    [StringLength(50)] string? DispositivoMac = null,
    [StringLength(200)] string? SourceMessageId = null);

public record TrackingResponse(
    double Longitud, double Latitud, DateTime Timestamp, bool EsEmergencia);

// ── Notificaciones ────────────────────────────────────────

public record NotificacionResponse(
    string Id, string Titulo, string Mensaje, bool Leida, DateTime FechaEnvio);

// ── Planes ────────────────────────────────────────────────

public record PlanResponse(
    string Id, string Nombre, decimal Precio, string PrecioMoneda,
    int LimitePacientes, int LimiteCuidadores, int DiasHistorial,
    bool GpsContinuo, bool AiConsole, string Descripcion,
    bool GuardianNocturnoDisponible = false,
    bool ExportacionReportesDisponible = false);

public record EffectiveAccessResponse(
    string Rol,
    string? PacienteId,
    string? NivelAccesoCuidador,
    bool CuidadorDentroDelPlan,
    PlanResponse? Plan,
    IReadOnlyCollection<string> Permisos);

// ── Usuarios Web ──────────────────────────────────────────

public record UpdatePerfilRequest(
    [Required] [StringLength(100)] string Nombre,
    [Required] [StringLength(100)] string ApellidoPaterno,
    [StringLength(100)] string ApellidoMaterno);

public record CambiarCorreoRequest(
    [Required] [EmailAddress] string NuevoCorreo,
    [Required] [StringLength(128)] string PasswordActual);

// ── Pagos ─────────────────────────────────────────────────

public record CrearSesionPagoRequest(
    [Required] [StringLength(100)] string PlanNombre,
    [StringLength(20)] string Procesador = "stripe");

public record PagoResponse(
    string Id, decimal Monto, string Moneda, string Estado,
    DateTime FechaPago, string MetodoPago);

// ── ML ────────────────────────────────────────────────────

// ── Sesiones ─────────────────────────────────────────────

public record SesionResponse(
    string SesionId, string? Dispositivo, string? Navegador,
    DateTime UltimaActividad, bool Actual);

// ── Dispositivos ──────────────────────────────────────────

public record VincularDispositivoRequest(
    [Required] [StringLength(200)] string Nombre,
    [Required] [StringLength(64)] string MacAddress);
