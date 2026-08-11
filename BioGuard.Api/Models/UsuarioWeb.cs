using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BioGuard.Api.Models;

public class UsuarioWeb
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("plan_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string PlanId { get; set; } = string.Empty;

    [BsonElement("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [BsonElement("apellido_paterno")]
    public string ApellidoPaterno { get; set; } = string.Empty;

    [BsonElement("apellido_materno")]
    public string ApellidoMaterno { get; set; } = string.Empty;

    [BsonElement("correo")]
    public string Correo { get; set; } = string.Empty;

    [BsonElement("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("foto_perfil")]
    public string? FotoPerfil { get; set; }

    [BsonElement("proveedor_auth")]
    public string ProveedorAuth { get; set; } = "local";

    [BsonElement("rol")]
    public string Rol { get; set; } = Config.SystemRoles.Dueno;

    [BsonElement("google_id")]
    public string? GoogleId { get; set; }

    [BsonElement("two_factor_code")]
    public string? TwoFactorCode { get; set; }

    [BsonElement("two_factor_expira")]
    public DateTime? TwoFactorExpira { get; set; }

    [BsonElement("two_factor_verificado")]
    public bool TwoFactorVerificado { get; set; } = false;

    [BsonElement("two_factor_habilitado")]
    public bool TwoFactorHabilitado { get; set; } = false;

    [BsonElement("reset_password_token")]
    public string? ResetPasswordToken { get; set; }

    [BsonElement("reset_password_expira")]
    public DateTime? ResetPasswordExpira { get; set; }

    [BsonElement("activo")]
    public bool Activo { get; set; } = true;

    [BsonElement("failed_login_attempts")]
    public int FailedLoginAttempts { get; set; } = 0;

    [BsonElement("locked_until")]
    public DateTime? LockedUntil { get; set; }

    [BsonElement("failed_2fa_attempts")]
    public int Failed2FAAttempts { get; set; } = 0;

    [BsonElement("two_factor_locked_until")]
    public DateTime? TwoFactorLockedUntil { get; set; }

    [BsonElement("fecha_registro")]
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}
