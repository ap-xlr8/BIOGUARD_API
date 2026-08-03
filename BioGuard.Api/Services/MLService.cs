using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;

namespace BioGuard.Api.Services;

public class MLService
{
    private readonly IMongoDbContext _db;
    private readonly ILogger<MLService> _logger;

    public MLService(IMongoDbContext db, ILogger<MLService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<PrediccionMl>> ObtenerPrediccionesAsync(string pacienteId)
    {
        _logger.LogInformation("Obteniendo predicciones ML para paciente {PacienteId}", pacienteId);
        var filter = Builders<PrediccionMl>.Filter.Eq(p => p.PacienteId, pacienteId);
        var sort = Builders<PrediccionMl>.Sort.Descending(p => p.FechaPrediccion);
        return await _db.FindToListAsync(_db.PrediccionesMl, filter, sort, 20);
    }

    public async Task<PrediccionMl?> ObtenerPrediccionActualAsync(string pacienteId)
    {
        _logger.LogInformation("Obteniendo predicción actual para paciente {PacienteId}", pacienteId);
        var filter = Builders<PrediccionMl>.Filter.And(
            Builders<PrediccionMl>.Filter.Eq(p => p.PacienteId, pacienteId),
            Builders<PrediccionMl>.Filter.Gt(p => p.FechaExpiracion, DateTime.UtcNow));
        var sort = Builders<PrediccionMl>.Sort.Descending(p => p.FechaPrediccion);
        return await _db.FindFirstOrDefaultAsync(_db.PrediccionesMl, filter, sort);
    }

    public async Task<List<string>> ObtenerRecomendacionesAsync(string pacienteId)
    {
        _logger.LogInformation("Obteniendo recomendaciones ML para paciente {PacienteId}", pacienteId);
        var prediccion = await ObtenerPrediccionActualAsync(pacienteId);
        if (prediccion == null)
        {
            _logger.LogWarning("No hay predicción activa para paciente {PacienteId}", pacienteId);
            return new List<string>();
        }

        var recomendaciones = new List<string> { prediccion.Recomendacion };

        if (prediccion.NivelRiesgo == "Crítico" || prediccion.NivelRiesgo == "Critico")
        {
            recomendaciones.Add("Contactar al cuidador de inmediato.");
            recomendaciones.Add("Verificar niveles de glucosa si es posible.");
        }
        else if (prediccion.NivelRiesgo == "Pre-Pico")
        {
            recomendaciones.Add("Mantener hidratación constante.");
            recomendaciones.Add("Evitar actividad física intensa.");
        }

        return recomendaciones;
    }

    public async Task<List<ModeloMl>> ObtenerModelosAsync()
    {
        _logger.LogInformation("Obteniendo modelos ML");
        var filter = Builders<ModeloMl>.Filter.Empty;
        var sort = Builders<ModeloMl>.Sort.Descending(m => m.FechaEntrenamiento);
        return await _db.FindToListAsync(_db.ModelosMl, filter, sort);
    }

    public async Task<ModeloMl?> ObtenerModeloActivoAsync()
    {
        _logger.LogInformation("Buscando modelo ML activo");
        return await _db.FindFirstOrDefaultAsync(_db.ModelosMl, m => m.Activo);
    }

    public async Task<ModeloMl> CrearModeloAsync(ModeloMl modelo)
    {
        _logger.LogInformation("Creando modelo ML v{Version}", modelo.Version);
        await _db.ModelosMl.InsertOneAsync(modelo);
        _logger.LogInformation("Modelo ML creado con ID {ModeloId}", modelo.Id);
        return modelo;
    }

    public async Task<ModeloMl?> ObtenerMetricasAsync(string modeloId)
    {
        _logger.LogInformation("Obteniendo métricas del modelo {ModeloId}", modeloId);
        return await _db.FindFirstOrDefaultAsync(_db.ModelosMl, m => m.Id == modeloId);
    }

    public async Task DesactivarModeloAsync(string modeloId)
    {
        _logger.LogInformation("Desactivando modelo {ModeloId}", modeloId);
        var update = Builders<ModeloMl>.Update.Set(m => m.Activo, false);
        await _db.ModelosMl.UpdateOneAsync(m => m.Id == modeloId, update);
    }
}
