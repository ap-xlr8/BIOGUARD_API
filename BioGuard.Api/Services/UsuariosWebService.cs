using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;
using Microsoft.Extensions.Logging;

namespace BioGuard.Api.Services;

public class UsuariosWebService
{
    private readonly IMongoDbContext _db;
    private readonly ILogger<UsuariosWebService> _logger;
    private readonly IImageStorageService _imageStorage;
    private readonly IEmailService _emailService;

    public UsuariosWebService(IMongoDbContext db, ILogger<UsuariosWebService> logger, IImageStorageService imageStorage, IEmailService emailService)
    {
        _db = db;
        _logger = logger;
        _imageStorage = imageStorage;
        _emailService = emailService;
    }

    public async Task<UsuarioWeb?> GetByIdAsync(string id)
    {
        return await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == id);
    }

    public async Task<Plan?> GetPlanAsync(string usuarioId)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == usuarioId);
        if (user == null) return null;
        return await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == user.PlanId);
    }

    public async Task<bool> UpdatePerfilAsync(string usuarioId, UpdatePerfilRequest request)
    {
        var update = Builders<UsuarioWeb>.Update
            .Set(u => u.Nombre, InputSanitizer.StripHtml(request.Nombre))
            .Set(u => u.ApellidoPaterno, InputSanitizer.StripHtml(request.ApellidoPaterno))
            .Set(u => u.ApellidoMaterno, InputSanitizer.StripHtml(request.ApellidoMaterno));

        var result = await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == usuarioId, update);
        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning("Profile update not found or unchanged: {UsuarioId}", usuarioId);
        }
        else
        {
            _logger.LogInformation("Profile updated: {UsuarioId}", usuarioId);
        }
        return result.ModifiedCount > 0;
    }

    public async Task<bool> CambiarCorreoAsync(string usuarioId, string nuevoCorreo, string passwordActual)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == usuarioId);
        if (user == null)
        {
            _logger.LogWarning("Email change failed: user not found: {UsuarioId}", usuarioId);
            return false;
        }

        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            if (!PasswordHasher.Verify(passwordActual, user.PasswordHash))
            {
                _logger.LogWarning("Email change failed: invalid password for user: {UsuarioId}", usuarioId);
                return false;
            }
        }

        var exists = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == nuevoCorreo);
        if (exists != null)
        {
            _logger.LogWarning("Email change failed: email already in use: {NuevoCorreo}", nuevoCorreo);
            return false;
        }

        var update = Builders<UsuarioWeb>.Update.Set(u => u.Correo, nuevoCorreo);
        var result = await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == usuarioId, update);
        _logger.LogInformation("Email changed for user: {UsuarioId}", usuarioId);

        await _emailService.SendPasswordChangedNotificationAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}");

        return result.ModifiedCount > 0;
    }

    public async Task<(bool success, string? url)> SubirFotoAsync(string usuarioId, string fotoBase64)
    {
        var upload = await _imageStorage.UploadAsync(fotoBase64);
        if (!upload.Success)
        {
            _logger.LogWarning("Profile photo upload failed at ImgBB for user: {UsuarioId}", usuarioId);
            return (false, null);
        }

        var update = Builders<UsuarioWeb>.Update.Set(u => u.FotoPerfil, upload.Url);
        var write = await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == usuarioId, update);
        _logger.LogInformation("Profile photo uploaded for user: {UsuarioId}", usuarioId);
        return (write.ModifiedCount > 0, upload.Url);
    }

    public async Task<(bool success, string? url)> SubirFotoPacienteAsync(string pacienteId, string fotoBase64)
    {
        var upload = await _imageStorage.UploadAsync(fotoBase64);
        if (!upload.Success) return (false, null);

        var update = Builders<Paciente>.Update.Set(p => p.Foto, upload.Url);
        var write = await _db.Pacientes.UpdateOneAsync(p => p.Id == pacienteId, update);
        return (write.ModifiedCount > 0, upload.Url);
    }

    public async Task<bool> CambiarPlanAsync(string usuarioId, string planNombre)
    {
        var aliases = PlanCatalog.Aliases(planNombre);
        var plan = await _db.FindFirstOrDefaultAsync(
            _db.Planes, p => aliases.Contains(p.Nombre) && p.Activo);
        if (plan == null)
        {
            _logger.LogWarning("Plan change failed: plan not found: {PlanNombre}", planNombre);
            return false;
        }

        var update = Builders<UsuarioWeb>.Update.Set(u => u.PlanId, plan.Id);
        var result = await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == usuarioId, update);
        _logger.LogInformation("Plan changed to {PlanNombre} for user: {UsuarioId}", planNombre, usuarioId);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> EliminarCuentaAsync(string usuarioId)
    {
        await _db.DeleteManyAsync(_db.Cuidadores, c => c.UsuarioWebId == usuarioId);

        var pacientes = await _db.FindToListAsync(_db.Pacientes, p => p.UsuarioWebId == usuarioId);
        foreach (var paciente in pacientes)
        {
            await _db.DeleteManyAsync(_db.LecturasSensores, l => l.Meta.PacienteId == paciente.Id);
            await _db.DeleteManyAsync(_db.EventosMetabolicos, e => e.PacienteId == paciente.Id);
            await _db.DeleteManyAsync(_db.TrackingGps, t => t.Meta.PacienteId == paciente.Id);
            await _db.DeleteManyAsync(_db.Notificaciones, n => n.PacienteId == paciente.Id);
            await _db.DeleteManyAsync(_db.Dispositivos, d => d.PacienteId == paciente.Id);
            await _db.DeleteManyAsync(_db.Medicamentos, m => m.PacienteId == paciente.Id);
            await _db.DeleteManyAsync(_db.Alertas, a => a.PacienteId == paciente.Id);
        }
        await _db.DeleteManyAsync(_db.Pacientes, p => p.UsuarioWebId == usuarioId);
        await _db.DeleteManyAsync(_db.Pagos, p => p.UsuarioWebId == usuarioId);

        var result = await _db.UsuariosWeb.DeleteOneAsync(u => u.Id == usuarioId);
        if (result.DeletedCount == 0)
        {
            _logger.LogWarning("Account delete not found: {UsuarioId}", usuarioId);
        }
        else
        {
            _logger.LogInformation("Account deleted: {UsuarioId}", usuarioId);
        }
        return result.DeletedCount > 0;
    }

    public async Task<bool> ExistePorEmailAsync(string correo)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == correo);
        return user != null;
    }

    public async Task<List<SesionResponse>> GetSesionesAsync(string usuarioId)
    {
        var tokens = await _db.FindToListAsync(_db.RefreshTokens, t =>
            t.UsuarioId == usuarioId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow);

        return tokens.Select(t => new SesionResponse(
            t.Id ?? (t.Token is not null ? t.Token[..Math.Min(8, t.Token.Length)] : "unknown"),
            null,
            null,
            t.CreatedAt,
            false
        )).ToList();
    }

    public async Task<bool> RevocarSesionAsync(string usuarioId, string sesionId)
    {
        var token = await _db.FindFirstOrDefaultAsync(_db.RefreshTokens, t => t.Id == sesionId);
        if (token == null || token.UsuarioId != usuarioId) return false;

        var update = Builders<RefreshToken>.Update.Set(t => t.RevokedAt, DateTime.UtcNow);
        await _db.RefreshTokens.UpdateOneAsync(t => t.Id == sesionId, update);
        _logger.LogInformation("Session revoked: {SesionId} for user: {UsuarioId}", sesionId, usuarioId);
        return true;
    }

    public virtual async Task<bool> CambiarPasswordAsync(string usuarioId, string passwordActual, string passwordNueva)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == usuarioId);
        if (user == null)
        {
            _logger.LogWarning("Password change failed: user not found: {UsuarioId}", usuarioId);
            return false;
        }

        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            if (!PasswordHasher.Verify(passwordActual, user.PasswordHash))
            {
                _logger.LogWarning("Password change failed: invalid current password for user: {UsuarioId}", usuarioId);
                return false;
            }
        }

        var (passwordValid, _) = PasswordHasher.ValidateComplexity(passwordNueva);
        if (!passwordValid)
        {
            _logger.LogWarning("Password change failed: weak new password for user: {UsuarioId}", usuarioId);
            return false;
        }

        var update = Builders<UsuarioWeb>.Update.Set(u => u.PasswordHash, PasswordHasher.Hash(passwordNueva));
        var result = await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == usuarioId, update);
        _logger.LogInformation("Password changed successfully for user: {UsuarioId}", usuarioId);

        await _emailService.SendPasswordChangedNotificationAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}");
        return result.ModifiedCount > 0;
    }
}
