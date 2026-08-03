using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BioGuard.Api.Models;

public class Paciente
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("usuario_web_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UsuarioWebId { get; set; } = string.Empty;

    [BsonElement("codigo_acceso_qr")]
    public string CodigoAccesoQr { get; set; } = string.Empty;

    [BsonElement("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [BsonElement("foto")]
    public string? Foto { get; set; }

    [BsonElement("fecha_nacimiento")]
    public DateTime? FechaNacimiento { get; set; }

    [BsonElement("biometria")]
    public Biometria Biometria { get; set; } = new();

    [BsonElement("dispositivo")]
    public DispositivoInfo Dispositivo { get; set; } = new();

    [BsonElement("perfil_completado")]
    public bool PerfilCompletado { get; set; } = false;

    [BsonElement("fecha_registro")]
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    [BsonElement("codigo_expira")]
    public DateTime? CodigoExpira { get; set; }

    [BsonElement("intentos_fallidos")]
    public int IntentosFallidos { get; set; } = 0;

    [BsonElement("bloqueado_hasta")]
    public DateTime? BloqueadoHasta { get; set; }

    [BsonElement("contacto_emergencia_nombre")]
    public string? ContactoEmergenciaNombre { get; set; }

    [BsonElement("contacto_emergencia_telefono")]
    public string? ContactoEmergenciaTelefono { get; set; }

    [BsonElement("contacto_emergencia_parentesco")]
    public string? ContactoEmergenciaParentesco { get; set; }

    [BsonElement("zona_horaria")]
    public string ZonaHoraria { get; set; } = "America/Mexico_City";

    [BsonElement("guardian_nocturno_activo")]
    public bool GuardianNocturnoActivo { get; set; } = false;

    [BsonElement("ventana_respuesta_segundos")]
    public int VentanaRespuestaSegundos { get; set; } = 30;

    [BsonElement("ubicacion_emergencia_autorizada")]
    public bool UbicacionEmergenciaAutorizada { get; set; } = false;
}

public class Biometria
{
    [BsonElement("edad")]
    public int Edad { get; set; }

    [BsonElement("peso_kg")]
    public double PesoKg { get; set; }

    [BsonElement("estatura_cm")]
    public double EstaturaCm { get; set; }

    [BsonElement("es_diabetico")]
    public bool EsDiabetico { get; set; }

    [BsonElement("familiares_diabetes")]
    public bool FamiliaresDiabetes { get; set; }

    [BsonElement("actividad_fisica")]
    public string ActividadFisica { get; set; } = string.Empty;

    [BsonElement("sexo")]
    public string Sexo { get; set; } = string.Empty;
}

public class DispositivoInfo
{
    [BsonElement("mac_address")]
    public string MacAddress { get; set; } = string.Empty;

    [BsonElement("conectado")]
    public bool Conectado { get; set; }
}
