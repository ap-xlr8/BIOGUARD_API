using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using AspNetCoreRateLimit;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

// =============================================
// CONFIGURATION AND DATABASE (MongoDB)
// =============================================
static string? FallbackIfEmpty(string? value, string? fallback)
    => string.IsNullOrWhiteSpace(value) ? fallback : value;

var mongoConnectionString = FallbackIfEmpty(builder.Configuration["ConnectionStrings:MongoDB"],
        Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING"))
    ?? throw new InvalidOperationException("MongoDB connection string not configured.");
var jwtKey = FallbackIfEmpty(builder.Configuration["Jwt:Key"],
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using AspNetCoreRateLimit;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

// =============================================
// CONFIGURATION AND DATABASE (MongoDB)
// =============================================
static string? FallbackIfEmpty(string? value, string? fallback)
    => string.IsNullOrWhiteSpace(value) ? fallback : value;

var mongoConnectionString = FallbackIfEmpty(builder.Configuration["ConnectionStrings:MongoDB"],
        Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING"))
    ?? throw new InvalidOperationException("MongoDB connection string not configured.");
var jwtKey = FallbackIfEmpty(builder.Configuration["Jwt:Key"],
        Environment.GetEnvironmentVariable("JWT_SECRET_KEY"))
    ?? throw new InvalidOperationException("JWT secret key not configured.");

// Aislar la clave de cifrado: centralizar aquí el mapeo de CRIPTO_KEY bajo Cripto:Key
// para que CriptoService dependa solo de la configuración y no lea variables de entorno
// directamente (la clave de cifrado queda separada de la firma JWT).
var criptoKey = FallbackIfEmpty(builder.Configuration["Cripto:Key"],
    Environment.GetEnvironmentVariable("CRIPTO_KEY"));
if (!string.IsNullOrWhiteSpace(criptoKey))
{
    builder.Configuration["Cripto:Key"] = criptoKey;
}
else if (builder.Environment.IsProduction())
{
    // En producción, CRIPTO_KEY es obligatorio para garantizar cifrado
    // independiente de la clave JWT. Sin esta clave, los datos GPS
    // y datos sensibles no tendrán cifrado dedicado.
    throw new InvalidOperationException(
        "CRIPTO_KEY es obligatorio en producción. " +
        "Configura esta variable de entorno en DigitalOcean App Platform / GitHub Secrets.");
}

var mongoConfig = new MongoDbConfig
{
    ConnectionString = mongoConnectionString,
    DatabaseName = "bioguard"
};
builder.Services.AddSingleton(mongoConfig);
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<IMongoDbContext>(sp => sp.GetRequiredService<MongoDbContext>());

// =============================================
// QUESTPDF LICENSE (generación de PDFs)
// =============================================
// Community: gratuita para empresas con < $1M de ingresos anuales.
// Para una licencia de producción comercial, sustituir por LicenseType.Production
// con la key emitida por QuestPDF y centralizarla en variables de entorno.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// =============================================
// SERVICE CONFIGURATIONS (via Extension Methods)
// =============================================
builder.Services.ConfigureJwtAuthentication(builder.Configuration, jwtKey);
builder.Services.ConfigureRateLimiting(builder.Configuration, builder.Environment.IsProduction());
builder.Services.ConfigureCors(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddSignalR();

// Option Bindings
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<PayPalOptions>(builder.Configuration.GetSection("PayPal"));
builder.Services.Configure<FirebaseOptions>(builder.Configuration.GetSection("Firebase"));
builder.Services.Configure<ImgBbOptions>(builder.Configuration.GetSection("ImgBB"));

builder.Services.AddSingleton<CriptoService>();

// =============================================
// DEPENDENCY INJECTION (Services)
// =============================================
builder.Services.AddHttpClient<AuthService>();
builder.Services.AddScoped<PacienteService>();
builder.Services.AddScoped<SensorService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<UsuariosWebService>();
builder.Services.AddScoped<PagosService>();
builder.Services.AddScoped<CuidadorService>();
builder.Services.AddScoped<DispositivoService>();
builder.Services.AddScoped<NotificacionService>();
builder.Services.AddScoped<MLService>();
builder.Services.AddScoped<AuditoriaService>();
builder.Services.AddScoped<MedicamentoService>();
builder.Services.AddScoped<AlertaService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<OwnershipHelper>();
builder.Services.AddScoped<AccessControlService>();
builder.Services.AddScoped<IRiesgoMetabolicoService, RiesgoMetabolicoService>();
builder.Services.AddScoped<IRiesgoService, RiesgoService>();
builder.Services.AddScoped<IPacienteAccessService, PacienteAccessService>();
builder.Services.AddScoped<IPlanLimiteService, PlanLimiteService>();
builder.Services.AddScoped<IFCMService, FCMService>();
builder.Services.AddScoped<IImageStorageService, ImgBbImageStorageService>();
builder.Services.AddHttpClient<IImageStorageService, ImgBbImageStorageService>();
builder.Services.AddScoped<IPaymentGateway, StripePaymentGateway>();
builder.Services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

// Durable Background Task Runner for critical alarms
builder.Services.AddHostedService<EscalamientoBackgroundService>();

// =============================================
// API VERSIONING
// =============================================
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = Asp.Versioning.ApiVersionReader.Combine(
        new Asp.Versioning.UrlSegmentApiVersionReader(),
        new Asp.Versioning.HeaderApiVersionReader("X-Api-Version")
    );
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Controllers + Swagger
// Alineado con producción: JSON camelCase (los clientes móvil/web y el API desplegado usan camelCase)
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BioGuard API",
        Version = "v1",
        Description = "API REST para el ecosistema médico IoT BioGuard"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingresa tu token JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// =============================================
// MONGODB TTL INDEXES & TIME SERIES
// =============================================
try
{
    using var scope = app.Services.CreateScope();
    var mongoDbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

    // Time Series Collections
    await CreateTimeSeriesCollectionIfNotExistsAsync(mongoDbContext.Database, "lecturas_sensores", "timestamp", "meta");
    await CreateTimeSeriesCollectionIfNotExistsAsync(mongoDbContext.Database, "tracking_gps", "timestamp", "meta");

    await CreateTtlIndex(mongoDbContext.LecturasSensores, "timestamp", 2592000);
    await CreateTtlIndex(mongoDbContext.RefreshTokens, "expires_at", 0);
    await CreateTtlIndex(mongoDbContext.TokenBlacklist, "expires_at", 0);
    await CreateTtlIndex(mongoDbContext.FcmTokens, "fecha_registro", 7776000); // 90 days TTL
    await CreateTtlIndex(mongoDbContext.ReportesCompartidos, "fecha_expiracion", 0);
    await CreateTtlIndex(mongoDbContext.EventosProcesados, "fecha", 2592000); // 30 days TTL for processed events
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Failed to create TTL indexes at startup");
}

// =============================================
// MIDDLEWARE PIPELINE
// =============================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
    RequireHeaderSymmetry = true
};
var trustedProxyValues = (builder.Configuration["TRUSTED_PROXY_IPS"] ?? string.Empty)
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
foreach (var value in trustedProxyValues)
{
    if (!System.Net.IPAddress.TryParse(value, out var address))
        throw new InvalidOperationException($"TRUSTED_PROXY_IPS contains an invalid address: {value}");
    forwardedHeadersOptions.KnownProxies.Add(address);
}
if (app.Environment.IsProduction() && trustedProxyValues.Length == 0)
{
    throw new InvalidOperationException(
        "TRUSTED_PROXY_IPS is required in production so forwarded headers cannot be spoofed");
}
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseHsts();
app.UseHttpsRedirection();
app.UseCors("BioGuardPolicy");

// Respetar X-Forwarded-For/Proto detrás de proxy para IP real (rate limiting, auditoría)
// Rate limiting middleware
app.UseIpRateLimiting();

// Security headers + global exception handling (via extensions)
app.UseSecurityHeaders();
app.UseGlobalExceptionHandler(app.Environment);

app.UseAuthentication();
app.UseAuthorization();

// =============================================
// MAP ENDPOINTS
// =============================================
app.MapControllers();
app.MapHub<BioGuardHub>("/hubs/bioguard");

app.MapGet("/health", async (IMongoDbContext db, IWebHostEnvironment env) =>
{
    try
    {
        await db.FindFirstOrDefaultAsync(db.Pacientes, Builders<Paciente>.Filter.Empty, null);
        if (env.IsDevelopment())
            return Results.Ok(new { status = "healthy", database = "connected", timestamp = DateTime.UtcNow });
        return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        if (env.IsDevelopment())
            return Results.Json(new { status = "unhealthy", database = "disconnected", error = ex.Message, timestamp = DateTime.UtcNow }, statusCode: 503);
        return Results.Json(new { status = "unhealthy", timestamp = DateTime.UtcNow }, statusCode: 503);
    }
});

// =============================================
// SEED ENDPOINT (Development only, explicitly enabled)
// =============================================
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Seed:Enabled"))
{
var seedEndpoint = app.MapPost("/api/Seed/seed-all", async (IMongoDbContext db, ILogger<Program> logger, HttpContext httpContext, CriptoService cripto) =>
{
    if (!app.Environment.IsDevelopment())
        return Results.Forbid();
    var skipped = new List<string>();

    async Task SafeInsertOne<T>(IMongoCollection<T> col, T doc, string name)
    {
        try { await col.InsertOneAsync(doc); }
        catch (Exception ex) when (ex.Message.Contains("E11000"))
        { skipped.Add(name); }
    }

    async Task SafeInsertMany<T>(IMongoCollection<T> col, List<T> docs, string name)
    {
        try { await col.InsertManyAsync(docs); }
        catch (Exception ex) when (ex.Message.Contains("E11000"))
        { skipped.Add(name); }
    }

    try
    {
        var now = DateTime.UtcNow;

        var freeAliases = PlanCatalog.Aliases(PlanCatalog.Free);
        var existingPlanGratis = await db.FindFirstOrDefaultAsync(db.Planes, p => freeAliases.Contains(p.Nombre));
        if (existingPlanGratis == null)
        {
            // Alineado con producción: planes Gratis/Familiar/Pro (0/10/20 MXN)
            var planes = PlanCatalog.CreateDefaultPlans();
            await SafeInsertMany(db.Planes, planes, "planes");
        }
        var existingPlan = existingPlanGratis
            ?? await db.FindFirstOrDefaultAsync(db.Planes, p => freeAliases.Contains(p.Nombre));
        if (existingPlan == null)
            return Results.Problem("No se pudo resolver el plan 'Gratis' para el seed.");

        var rnd = new Random(Guid.NewGuid().GetHashCode());
        var macAddr = $"AA:BB:CC:{rnd.Next(0x10,0xFF):X2}:{rnd.Next(0x10,0xFF):X2}:{rnd.Next(0x10,0xFF):X2}";
        var testEmail = $"seed_{DateTime.UtcNow.Ticks}@bioguard.test";

        var user = new UsuarioWeb
        {
            Nombre = "Carlos", ApellidoPaterno = "Martinez", ApellidoMaterno = "Lopez",
            Correo = testEmail, PasswordHash = PasswordHasher.Hash("SeedTest@123!"),
            ProveedorAuth = "local", PlanId = existingPlan!.Id, Activo = true, FechaRegistro = now
        };
        await SafeInsertOne(db.UsuariosWeb, user, "user");

        var paciente = new Paciente
        {
            UsuarioWebId = user.Id, CodigoAccesoQr = "SEED-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
            Nombre = "Carlos Martinez Lopez",
            FechaNacimiento = new DateTime(1955, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            Biometria = new Biometria { Edad = 71, PesoKg = 78.5, EstaturaCm = 170.0, EsDiabetico = true, FamiliaresDiabetes = true, ActividadFisica = "sedentario" },
            PerfilCompletado = true, FechaRegistro = now
        };
        await SafeInsertOne(db.Pacientes, paciente, "paciente");

        var lecturas = new List<LecturaSensor>();
        for (int i = 0; i < 50; i++)
        {
            var ts = now.AddMinutes(-i * 10);
            var isPrePico = i % 10 == 0;
            lecturas.Add(new LecturaSensor
            {
                Meta = new MetaData { PacienteId = paciente.Id, DispositivoMac = macAddr },
                Timestamp = ts,
                PulsoBpm = isPrePico ? rnd.Next(100, 120) : rnd.Next(65, 95),
                TemperaturaC = isPrePico ? 37.5 + rnd.NextDouble() * 0.8 : 36.2 + rnd.NextDouble() * 0.6,
                SudoracionGsr = isPrePico ? 8.0 + rnd.NextDouble() * 4.0 : 2.0 + rnd.NextDouble() * 3.0,
                ProbabilidadPico = isPrePico ? 0.75 + rnd.NextDouble() * 0.2 : 0.1 + rnd.NextDouble() * 0.3,
                ExpireAt = ts.AddDays(30)
            });
        }
        await SafeInsertMany(db.LecturasSensores, lecturas, "lecturas");

        var eventos = new List<EventoMetabolico>();
        for (int i = 0; i < 8; i++)
        {
            var nivel = i < 4 ? "Normal" : i < 6 ? "Pre-Pico" : "Critico";
            eventos.Add(new EventoMetabolico
            {
                PacienteId = paciente.Id, NivelRiesgo = nivel,
                ProbabilidadMl = nivel == "Critico" ? 0.88 + rnd.NextDouble() * 0.1 : nivel == "Pre-Pico" ? 0.65 + rnd.NextDouble() * 0.15 : 0.2 + rnd.NextDouble() * 0.3,
                Descripcion = nivel == "Critico" ? "Pico detectado" : nivel == "Pre-Pico" ? "Signos pre-pico" : "Normal",
                FechaEvento = now.AddHours(-i * 3), Atendida = i < 5
            });
        }
        await SafeInsertMany(db.EventosMetabolicos, eventos, "eventos");

        // Alineado con producción: coordenadas cifradas, Ubicacion en claro vacío.
        TrackingGps NuevoTrack(DateTime ts, double lon, double lat, bool emerg) => new()
        {
            Meta = new MetaData { PacienteId = paciente.Id, DispositivoMac = macAddr },
            Timestamp = ts,
            Ubicacion = new UbicacionGps(),
            UbicacionCifrada = cripto.Encrypt($"{lon},{lat}"),
            EsEmergencia = emerg
        };
        await SafeInsertMany(db.TrackingGps, new List<TrackingGps>
        {
            NuevoTrack(now.AddMinutes(-30), -99.1332, 19.4326, false),
            NuevoTrack(now.AddMinutes(-20), -99.1335, 19.4328, false),
            NuevoTrack(now.AddMinutes(-10), -99.1340, 19.4330, true)
        }, "tracking");

        var medNames = new[] { ("Metformina", "500mg", "08:00,20:00"), ("Insulina", "10 unidades", "07:00,13:00,19:00"), ("Losartan", "50mg", "09:00") };
        foreach (var (name, dosis, horario) in medNames)
            await SafeInsertOne(db.Medicamentos, new Medicamento { PacienteId = paciente.Id, Nombre = name, Dosis = dosis, Horario = horario, Activo = true, FechaCreacion = now.AddDays(-rnd.Next(5, 30)), UltimaToma = now.AddHours(-rnd.Next(1, 12)) }, $"med:{name}");

        await SafeInsertMany(db.Alertas, new List<Alerta>
        {
            new() { PacienteId = paciente.Id, Tipo = "glucosa", Nivel = "critico", Titulo = "Pico de glucosa", Mensaje = "Glucosa en 280 mg/dL", Atendida = false, FechaCreacion = now.AddMinutes(-45), SensorData = new SensorData { PulsoBpm = 105, TemperaturaC = 37.8, ProbabilidadPico = 0.92 } },
            new() { PacienteId = paciente.Id, Tipo = "cardiaca", Nivel = "advertencia", Titulo = "FC elevada", Mensaje = "Pulso en 110 bpm", Atendida = true, FechaCreacion = now.AddHours(-6), FechaAtencion = now.AddHours(-5) },
            new() { PacienteId = paciente.Id, Tipo = "glucosa", Nivel = "informativo", Titulo = "Medicamento pendiente", Mensaje = "Tomar Metformina", Atendida = true, FechaCreacion = now.AddHours(-3), FechaAtencion = now.AddHours(-2) }
        }, "alertas");

        await SafeInsertMany(db.Notificaciones, new List<Notificacion>
        {
            new() { PacienteId = paciente.Id, UsuarioWebId = user.Id, Titulo = "Pico detectado", Mensaje = "Pico glucémico a las 14:30", Tipo = "alerta", Leida = false, FechaEnvio = now.AddMinutes(-45) },
            new() { PacienteId = paciente.Id, UsuarioWebId = user.Id, Titulo = "Medicamento tomado", Mensaje = "Metformina registrada", Tipo = "sistema", Leida = true, FechaEnvio = now.AddHours(-2) }
        }, "notificaciones");

        await SafeInsertOne(db.Dispositivos, new Dispositivo { PacienteId = paciente.Id, NombreDispositivo = "BioGuard Watch Pro", MacAddress = macAddr, Conectado = true, FechaVinculacion = now.AddDays(-30) }, "dispositivo");

        var cuidadorUser = new UsuarioWeb
        {
            Nombre = "Maria", ApellidoPaterno = "Martinez", ApellidoMaterno = "Ruiz",
            Correo = $"cuidador_{DateTime.UtcNow.Ticks}@bioguard.test",
            PasswordHash = PasswordHasher.Hash("Cuidador@123!"),
            ProveedorAuth = "local", PlanId = existingPlan.Id, Activo = true, FechaRegistro = now
        };
        await SafeInsertOne(db.UsuariosWeb, cuidadorUser, "cuidador-user");
        await SafeInsertOne(db.Cuidadores, new Cuidador
        {
            UsuarioWebId = cuidadorUser.Id, PacienteId = paciente.Id,
            CodigoAccesoQr = "CU-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
            Nombre = "Maria Martinez Ruiz", Parentesco = "Hija", Telefono = "5551234567",
            Correo = cuidadorUser.Correo, FechaAutorizacion = now.AddDays(-15)
        }, "cuidador");

        await SafeInsertOne(db.Pagos, new Pago
        {
            UsuarioWebId = user.Id, Monto = 0, Moneda = "MXN", PlanId = existingPlan.Id,
            StripeSessionId = $"cs_seed_{Guid.NewGuid():N}", StripeCustomerId = $"cus_seed_{Guid.NewGuid():N}",
            Estado = "completado", FechaPago = now.AddDays(-30), MetodoPago = "gratis"
        }, "pago");

        var version = $"1.0.{rnd.Next(0, 999)}";
        await SafeInsertOne(db.ModelosMl, new ModeloMl
        {
            Version = version, FechaEntrenamiento = now.AddDays(-7),
            Accuracy = 0.89, Precision = 0.87, Recall = 0.91, F1Score = 0.89,
            TotalMuestras = 5000, Activo = true, Descripcion = "Modelo ML de predicción de picos"
        }, "modelo-ml");

        await SafeInsertOne(db.PrediccionesMl, new PrediccionMl
        {
            PacienteId = paciente.Id, ProbabilidadPico = 0.72, NivelRiesgo = "Pre-Pico",
            HorasEstimadas = 4, Recomendacion = "Mantener hidratación y verificar glucosa en 2 horas",
            ModeloVersion = version, FechaPrediccion = now.AddMinutes(-30), FechaExpiracion = now.AddHours(2)
        }, "prediccion-ml");

        return Results.Ok(new
        {
            message = "Seed data inserted",
            userId = user.Id, pacienteId = paciente.Id, cuidadorUserId = cuidadorUser.Id,
            email = testEmail,
            skipped, stats = new { lecturas = lecturas.Count, eventos = eventos.Count, tracking = 3, medicamentos = medNames.Length, alertas = 3, notificaciones = 2, dispositivos = 1, cuidadores = 1, pagos = 1, modelos = 1, predicciones = 1 }
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during seed");
        return Results.Problem(ex.Message);
    }
});

// Seed solo accesible con header de secreto en Development
seedEndpoint.AllowAnonymous().AddEndpointFilter(async (ctx, next) =>
{
    var secret = app.Configuration["Seed:Secret"] ?? "dev-seed-secret";
    if (!ctx.HttpContext.Request.Headers.TryGetValue("X-Seed-Secret", out var val) || val != secret)
        return Results.Problem("Unauthorized", statusCode: 401);
    return await next(ctx);
});
}

app.Run();

static async Task CreateTtlIndex<T>(IMongoCollection<T> collection, string fieldName, int expirationSeconds)
{
    var indexKeys = Builders<T>.IndexKeys.Ascending(fieldName);
    var indexOptions = new CreateIndexOptions { ExpireAfter = TimeSpan.FromSeconds(expirationSeconds) };
    var indexModel = new CreateIndexModel<T>(indexKeys, indexOptions);
    await collection.Indexes.CreateOneAsync(indexModel);
}

static async Task CreateTimeSeriesCollectionIfNotExistsAsync(IMongoDatabase database, string collectionName, string timeField, string metaField)
{
    var filter = new BsonDocument("name", collectionName);
    var collections = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = filter });
    if (!await collections.AnyAsync())
    {
        var options = new CreateCollectionOptions
        {
            TimeSeriesOptions = new TimeSeriesOptions(timeField, metaField, TimeSeriesGranularity.Seconds)
        };
        await database.CreateCollectionAsync(collectionName, options);
    }
}

// ReSharper disable once EmptyNamespaceDeclaration
namespace BioGuard.Api
{
    public partial class Program { }
}
