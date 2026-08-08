using System.Text;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using BioGuard.Api.Services;

namespace BioGuard.Api.Config;

public static class ServiceExtensions
{
    public static void ConfigureJwtAuthentication(this IServiceCollection services, IConfiguration config, string jwtKey)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidIssuer = config["Jwt:Issuer"] ?? "BioGuardApi",
                ValidAudience = config["Jwt:Audience"] ?? "BioGuardApp",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) &&
                        path.StartsWithSegments("/hubs/bioguard"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    try
                    {
                        var jti = context.Principal?.FindFirst("jti")?.Value;
                        if (!string.IsNullOrEmpty(jti))
                        {
                            using var scope = context.HttpContext.RequestServices.CreateScope();
                            var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
                            if (await authService.IsTokenRevokedAsync(jti))
                            {
                                context.Fail("Token has been revoked");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();
                        logger.LogWarning(ex, "Error during token validation (OnTokenValidated)");
                    }
                }
            };
        });

        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("PacienteOnly", policy => policy.RequireRole("paciente"));
            options.AddPolicy("DuenoOnly", policy => policy.RequireRole("dueno"));
            options.AddPolicy("CuidadorOnly", policy => policy.RequireRole("cuidador"));
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("administrador"));
            options.AddPolicy("CanReadPatientData", policy =>
                policy.RequireRole("paciente", "cuidador", "dueno", "administrador"));
            options.AddPolicy("CanWriteSensorData", policy =>
                policy.RequireRole("paciente"));
        });
    }

    public static void ConfigureRateLimiting(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.Configure<IpRateLimitOptions>(options =>
        {
            options.EnableEndpointRateLimiting = true;
            options.StackBlockedRequests = false;
            options.HttpStatusCode = 429;
            options.GeneralRules = new List<RateLimitRule>
            {
                new RateLimitRule { Endpoint = "*", Period = "1m", Limit = 100 },
                new RateLimitRule { Endpoint = "*:*:post", Period = "1m", Limit = 30 },
                new RateLimitRule { Endpoint = "post:/api/Auth/login-web", Period = "1m", Limit = 5 },
                new RateLimitRule { Endpoint = "post:/api/Auth/login-codigo", Period = "1m", Limit = 5 },
                new RateLimitRule { Endpoint = "post:/api/Auth/register", Period = "1m", Limit = 3 },
                new RateLimitRule { Endpoint = "post:/api/Auth/2FA/enviar", Period = "1m", Limit = 3 },
                new RateLimitRule { Endpoint = "post:/api/Auth/2FA/verificar", Period = "1m", Limit = 5 },
                new RateLimitRule { Endpoint = "post:/api/Auth/forgot-password", Period = "1m", Limit = 3 },
                new RateLimitRule { Endpoint = "post:/api/Auth/refresh", Period = "1m", Limit = 10 },
                new RateLimitRule { Endpoint = "post:/api/Auth/reset-password", Period = "1m", Limit = 3 },
                new RateLimitRule { Endpoint = "put:/api/Auth/cambiar-password", Period = "1m", Limit = 3 },
                new RateLimitRule { Endpoint = "post:/api/Sensores/lectura", Period = "1m", Limit = 60 },
                new RateLimitRule { Endpoint = "post:/api/Sensores/lectura-batch", Period = "1m", Limit = 10 },
                new RateLimitRule { Endpoint = "post:/api/Sensores/lecturas", Period = "1m", Limit = 10 },
                new RateLimitRule { Endpoint = "post:/api/Sensores/tracking", Period = "1m", Limit = 30 },
                new RateLimitRule { Endpoint = "post:/api/Sensores/tracking-batch", Period = "1m", Limit = 6 }
            };
        });
        services.Configure<ClientRateLimitOptions>(options =>
        {
            options.ClientIdHeader = "X-ClientId";
            options.HttpStatusCode = 429;
        });
        services.AddInMemoryRateLimiting();
        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
    }

    public static void ConfigureCors(this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        if (isDevelopment)
        {
            allowedOrigins = allowedOrigins.Concat(new[] { "http://localhost:3000" }).ToArray();
        }

        services.AddCors(options =>
        {
            options.AddPolicy("BioGuardPolicy", policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                          .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                          .WithHeaders("Authorization", "Content-Type", "Accept")
                          .AllowCredentials();
                }
                else
                {
                    policy.WithOrigins("https://bioguard.app")
                          .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                          .WithHeaders("Authorization", "Content-Type", "Accept")
                          .AllowCredentials();
                }
            });
        });
    }
}
