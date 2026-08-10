using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BioGuard.Api.Models;

public class EventoProcesado
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("fecha")]
    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    [BsonElement("estado")]
    public string Estado { get; set; } = "completed";

    [BsonElement("resultado_id")]
    public string? ResultadoId { get; set; }
}
