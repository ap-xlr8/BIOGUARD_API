using MongoDB.Driver;
using BioGuard.Api.Models;

namespace BioGuard.Api.Config;

public class OwnershipHelper
{
    public const string NivelSoloAlertas = "solo_alertas";
    public const string NivelResumenSemanal = "resumen_semanal";
    public const string NivelHistorialCompleto = "historial_completo";

    private static readonly Dictionary<string, int> JerarquiaNiveles = new()
    {
        [NivelSoloAlertas] = 0,
        [NivelResumenSemanal] = 1,
        [NivelHistorialCompleto] = 2
    };

    private readonly IMongoDbContext _db;

    public OwnershipHelper(IMongoDbContext db)
    {
        _db = db;
    }

    public async Task<bool> VerifyPacienteOwnershipAsync(string pacienteId, string userId, string role)
    {
        if (role == "paciente") return pacienteId == userId;
        if (role == "cuidador")
        {
            var cuidador = await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.Id == userId && c.PacienteId == pacienteId);
            return cuidador != null;
        }

        // dueno role — check patient is owned by this user
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
        if (paciente == null) return false;
        return paciente.UsuarioWebId == userId;
    }

    /// <summary>
    /// Verifica la propiedad del paciente y, para cuidadores, que su nivel de acceso
    /// sea suficiente para el recurso solicitado (nivelMinimo). El nivel del cuidador
    /// se toma del claim del token cuando está disponible; si no, se consulta a la BD.
    /// </summary>
    public async Task<bool> VerifyPacienteAccessAsync(string pacienteId, string userId, string role,
        string? nivelMinimo = null, string? nivelActual = null)
    {
        if (role == "paciente") return pacienteId == userId;

        if (role == "cuidador")
        {
            var cuidador = await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.Id == userId && c.PacienteId == pacienteId);
            if (cuidador == null) return false;
            if (string.IsNullOrEmpty(nivelMinimo)) return true;

            var nivel = nivelActual ?? cuidador.NivelAcceso;
            return JerarquiaNiveles.GetValueOrDefault(nivel) >= JerarquiaNiveles.GetValueOrDefault(nivelMinimo);
        }

        // dueno role — check patient is owned by this user
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
        if (paciente == null) return false;
        return paciente.UsuarioWebId == userId;
    }
}
