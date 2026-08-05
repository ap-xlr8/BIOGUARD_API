using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BioGuard.Api.Models;

public class LecturaSensor
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("meta")]
    public MetaData Meta { get; set; } = new();

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("pulso_bpm")]
    public int PulsoBpm { get; set; }

    [BsonElement("temperatura_c")]
    public double TemperaturaC { get; set; }

    [BsonElement("sudoracion_gsr")]
    public double SudoracionGsr { get; set; }

    [BsonElement("probabilidad_pico")]
    public double ProbabilidadPico { get; set; }

    [BsonElement("nivel_riesgo")]
    public string? NivelRiesgo { get; set; }

    [BsonElement("hrv")]
    public double? Hrv { get; set; }

    [BsonElement("spo2")]
    public int? Spo2 { get; set; }

    [BsonElement("es_simulado")]
    public bool? EsSimulado { get; set; }

    [BsonElement("expireAt")]
    public DateTime ExpireAt { get; set; }

    [BsonElement("oculto")]
    public bool Oculto { get; set; } = false;
}

public class MetaData
{
    [BsonElement("paciente_id")]
    public string PacienteId { get; set; } = string.Empty;

    [BsonElement("dispositivo_mac")]
    public string DispositivoMac { get; set; } = string.Empty;
}
