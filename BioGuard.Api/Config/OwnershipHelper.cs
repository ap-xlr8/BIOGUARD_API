using BioGuard.Api.Services;

namespace BioGuard.Api.Config;

public class OwnershipHelper
{
    public const string NivelSoloAlertas = CaregiverAccessLevels.SoloAlertas;
    public const string NivelResumenSemanal = CaregiverAccessLevels.ResumenSemanal;
    public const string NivelHistorialCompleto = CaregiverAccessLevels.HistorialCompleto;

    private readonly IMongoDbContext _db;
    private readonly AccessControlService _accessControl;

    public OwnershipHelper(IMongoDbContext db, AccessControlService accessControl)
    {
        _db = db;
        _accessControl = accessControl;
    }

    public async Task<bool> VerifyPacienteOwnershipAsync(string pacienteId, string userId, string role)
    {
        if (role == SystemRoles.Paciente) return pacienteId == userId;
        if (role == SystemRoles.Cuidador)
        {
            var access = await _accessControl.ResolveAsync(userId, role);
            return access is { CaregiverWithinPlan: true } && access.PatientId == pacienteId;
        }

        if (role != SystemRoles.Dueno) return false;
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
        return paciente?.UsuarioWebId == userId;
    }

    public async Task<bool> VerifyPacienteAccessAsync(
        string pacienteId,
        string userId,
        string role,
        string? nivelMinimo = null,
        string? nivelActual = null)
    {
        if (role == SystemRoles.Paciente) return pacienteId == userId;

        if (role == SystemRoles.Cuidador)
        {
            var access = await _accessControl.ResolveAsync(userId, role);
            if (access is not { CaregiverWithinPlan: true } || access.PatientId != pacienteId) return false;
            if (string.IsNullOrEmpty(nivelMinimo)) return true;
            if (!CaregiverAccessLevels.Rank.TryGetValue(nivelMinimo, out var required)) return false;

            // Database state is authoritative. JWT access-level claims are intentionally ignored
            // so caregiver downgrades and plan downgrades take effect on the next request.
            var actual = CaregiverAccessLevels.Rank.GetValueOrDefault(
                access.CaregiverAccessLevel ?? string.Empty, -1);
            return actual >= required;
        }

        if (role != SystemRoles.Dueno) return false;
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == pacienteId);
        return paciente?.UsuarioWebId == userId;
    }
}
