using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;
using Microsoft.Extensions.Logging;

namespace BioGuard.Api.Services;

public class PacienteService
{
    private readonly IMongoDbContext _db;
    private readonly ILogger<PacienteService> _logger;

    private const int QR_EXPIRY_MINUTES = 10;

    public PacienteService(IMongoDbContext db, ILogger<PacienteService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Paciente?> GetByCodigoAsync(string codigo)
    {
        return await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.CodigoAccesoQr == codigo);
    }

    public async Task<Paciente?> GetByIdAsync(string id)
    {
        return await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == id);
    }

    public async Task<List<Paciente>> GetAllByUsuarioAsync(string usuarioWebId)
    {
        return await _db.FindToListAsync(_db.Pacientes, p => p.UsuarioWebId == usuarioWebId);
    }

    public async Task UpdateBiometriaAsync(string pacienteId, UpdateBiometriaRequest request)
    {
        var edad = DateTime.Today.Year - request.FechaNacimiento.Year;
        if (request.FechaNacimiento.Date > DateTime.Today.AddYears(-edad)) edad--;

        var update = Builders<Paciente>.Update
            .Set(p => p.FechaNacimiento, request.FechaNacimiento)
            .Set(p => p.Biometria.Edad, edad)
            .Set(p => p.Biometria.Sexo, request.Sexo)
            .Set(p => p.Biometria.PesoKg, request.PesoKg)
            .Set(p => p.Biometria.EstaturaCm, request.EstaturaCm)
            .Set(p => p.Biometria.EsDiabetico, request.EsDiabetico)
            .Set(p => p.Biometria.FamiliaresDiabetes, request.FamiliaresDiabetes)
            .Set(p => p.Biometria.ActividadFisica, request.ActividadFisica)
            .Set(p => p.PerfilCompletado, true);

        await _db.Pacientes.UpdateOneAsync(p => p.Id == pacienteId, update);
        _logger.LogInformation("Biometrics updated for patient: {PacienteId}", pacienteId);
    }

    public async Task<(bool success, string? codigo, string? pacienteId, string? error)> CrearPacienteAsync(string usuarioWebId, string nombre)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == usuarioWebId);
        if (user == null) return (false, null, null, "Usuario no encontrado");

        var plan = await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == user.PlanId);
        if (plan == null) return (false, null, null, "Plan no encontrado");

        var count = await _db.CountDocumentsAsync(_db.Pacientes, p => p.UsuarioWebId == usuarioWebId);
        if (count >= plan.LimitePacientes)
            return (false, null, null, $"Límite de pacientes alcanzado ({plan.LimitePacientes})");

        var codigo = GenerarCodigo();
        var paciente = new Paciente
        {
            UsuarioWebId = usuarioWebId,
            CodigoAccesoQr = codigo,
            CodigoExpira = DateTime.UtcNow.AddMinutes(QR_EXPIRY_MINUTES),
            Nombre = nombre,
            FechaRegistro = DateTime.UtcNow
        };
        await _db.Pacientes.InsertOneAsync(paciente);
        _logger.LogInformation("Patient created: {PacienteId} for user: {UsuarioWebId}", paciente.Id, usuarioWebId);
        return (true, codigo, paciente.Id, null);
    }

    public async Task<bool> UpdateNombreAsync(string pacienteId, string nombre)
    {
        var update = Builders<Paciente>.Update.Set(p => p.Nombre, nombre);
        var result = await _db.Pacientes.UpdateOneAsync(p => p.Id == pacienteId, update);
        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning("Patient name update not found or unchanged: {PacienteId}", pacienteId);
        }
        else
        {
            _logger.LogInformation("Patient name updated: {PacienteId}", pacienteId);
        }
        return result.ModifiedCount > 0;
    }

    public async Task<string?> RegenerarCodigoAccesoAsync(string pacienteId)
    {
        var codigo = GenerarCodigo();
        var update = Builders<Paciente>.Update
            .Set(p => p.CodigoAccesoQr, codigo)
            .Set(p => p.CodigoExpira, DateTime.UtcNow.AddMinutes(QR_EXPIRY_MINUTES));
        var result = await _db.Pacientes.UpdateOneAsync(p => p.Id == pacienteId, update);
        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning("Paciente not found for QR regeneration: {PacienteId}", pacienteId);
            return null;
        }
        _logger.LogInformation("QR code regenerated for paciente: {PacienteId}", pacienteId);
        return codigo;
    }

    public async Task<bool> EliminarAsync(string pacienteId)
    {
        await _db.DeleteManyAsync(_db.LecturasSensores, l => l.Meta.PacienteId == pacienteId);
        await _db.DeleteManyAsync(_db.EventosMetabolicos, e => e.PacienteId == pacienteId);
        await _db.DeleteManyAsync(_db.TrackingGps, t => t.Meta.PacienteId == pacienteId);
        await _db.DeleteManyAsync(_db.Notificaciones, n => n.PacienteId == pacienteId);
        await _db.DeleteManyAsync(_db.Dispositivos, d => d.PacienteId == pacienteId);
        await _db.DeleteManyAsync(_db.Medicamentos, m => m.PacienteId == pacienteId);
        await _db.DeleteManyAsync(_db.Alertas, a => a.PacienteId == pacienteId);
        await _db.DeleteManyAsync(_db.Cuidadores, c => c.PacienteId == pacienteId);

        var result = await _db.Pacientes.DeleteOneAsync(p => p.Id == pacienteId);
        if (result.DeletedCount == 0)
        {
            _logger.LogWarning("Patient delete not found: {PacienteId}", pacienteId);
        }
        else
        {
            _logger.LogInformation("Patient deleted: {PacienteId}", pacienteId);
        }
        return result.DeletedCount > 0;
    }

    private static string GenerarCodigo()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[System.Security.Cryptography.RandomNumberGenerator.GetInt32(s.Length)]).ToArray());
    }
}
