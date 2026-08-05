using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using Microsoft.Extensions.Logging;

namespace BioGuard.Api.Services;

public class MedicamentoService
{
    private readonly IMongoDbContext _db;
    private readonly CriptoService _cripto;
    private readonly ILogger<MedicamentoService> _logger;

    public MedicamentoService(IMongoDbContext db, CriptoService cripto, ILogger<MedicamentoService> logger)
    {
        _db = db;
        _cripto = cripto;
        _logger = logger;
    }

    public async Task<List<Medicamento>> ObtenerPorPacienteAsync(string pacienteId)
    {
        var filter = Builders<Medicamento>.Filter.Eq(m => m.PacienteId, pacienteId);
        var sort = Builders<Medicamento>.Sort.Descending(m => m.FechaCreacion);
        var list = await _db.FindToListAsync(_db.Medicamentos, filter, sort);
        foreach (var m in list)
        {
            m.Nombre = _cripto.Decrypt(m.Nombre);
            m.Dosis = _cripto.Decrypt(m.Dosis);
            if (!string.IsNullOrEmpty(m.Notas)) m.Notas = _cripto.Decrypt(m.Notas);
        }
        return list;
    }

    public async Task<Medicamento?> ObtenerPorIdAsync(string id)
    {
        var m = await _db.FindFirstOrDefaultAsync(_db.Medicamentos, m => m.Id == id);
        if (m != null)
        {
            m.Nombre = _cripto.Decrypt(m.Nombre);
            m.Dosis = _cripto.Decrypt(m.Dosis);
            if (!string.IsNullOrEmpty(m.Notas)) m.Notas = _cripto.Decrypt(m.Notas);
        }
        return m;
    }

    public async Task<Medicamento> CrearAsync(string pacienteId, string nombre,
        string dosis, string horario, string? notas = null)
    {
        var medicamento = new Medicamento
        {
            PacienteId = pacienteId,
            Nombre = _cripto.Encrypt(nombre),
            Dosis = _cripto.Encrypt(dosis),
            Horario = horario,
            Notas = string.IsNullOrEmpty(notas) ? null : _cripto.Encrypt(notas),
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };

        await _db.Medicamentos.InsertOneAsync(medicamento);
        _logger.LogInformation("Medication created for patient: {PacienteId}", pacienteId);
        
        medicamento.Nombre = nombre;
        medicamento.Dosis = dosis;
        medicamento.Notas = notas;
        return medicamento;
    }

    public async Task<bool> ActualizarAsync(string id, string nombre,
        string dosis, string horario, string? notas)
    {
        var update = Builders<Medicamento>.Update
            .Set(m => m.Nombre, _cripto.Encrypt(nombre))
            .Set(m => m.Dosis, _cripto.Encrypt(dosis))
            .Set(m => m.Horario, horario)
            .Set(m => m.Notas, string.IsNullOrEmpty(notas) ? null : _cripto.Encrypt(notas));

        var result = await _db.Medicamentos.UpdateOneAsync(m => m.Id == id, update);
        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning("Medication update not found or unchanged: {MedicamentoId}", id);
        }
        else
        {
            _logger.LogInformation("Medication updated: {MedicamentoId}", id);
        }
        return result.ModifiedCount > 0;
    }

    public async Task<bool> RegistrarTomaAsync(string medicamentoId)
    {
        var update = Builders<Medicamento>.Update.Set(m => m.UltimaToma, DateTime.UtcNow);
        var result = await _db.Medicamentos.UpdateOneAsync(m => m.Id == medicamentoId, update);
        _logger.LogInformation("Medication dose recorded: {MedicamentoId}", medicamentoId);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> ActivarAsync(string id, bool activo)
    {
        var update = Builders<Medicamento>.Update.Set(m => m.Activo, activo);
        var result = await _db.Medicamentos.UpdateOneAsync(m => m.Id == id, update);
        _logger.LogInformation("Medication {MedicamentoId} activated: {Activo}", id, activo);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> EliminarAsync(string id)
    {
        var result = await _db.Medicamentos.DeleteOneAsync(m => m.Id == id);
        if (result.DeletedCount == 0)
        {
            _logger.LogWarning("Medication delete not found: {MedicamentoId}", id);
        }
        else
        {
            _logger.LogInformation("Medication deleted: {MedicamentoId}", id);
        }
        return result.DeletedCount > 0;
    }

    public async Task<bool> EliminarPorPacienteAsync(string pacienteId)
    {
        var result = await _db.DeleteManyAsync(_db.Medicamentos, m => m.PacienteId == pacienteId);
        _logger.LogInformation("Medications deleted for patient: {PacienteId}, count: {Count}", pacienteId, result.DeletedCount);
        return result.DeletedCount > 0;
    }

    private static int ObtenerFrecuenciaDiaria(string horario)
    {
        if (string.IsNullOrWhiteSpace(horario)) return 1;
        var partes = horario.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Math.Max(1, partes.Length);
    }

    public async Task<object> CalcularAdherenciaAsync(string pacienteId, DateTime? desde, DateTime? hasta)
    {
        var medicamentos = await ObtenerPorPacienteAsync(pacienteId);
        var activos = medicamentos.Where(m => m.Activo).ToList();
        if (activos.Count == 0)
            return new { porcentajeAdherencia = 0.0, tomasEsperadas = 0, tomasConfirmadas = 0 };

        var hastaDt = hasta ?? DateTime.UtcNow;
        var desdeDt = desde ?? hastaDt.AddDays(-30);
        var diasEnRango = Math.Max(1, (int)(hastaDt - desdeDt).TotalDays);

        var tomasEsperadas = activos.Sum(m => ObtenerFrecuenciaDiaria(m.Horario) * diasEnRango);
        var tomasConfirmadas = activos.Count(m => m.UltimaToma.HasValue && m.UltimaToma >= desdeDt && m.UltimaToma <= hastaDt);

        var porcentaje = tomasEsperadas > 0
            ? Math.Round((double)tomasConfirmadas / tomasEsperadas * 100, 1)
            : 0.0;

        _logger.LogInformation("Adherence calculated for paciente {PacienteId}: {Porcentaje}%", pacienteId, porcentaje);
        return new { porcentajeAdherencia = porcentaje, tomasEsperadas, tomasConfirmadas };
    }
}
