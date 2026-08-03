using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using Microsoft.Extensions.Logging;

namespace BioGuard.Api.Services;

public class CuidadorService
{
    private readonly IMongoDbContext _db;
    private readonly ILogger<CuidadorService> _logger;

    // QR Code: 8 chars, A-Z 0-9, no ambiguous chars (I, O, 0)
    private const string QR_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int QR_LENGTH = 8;
    private const int QR_EXPIRY_MINUTES = 10;
    private const int MAX_FAILED_ATTEMPTS = 5;
    private const int LOCKOUT_MINUTES = 15;

    public CuidadorService(IMongoDbContext db, ILogger<CuidadorService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<Cuidador>> ObtenerPorUsuarioAsync(string usuarioId)
    {
        return await _db.FindToListAsync(_db.Cuidadores, c => c.UsuarioWebId == usuarioId);
    }

    public async Task<Cuidador?> ObtenerPorIdAsync(string id)
    {
        return await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.Id == id);
    }

    public async Task<List<Cuidador>> ObtenerPorPacienteAsync(string pacienteId)
    {
        return await _db.FindToListAsync(_db.Cuidadores, c => c.PacienteId == pacienteId);
    }

    public async Task<int> ContarPorPacienteAsync(string pacienteId)
    {
        return (int)await _db.CountDocumentsAsync(_db.Cuidadores, c => c.PacienteId == pacienteId);
    }

    public async Task<(bool success, Cuidador? cuidador, string codigo, string? error)> CrearAsync(
        string usuarioId, string pacienteId, string nombre, string parentesco,
        string telefono, string correo, string nivelAcceso = "solo_alertas", int limiteCuidadores = 3)
    {
        var count = await ContarPorPacienteAsync(pacienteId);
        if (count >= limiteCuidadores)
        {
            return (false, null, "", $"Límite de cuidadores alcanzado ({limiteCuidadores})");
        }

        if (!string.IsNullOrWhiteSpace(correo))
        {
            var existente = await _db.FindFirstOrDefaultAsync(_db.Cuidadores,
                c => c.PacienteId == pacienteId && c.Correo == correo);
            if (existente != null)
                return (false, null, "", "Ya existe un cuidador con ese correo para este paciente");
        }

        var codigo = GenerarCodigo();
        var cuidador = new Cuidador
        {
            UsuarioWebId = usuarioId,
            PacienteId = pacienteId,
            CodigoAccesoQr = codigo,
            Nombre = nombre,
            Parentesco = parentesco,
            Telefono = telefono,
            Correo = correo,
            FechaAutorizacion = DateTime.UtcNow,
            NivelAcceso = nivelAcceso,
            CodigoExpira = DateTime.UtcNow.AddMinutes(QR_EXPIRY_MINUTES)
        };

        await _db.Cuidadores.InsertOneAsync(cuidador);
        _logger.LogInformation("Caregiver created: {CuidadorId} for patient: {PacienteId}, nivel: {NivelAcceso}", 
            cuidador.Id, pacienteId, nivelAcceso);
        return (true, cuidador, codigo, null);
    }

    public async Task<bool> ActualizarAsync(string id, string nombre, string parentesco, string? nivelAcceso = null)
    {
        var update = Builders<Cuidador>.Update
            .Set(c => c.Nombre, nombre)
            .Set(c => c.Parentesco, parentesco);

        if (nivelAcceso != null)
        {
            var nivelesValidos = new[] { "solo_alertas", "resumen_semanal", "historial_completo" };
            if (!nivelesValidos.Contains(nivelAcceso))
            {
                _logger.LogWarning("Invalid nivel_acceso in update: {Nivel}", nivelAcceso);
                return false;
            }
            update = update.Set(c => c.NivelAcceso, nivelAcceso);
        }

        var result = await _db.Cuidadores.UpdateOneAsync(c => c.Id == id, update);
        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning("Caregiver update not found or unchanged: {CuidadorId}", id);
        }
        else
        {
            _logger.LogInformation("Caregiver updated: {CuidadorId}", id);
        }
        return result.ModifiedCount > 0;
    }

    public async Task<bool> ActualizarNivelAccesoAsync(string id, string nivelAcceso)
    {
        var nivelesValidos = new[] { "solo_alertas", "resumen_semanal", "historial_completo" };
        if (!nivelesValidos.Contains(nivelAcceso))
        {
            _logger.LogWarning("Invalid nivel_acceso: {Nivel}", nivelAcceso);
            return false;
        }

        var update = Builders<Cuidador>.Update.Set(c => c.NivelAcceso, nivelAcceso);
        var result = await _db.Cuidadores.UpdateOneAsync(c => c.Id == id, update);
        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation("Caregiver access level updated: {CuidadorId} -> {Nivel}", id, nivelAcceso);
        }
        return result.ModifiedCount > 0;
    }

    public async Task<bool> EliminarAsync(string id)
    {
        var cuidador = await ObtenerPorIdAsync(id);
        if (cuidador == null) return false;

        var result = await _db.Cuidadores.DeleteOneAsync(c => c.Id == id);
        if (result.DeletedCount > 0)
        {
            var filter = Builders<RefreshToken>.Filter.Where(t => t.UsuarioId == id);
            var update = Builders<RefreshToken>.Update.Set(t => t.RevokedAt, DateTime.UtcNow);
            await _db.RefreshTokens.UpdateManyAsync(filter, update);
            _logger.LogInformation("Caregiver deleted and sessions revoked: {CuidadorId}", id);
        }
        else
        {
            _logger.LogWarning("Caregiver delete not found: {CuidadorId}", id);
        }
        return result.DeletedCount > 0;
    }

    public async Task<string> RegenerarQRAsync(string id)
    {
        var codigo = GenerarCodigo();
        var expira = DateTime.UtcNow.AddMinutes(QR_EXPIRY_MINUTES);
        var update = Builders<Cuidador>.Update
            .Set(c => c.CodigoAccesoQr, codigo)
            .Set(c => c.CodigoExpira, expira)
            .Set(c => c.IntentosFallidos, 0)
            .Set(c => c.BloqueadoHasta, null);
        await _db.Cuidadores.UpdateOneAsync(c => c.Id == id, update);
        _logger.LogInformation("QR regenerated for caregiver: {CuidadorId}", id);
        return codigo;
    }

    public async Task<(bool success, string? error)> ValidarCodigoAsync(string codigo)
    {
        var cuidador = await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.CodigoAccesoQr == codigo);
        if (cuidador == null)
        {
            return (false, "Código no encontrado");
        }

        if (cuidador.BloqueadoHasta.HasValue && cuidador.BloqueadoHasta > DateTime.UtcNow)
        {
            return (false, "Cuenta bloqueada temporalmente");
        }

        if (cuidador.CodigoExpira.HasValue && cuidador.CodigoExpira < DateTime.UtcNow)
        {
            return (false, "Código expirado");
        }

        // Reset intentos fallidos en éxito
        if (cuidador.IntentosFallidos > 0)
        {
            await _db.Cuidadores.UpdateOneAsync(c => c.Id == cuidador.Id,
                Builders<Cuidador>.Update.Set(c => c.IntentosFallidos, 0));
        }

        return (true, null);
    }

    public async Task RegistrarIntentoFallidoAsync(string codigo)
    {
        var cuidador = await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.CodigoAccesoQr == codigo);
        if (cuidador == null) return;

        var intentos = cuidador.IntentosFallidos + 1;
        var update = Builders<Cuidador>.Update.Set(c => c.IntentosFallidos, intentos);
        
        if (intentos >= MAX_FAILED_ATTEMPTS)
        {
            update = update.Set(c => c.BloqueadoHasta, DateTime.UtcNow.AddMinutes(LOCKOUT_MINUTES));
            _logger.LogWarning("Caregiver code locked after {Attempts} failed attempts: {Codigo}", intentos, codigo);
        }
        
        await _db.Cuidadores.UpdateOneAsync(c => c.Id == cuidador.Id, update);
    }

    private static string GenerarCodigo()
    {
        return new string(Enumerable.Repeat(QR_CHARS, QR_LENGTH)
            .Select(s => s[System.Security.Cryptography.RandomNumberGenerator.GetInt32(s.Length)]).ToArray());
    }
}
