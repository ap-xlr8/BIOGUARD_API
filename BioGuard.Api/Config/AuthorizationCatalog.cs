namespace BioGuard.Api.Config;

public static class SystemRoles
{
    public const string Administrador = "administrador";
    public const string Dueno = "dueno";
    public const string Paciente = "paciente";
    public const string Cuidador = "cuidador";

    public static bool IsKnown(string? role) => role is Administrador or Dueno or Paciente or Cuidador;
}

public static class CaregiverAccessLevels
{
    public const string SoloAlertas = "solo_alertas";
    public const string ResumenSemanal = "resumen_semanal";
    public const string HistorialCompleto = "historial_completo";

    public static readonly IReadOnlyDictionary<string, int> Rank = new Dictionary<string, int>
    {
        [SoloAlertas] = 0,
        [ResumenSemanal] = 1,
        [HistorialCompleto] = 2
    };
}

public static class AppPermissions
{
    public const string AdminPanel = "admin.panel";
    public const string AccountProfile = "account.profile";
    public const string AccountSessions = "account.sessions";
    public const string PatientCreate = "patient.create";
    public const string PatientRead = "patient.read";
    public const string PatientManage = "patient.manage";
    public const string AlertRead = "alert.read";
    public const string AlertAcknowledge = "alert.acknowledge";
    public const string HealthSummary = "health.summary";
    public const string HealthHistory = "health.history";
    public const string ReportExport = "report.export";
    public const string MedicationRead = "medication.read";
    public const string MedicationTake = "medication.take";
    public const string MedicationManage = "medication.manage";
    public const string CaregiverManage = "caregiver.manage";
    public const string BillingManage = "billing.manage";
    public const string DeviceRead = "device.read";
    public const string DevicePair = "device.pair";
    public const string GpsContinuous = "gps.continuous";
    public const string NightGuardian = "guardian.night";
    public const string AiConsole = "ai.console";
}
