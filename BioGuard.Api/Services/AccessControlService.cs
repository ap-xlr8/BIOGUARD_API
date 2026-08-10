using BioGuard.Api.Config;
using BioGuard.Api.Models;
using MongoDB.Driver;

namespace BioGuard.Api.Services;

public sealed record EffectiveAccessContext(
    string UserId,
    string Role,
    string? OwnerId,
    string? PatientId,
    string? CaregiverAccessLevel,
    bool CaregiverWithinPlan,
    Plan? Plan,
    IReadOnlySet<string> Permissions);

public sealed class AccessControlService
{
    private readonly IMongoDbContext _db;

    public AccessControlService(IMongoDbContext db)
    {
        _db = db;
    }

    public async Task<EffectiveAccessContext?> ResolveAsync(string userId, string? role)
    {
        if (string.IsNullOrWhiteSpace(userId) || !SystemRoles.IsKnown(role)) return null;

        if (role == SystemRoles.Administrador)
        {
            return new EffectiveAccessContext(
                userId, role, null, null, null, true, null,
                new HashSet<string>
                {
                    AppPermissions.AdminPanel,
                    AppPermissions.AccountProfile,
                    AppPermissions.AccountSessions
                });
        }

        string ownerId;
        string? patientId;
        string? caregiverLevel = null;
        var caregiverWithinPlan = true;

        if (role == SystemRoles.Dueno)
        {
            ownerId = userId;
            patientId = null;
        }
        else if (role == SystemRoles.Paciente)
        {
            var patient = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == userId);
            if (patient == null) return null;
            ownerId = patient.UsuarioWebId;
            patientId = patient.Id;
        }
        else
        {
            var caregiver = await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.Id == userId);
            if (caregiver == null) return null;
            var patient = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == caregiver.PacienteId);
            if (patient == null) return null;
            ownerId = patient.UsuarioWebId;
            patientId = patient.Id;
            caregiverLevel = CaregiverAccessLevels.Rank.ContainsKey(caregiver.NivelAcceso)
                ? caregiver.NivelAcceso
                : CaregiverAccessLevels.SoloAlertas;

            var owner = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == ownerId);
            var ownerPlan = owner == null
                ? null
                : await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == owner.PlanId && p.Activo);
            if (ownerPlan == null) return null;

            var allowedCount = Math.Max(0, ownerPlan.LimiteCuidadores);
            var caregiverCount = await _db.CountDocumentsAsync(
                _db.Cuidadores, c => c.PacienteId == patientId);
            if (allowedCount == 0)
            {
                caregiverWithinPlan = false;
            }
            else if (caregiverCount <= allowedCount)
            {
                caregiverWithinPlan = true;
            }
            else
            {
                var caregivers = await _db.FindToListAsync(
                    _db.Cuidadores,
                    Builders<Cuidador>.Filter.Eq(c => c.PacienteId, patientId),
                    Builders<Cuidador>.Sort.Ascending(c => c.FechaAutorizacion).Ascending(c => c.Id));
                caregiverWithinPlan = caregivers != null && caregivers
                    .Take(allowedCount)
                    .Any(c => string.Equals(c.Id, caregiver.Id, StringComparison.Ordinal));
            }
        }

        var ownerUser = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == ownerId);
        var plan = ownerUser == null
            ? null
            : await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == ownerUser.PlanId && p.Activo);
        if (plan == null) return null;

        var permissions = BuildPermissions(role!, caregiverLevel, caregiverWithinPlan, plan);
        return new EffectiveAccessContext(
            userId, role!, ownerId, patientId, caregiverLevel, caregiverWithinPlan, plan, permissions);
    }

    private static IReadOnlySet<string> BuildPermissions(
        string role,
        string? caregiverLevel,
        bool caregiverWithinPlan,
        Plan plan)
    {
        var permissions = new HashSet<string>
        {
            AppPermissions.AccountProfile,
            AppPermissions.AccountSessions
        };

        if (role == SystemRoles.Dueno)
        {
            permissions.UnionWith(new[]
            {
                AppPermissions.PatientCreate, AppPermissions.PatientRead, AppPermissions.PatientManage,
                AppPermissions.AlertRead, AppPermissions.AlertAcknowledge,
                AppPermissions.HealthSummary, AppPermissions.HealthHistory,
                AppPermissions.MedicationRead, AppPermissions.MedicationTake, AppPermissions.MedicationManage,
                AppPermissions.CaregiverManage, AppPermissions.BillingManage, AppPermissions.DeviceRead
            });
        }
        else if (role == SystemRoles.Paciente)
        {
            permissions.UnionWith(new[]
            {
                AppPermissions.PatientRead, AppPermissions.PatientManage,
                AppPermissions.AlertRead, AppPermissions.AlertAcknowledge,
                AppPermissions.HealthSummary, AppPermissions.HealthHistory,
                AppPermissions.MedicationRead, AppPermissions.MedicationTake,
                AppPermissions.DeviceRead, AppPermissions.DevicePair
            });
        }
        else if (role == SystemRoles.Cuidador && caregiverWithinPlan)
        {
            permissions.UnionWith(new[] { AppPermissions.AlertRead, AppPermissions.AlertAcknowledge });
            var rank = CaregiverAccessLevels.Rank.GetValueOrDefault(caregiverLevel ?? string.Empty, -1);
            if (rank >= CaregiverAccessLevels.Rank[CaregiverAccessLevels.ResumenSemanal])
            {
                permissions.UnionWith(new[]
                {
                    AppPermissions.PatientRead, AppPermissions.HealthSummary, AppPermissions.DeviceRead
                });
            }
            if (rank >= CaregiverAccessLevels.Rank[CaregiverAccessLevels.HistorialCompleto])
            {
                permissions.UnionWith(new[]
                {
                    AppPermissions.HealthHistory, AppPermissions.MedicationRead, AppPermissions.MedicationTake
                });
            }
        }

        if (plan.ExportacionReportesDisponible && permissions.Contains(AppPermissions.HealthHistory))
            permissions.Add(AppPermissions.ReportExport);
        if (plan.GpsContinuo && role is SystemRoles.Dueno or SystemRoles.Paciente)
            permissions.Add(AppPermissions.GpsContinuous);
        if (plan.GuardianNocturnoDisponible && role is SystemRoles.Dueno or SystemRoles.Paciente)
            permissions.Add(AppPermissions.NightGuardian);
        if (plan.AiConsole && role == SystemRoles.Dueno)
            permissions.Add(AppPermissions.AiConsole);

        return permissions;
    }
}
