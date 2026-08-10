using BioGuard.Api.Models;

namespace BioGuard.Api.Config;

public static class PlanCatalog
{
    public const string Free = "BioGuard Free";
    public const string Plus = "BioGuard Plus";
    public const string Care = "BioGuard Care";
    public const string Family = "BioGuard Family";
    public const string ProHealth = "BioGuard Pro Salud";

    public static IReadOnlyList<string> Aliases(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "free" or "gratis" or "bioguard free" or "bio guard free" =>
                new[] { Free, "Free", "Gratis" },
            "plus" or "bioguard plus" or "bio guard plus" =>
                new[] { Plus, "Plus" },
            "care" or "bioguard care" or "bio guard care" =>
                new[] { Care, "Care" },
            "family" or "familia" or "familiar" or "bioguard family" or "bio guard family" =>
                new[] { Family, "Family", "Familiar" },
            "pro" or "pro salud" or "premium" or "bioguard pro salud" or "bio guard pro salud" =>
                new[] { ProHealth, "Pro Salud", "Pro", "Premium" },
            _ when !string.IsNullOrWhiteSpace(value) => new[] { value.Trim() },
            _ => Array.Empty<string>()
        };
    }

    public static List<Plan> CreateDefaultPlans() => new()
    {
        new()
        {
            Nombre = Free, Precio = 0m, PrecioMoneda = "MXN", LimitePacientes = 1,
            LimiteCuidadores = 0, DiasHistorial = 7, GpsContinuo = false, AiConsole = false,
            GuardianNocturnoDisponible = false, ExportacionReportesDisponible = false,
            EsSuscripcion = false, Activo = true, Orden = 1,
            Descripcion = "Monitoreo basico, semaforo metabolico y alertas preventivas limitadas."
        },
        new()
        {
            Nombre = Plus, Precio = 69m, PrecioMoneda = "MXN", LimitePacientes = 1,
            LimiteCuidadores = 1, DiasHistorial = 90, GpsContinuo = false, AiConsole = false,
            GuardianNocturnoDisponible = false, ExportacionReportesDisponible = false,
            Activo = true, Orden = 2,
            Descripcion = "Analisis semanal y recordatorios inteligentes de medicamentos."
        },
        new()
        {
            Nombre = Care, Precio = 129m, PrecioMoneda = "MXN", LimitePacientes = 1,
            LimiteCuidadores = 2, DiasHistorial = 180, GpsContinuo = false, AiConsole = false,
            GuardianNocturnoDisponible = true, ExportacionReportesDisponible = true,
            Activo = true, Orden = 3,
            Descripcion = "Guardian Nocturno, contacto de emergencia y reportes descargables."
        },
        new()
        {
            Nombre = Family, Precio = 189m, PrecioMoneda = "MXN", LimitePacientes = 4,
            LimiteCuidadores = 4, DiasHistorial = 365, GpsContinuo = true, AiConsole = false,
            GuardianNocturnoDisponible = true, ExportacionReportesDisponible = true,
            Activo = true, Orden = 4,
            Descripcion = "Panel familiar, GPS continuo y alertas compartidas."
        },
        new()
        {
            Nombre = ProHealth, Precio = 399m, PrecioMoneda = "MXN", LimitePacientes = 100,
            LimiteCuidadores = 100, DiasHistorial = 3650, GpsContinuo = true, AiConsole = true,
            GuardianNocturnoDisponible = true, ExportacionReportesDisponible = true,
            Activo = true, Orden = 5,
            Descripcion = "Panel institucional multiusuario, exportacion e indicadores agregados."
        }
    };
}
