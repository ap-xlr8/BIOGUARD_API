using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.DTOs;
using BioGuard.Api.Models;

namespace BioGuard.Api.Services;

public class AuthService
{
    private readonly IMongoDbContext _db;
    private readonly string _jwtKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;
    private readonly int _refreshTokenDays;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthService> _logger;
    private readonly IEmailService _emailService;

    public AuthService(IMongoDbContext db, IConfiguration config, HttpClient httpClient, ILogger<AuthService> logger, IEmailService emailService)
    {
        _db = db;
        _httpClient = httpClient;
        _logger = logger;
        _emailService = emailService;
        _jwtKey = config["Jwt:Key"] is { Length: > 0 } k ? k
            : Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? throw new InvalidOperationException("JWT secret key not configured.");
        if (Encoding.UTF8.GetByteCount(_jwtKey) < 32)
            throw new InvalidOperationException("JWT secret key must be at least 256 bits (32 bytes).");
        _issuer = config["Jwt:Issuer"] ?? "BioGuardApi";
        _audience = config["Jwt:Audience"] ?? "BioGuardApp";
        _expirationMinutes = int.Parse(config["Jwt:ExpirationMinutes"] ?? "60");
        _refreshTokenDays = int.Parse(config["Jwt:RefreshTokenDays"] ?? "7");
    }

    // ── Register ───────────────────────────────────────────

    public async Task<AuthResponse?> RegisterWebAsync(RegisterWebRequest request)
    {
        var exists = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == request.Correo);
        if (exists != null)
        {
            _logger.LogWarning("Registration attempt with existing email: {Email}", SecurityLog.MaskEmail(request.Correo));
            return null;
        }

        var planAliases = PlanCatalog.Aliases(request.PlanNombre);
        var plan = await _db.FindFirstOrDefaultAsync(
            _db.Planes, p => planAliases.Contains(p.Nombre) && p.Activo);
        if (plan == null)
        {
            _logger.LogWarning("Registration attempt with invalid plan: {PlanNombre}", request.PlanNombre);
            return null;
        }

        var (passwordValid, passwordError) = PasswordHasher.ValidateComplexity(request.Password);
        if (!passwordValid)
        {
            _logger.LogWarning("Registration with weak password for email: {Correo}", SecurityLog.MaskEmail(request.Correo));
            return null;
        }

        var verificationCode = RandomNumberString(6);
        var codeExpiry = DateTime.UtcNow.AddMinutes(10);

        var user = new UsuarioWeb
        {
            Nombre = InputSanitizer.StripHtml(request.Nombre),
            ApellidoPaterno = InputSanitizer.StripHtml(request.ApellidoPaterno),
            ApellidoMaterno = InputSanitizer.StripHtml(request.ApellidoMaterno),
            Correo = request.Correo,
            PasswordHash = PasswordHasher.Hash(request.Password),
            ProveedorAuth = "local",
            Rol = SystemRoles.Dueno,
            PlanId = plan.Id,
            Activo = false,
            TwoFactorCode = ComputeSha256Hash(verificationCode),
            TwoFactorExpira = codeExpiry,
            TwoFactorVerificado = false,
            FechaRegistro = DateTime.UtcNow
        };

        await _db.UsuariosWeb.InsertOneAsync(user);

        await _emailService.SendVerificationCodeAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}", verificationCode);

        _logger.LogInformation("User registered (pending verification): {UserId}", user.Id);

        return new AuthResponse("", user.Id, $"{user.Nombre} {user.ApellidoPaterno}", "dueno", plan.Nombre, RequiresVerification: true);
    }

    // ── Login Web ──────────────────────────────────────────

    public async Task<AuthResponse?> LoginWebAsync(LoginWebRequest request)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == request.Correo);
        if (user == null || !user.Activo)
        {
            _logger.LogWarning("Login attempt for inactive or non-existent user: {Email}", SecurityLog.MaskEmail(request.Correo));
            return null;
        }

        if (user.LockedUntil != null && user.LockedUntil > DateTime.UtcNow)
        {
            _logger.LogWarning("Login blocked - account locked until {LockedUntil}", user.LockedUntil);
            return null;
        }

        if (!PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            var attempts = user.FailedLoginAttempts + 1;
            var update = Builders<UsuarioWeb>.Update.Set(u => u.FailedLoginAttempts, attempts);
            if (attempts >= 5)
            {
                update = Builders<UsuarioWeb>.Update
                    .Set(u => u.FailedLoginAttempts, attempts)
                    .Set(u => u.LockedUntil, DateTime.UtcNow.AddMinutes(15));
                _logger.LogWarning("Account locked for user {Correo} after {Attempts} failed attempts", SecurityLog.MaskEmail(request.Correo), attempts);
            }
            await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, update);
            _logger.LogWarning("Invalid password for user: {UserId}", user.Id);
            return null;
        }

        if (user.FailedLoginAttempts > 0 || user.LockedUntil != null)
        {
            await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id,
                Builders<UsuarioWeb>.Update
                    .Set(u => u.FailedLoginAttempts, 0)
                    .Set(u => u.LockedUntil, null));
        }

        var role = string.IsNullOrWhiteSpace(user.Rol) ? SystemRoles.Dueno : user.Rol;

        var requires2FA = user.TwoFactorHabilitado;
        if (role == SystemRoles.Administrador && !requires2FA)
        {
            requires2FA = true;
        }

        if (requires2FA)
        {
            var codigo = RandomNumberString(6);
            var expira = DateTime.UtcNow.AddMinutes(10);
            var update2fa = Builders<UsuarioWeb>.Update
                .Set(u => u.TwoFactorCode, ComputeSha256Hash(codigo))
                .Set(u => u.TwoFactorExpira, expira)
                .Set(u => u.TwoFactorVerificado, false);
            await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, update2fa);
            _logger.LogInformation("2FA required for user: {UserId}", user.Id);
            return new AuthResponse("", user.Id, "", "", "", Requires2FA: true);
        }

        var plan = await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == user.PlanId);
        var duenoExtra = await PacienteIdClaimParaDuenoAsync(user.Id);
        var token = GenerateToken(user.Id, user.Correo, role, duenoExtra);
        var refreshToken = await CreateAndStoreRefreshTokenAsync(user.Id);
        _logger.LogInformation("User logged in successfully: {UserId}", user.Id);

        return new AuthResponse(token, user.Id, $"{user.Nombre} {user.ApellidoPaterno}", role, plan?.Nombre ?? "Sin plan", RefreshToken: refreshToken);
    }

    // ── Login Google (Deshabilitado) ───────────────────────

    public Task<AuthResponse?> LoginGoogleAsync(LoginGoogleRequest request)
    {
        _logger.LogWarning("LoginGoogleAsync called but Google login is disabled.");
        return Task.FromResult<AuthResponse?>(null);
    }

    // ── Login por Código (Móvil) ───────────────────────────

    public async Task<LoginCodigoResponse?> LoginByCodigoAsync(LoginCodigoRequest request)
    {
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.CodigoAccesoQr == request.CodigoAcceso);
        if (paciente != null)
        {
            if (paciente.BloqueadoHasta.HasValue && paciente.BloqueadoHasta.Value > DateTime.UtcNow)
            {
                _logger.LogWarning("Patient blocked until {BloqueadoHasta}", paciente.BloqueadoHasta);
                return null;
            }
            if (paciente.CodigoExpira.HasValue && paciente.CodigoExpira.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("Patient code expired for {PacienteId}", paciente.Id);
                return null;
            }

            paciente.IntentosFallidos = 0;
            paciente.CodigoExpira = null;
            paciente.BloqueadoHasta = null;
            await _db.Pacientes.ReplaceOneAsync(p => p.Id == paciente.Id, paciente);

            var token = GenerateToken(paciente.Id, paciente.Id, "paciente");
            var refreshToken = await CreateAndStoreRefreshTokenAsync(paciente.Id);
            _logger.LogInformation("Patient login by code: {PacienteId}", paciente.Id);
            return new LoginCodigoResponse(token, refreshToken, paciente.Id, paciente.Nombre, "paciente");
        }

        var cuidador = await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.CodigoAccesoQr == request.CodigoAcceso);
        if (cuidador != null)
        {
            if (cuidador.BloqueadoHasta.HasValue && cuidador.BloqueadoHasta.Value > DateTime.UtcNow)
            {
                _logger.LogWarning("Caregiver blocked until {BloqueadoHasta}", cuidador.BloqueadoHasta);
                return null;
            }
            if (cuidador.CodigoExpira.HasValue && cuidador.CodigoExpira.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("Caregiver code expired for {CuidadorId}", cuidador.Id);
                return null;
            }

            cuidador.IntentosFallidos = 0;
            cuidador.CodigoExpira = null;
            cuidador.BloqueadoHasta = null;
            await _db.Cuidadores.ReplaceOneAsync(c => c.Id == cuidador.Id, cuidador);

            var extra = new Dictionary<string, string> { { "nivel_acceso", cuidador.NivelAcceso } };
            var token = GenerateToken(cuidador.Id, cuidador.Id, "cuidador", extra);
            var refreshToken = await CreateAndStoreRefreshTokenAsync(cuidador.Id);
            _logger.LogInformation("Caregiver login by code: {CuidadorId}", cuidador.Id);
            return new LoginCodigoResponse(token, refreshToken, cuidador.Id, cuidador.Nombre, "cuidador");
        }

        _logger.LogWarning("Login by code failed: code not found");
        return null;
    }

    // ── Refresh Token ──────────────────────────────────────

    public async Task<RefreshTokenResponse?> RefreshTokenAsync(RefreshTokenRequest request, string? ip = null)
    {
        var hashedToken = ComputeSha256Hash(request.RefreshToken);
        // Atomic revoke: only succeeds if token exists and is not yet revoked (prevents rotation race)
        var filter = Builders<RefreshToken>.Filter.And(
            Builders<RefreshToken>.Filter.Eq(t => t.Token, hashedToken),
            Builders<RefreshToken>.Filter.Eq(t => t.RevokedAt, null)
        );
        var revokeUpdate = Builders<RefreshToken>.Update.Set(t => t.RevokedAt, DateTime.UtcNow);
        var revokeResult = await _db.RefreshTokens.UpdateOneAsync(filter, revokeUpdate);

        if (revokeResult.ModifiedCount == 0)
        {
            var alreadyRevoked = await _db.FindFirstOrDefaultAsync(_db.RefreshTokens, t =>
                t.Token == hashedToken);
            if (alreadyRevoked != null && alreadyRevoked.IsRevoked)
            {
                _logger.LogWarning("Reused revoked refresh token, revoking chain for user: {UsuarioId}", alreadyRevoked.UsuarioId);
                await RevokeRefreshTokenChainAsync(alreadyRevoked.UsuarioId);
            }
            else
            {
                _logger.LogWarning("Refresh token attempt with non-existent token");
            }
            return null;
        }

        // Fetch the revoked token data
        var stored = await _db.FindFirstOrDefaultAsync(_db.RefreshTokens, t =>
            t.Token == hashedToken);

        if (stored == null) return null;

        if (stored.IsExpired)
        {
            _logger.LogWarning("Refresh token attempt with expired token for user: {UsuarioId}", stored.UsuarioId);
            return null;
        }

        var (userId, nombre, role, extraClaims) = await ResolveUserRoleAsync(stored.UsuarioId);
        if (userId == null)
        {
            _logger.LogWarning("Refresh token user not found: {UsuarioId}", stored.UsuarioId);
            return null;
        }

        var newRefreshToken = GenerateRefreshToken();
        var newHashedToken = ComputeSha256Hash(newRefreshToken);

        // Record replacement chain on the old token
        await _db.RefreshTokens.UpdateOneAsync(t => t.Id == stored.Id,
            Builders<RefreshToken>.Update.Set(t => t.ReplacedBy, newHashedToken));

        await _db.RefreshTokens.InsertOneAsync(new RefreshToken
        {
            UsuarioId = userId,
            Token = newHashedToken,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshTokenDays),
            Ip = ip
        });

        var accessToken = GenerateToken(userId, userId, role, extraClaims);
        _logger.LogInformation("Token refreshed for user: {UserId} (role: {Role})", userId, role);

        return new RefreshTokenResponse(accessToken, newRefreshToken);
    }

    private async Task<(string? userId, string? nombre, string role, Dictionary<string, string>? extraClaims)> ResolveUserRoleAsync(string usuarioId)
    {
        var dueno = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == usuarioId);
        if (dueno != null)
        {
            var role = string.IsNullOrWhiteSpace(dueno.Rol) ? SystemRoles.Dueno : dueno.Rol;
            var duenoExtra = await PacienteIdClaimParaDuenoAsync(dueno.Id);
            return (dueno.Id, $"{dueno.Nombre} {dueno.ApellidoPaterno}", role, duenoExtra);
        }

        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == usuarioId);
        if (paciente != null)
            return (paciente.Id, paciente.Nombre, "paciente", null);

        var cuidador = await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.Id == usuarioId);
        if (cuidador != null)
        {
            var extra = new Dictionary<string, string> { { "nivel_acceso", cuidador.NivelAcceso } };
            return (cuidador.Id, cuidador.Nombre, "cuidador", extra);
        }

        return (null, null, "", null);
    }

    public async Task RevokeRefreshTokenChainAsync(string usuarioId)
    {
        var filter = Builders<RefreshToken>.Filter.Where(t => t.UsuarioId == usuarioId);
        var update = Builders<RefreshToken>.Update.Set(t => t.RevokedAt, DateTime.UtcNow);
        await _db.RefreshTokens.UpdateManyAsync(filter, update);
        _logger.LogWarning("All refresh tokens revoked for user: {UsuarioId}", usuarioId);
    }

    public async Task RevokeRefreshTokenAsync(RefreshToken token)
    {
        var hashedToken = ComputeSha256Hash(token.Token);
        var hashedReplacedBy = token.ReplacedBy != null ? ComputeSha256Hash(token.ReplacedBy) : null;

        var filter = Builders<RefreshToken>.Filter.Where(t =>
            t.Token == hashedToken || t.Token == token.Token ||
            (hashedReplacedBy != null && (t.Token == hashedReplacedBy || t.Token == token.ReplacedBy)));

        var update = Builders<RefreshToken>.Update.Set(t => t.RevokedAt, DateTime.UtcNow);

        await _db.RefreshTokens.UpdateManyAsync(filter, update);
    }

    // ── 2FA ────────────────────────────────────────────────

    public async Task<bool> Enviar2FAAsync(Enviar2FARequest request)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == request.Correo);
        if (user == null)
        {
            _logger.LogWarning("2FA send attempt for non-existent user: {Email}", SecurityLog.MaskEmail(request.Correo));
            return false;
        }

        var codigo = RandomNumberString(6);
        var expira = DateTime.UtcNow.AddMinutes(10);

        var update = Builders<UsuarioWeb>.Update
            .Set(u => u.TwoFactorCode, ComputeSha256Hash(codigo))
            .Set(u => u.TwoFactorExpira, expira)
            .Set(u => u.TwoFactorVerificado, false);

        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, update);
        _logger.LogInformation("2FA code sent to user: {UserId}", user.Id);

        await _emailService.SendVerificationCodeAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}", codigo);

        return true;
    }

    public async Task<AuthResponse?> Verificar2FAAsync(Verificar2FARequest request)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == request.Correo);
        if (user == null)
        {
            _logger.LogWarning("2FA verification attempt for non-existent user: {Email}", SecurityLog.MaskEmail(request.Correo));
            return null;
        }

        if (user.TwoFactorLockedUntil != null && user.TwoFactorLockedUntil > DateTime.UtcNow)
        {
            _logger.LogWarning("2FA verification blocked - account 2FA locked until {LockedUntil}", user.TwoFactorLockedUntil);
            return null;
        }

        if (string.IsNullOrEmpty(user.TwoFactorCode)) return null;
        if (user.TwoFactorExpira == null || user.TwoFactorExpira < DateTime.UtcNow)
        {
            _logger.LogWarning("2FA verification attempt with expired code for user: {UserId}", user.Id);
            return null;
        }

        var requestCodigo = request.Codigo ?? request.CodigoOtp;
        if (string.IsNullOrEmpty(requestCodigo)) return null;

        var hashedRequestCode = ComputeSha256Hash(requestCodigo);
        var codeMatch = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(user.TwoFactorCode),
            Encoding.UTF8.GetBytes(hashedRequestCode));
        if (!codeMatch)
        {
            var attempts = user.Failed2FAAttempts + 1;
            var update = Builders<UsuarioWeb>.Update.Set(u => u.Failed2FAAttempts, attempts);
            if (attempts >= 5)
            {
                update = Builders<UsuarioWeb>.Update
                    .Set(u => u.Failed2FAAttempts, attempts)
                    .Set(u => u.TwoFactorLockedUntil, DateTime.UtcNow.AddMinutes(15));
                _logger.LogWarning("2FA account locked for user {UserId} after {Attempts} failed attempts", user.Id, attempts);
            }
            await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, update);
            _logger.LogWarning("2FA verification failed with invalid code for user: {UserId}", user.Id);
            return null;
        }

        var wasInactive = !user.Activo;

        var updateReset = Builders<UsuarioWeb>.Update
            .Set(u => u.TwoFactorCode, null)
            .Set(u => u.TwoFactorExpira, null)
            .Set(u => u.TwoFactorVerificado, true)
            .Set(u => u.Activo, true)
            .Set(u => u.Failed2FAAttempts, 0)
            .Set(u => u.TwoFactorLockedUntil, null);

        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, updateReset);

        var userRole = string.IsNullOrWhiteSpace(user.Rol) ? SystemRoles.Dueno : user.Rol;
        var plan = await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == user.PlanId);
        var token = GenerateToken(user.Id, user.Correo, userRole);
        var refreshToken = await CreateAndStoreRefreshTokenAsync(user.Id);
        _logger.LogInformation("2FA verified successfully for user: {UserId} (activated={WasInactive})", user.Id, wasInactive);

        return new AuthResponse(token, user.Id, $"{user.Nombre} {user.ApellidoPaterno}", userRole, plan?.Nombre ?? "Sin plan", RefreshToken: refreshToken);
    }

    // ── Forgot Password ────────────────────────────────────

    public async Task<bool> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == request.Correo);
        if (user == null || !user.Activo)
        {
            _logger.LogWarning("Password reset attempt for inactive or non-existent user: {Email}", SecurityLog.MaskEmail(request.Correo));
            return false;
        }

        var token = GenerateRandomToken();
        var expira = DateTime.UtcNow.AddHours(1);

        var update = Builders<UsuarioWeb>.Update
            .Set(u => u.ResetPasswordToken, ComputeSha256Hash(token))
            .Set(u => u.ResetPasswordExpira, expira);

        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, update);
        _logger.LogInformation("Password reset token generated for user: {UserId}", user.Id);

        var resetLink = $"https://bioguard.app/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(request.Correo)}";
        await _emailService.SendPasswordResetAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}", resetLink);

        return true;
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.Correo))
        {
            _logger.LogWarning("Password reset attempt without email");
            return false;
        }

        var hashedToken = ComputeSha256Hash(request.Token);
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == request.Correo && u.ResetPasswordToken == hashedToken);

        if (user == null)
        {
            _logger.LogWarning("Password reset attempt with invalid token or email");
            return false;
        }
        if (user.ResetPasswordExpira == null || user.ResetPasswordExpira < DateTime.UtcNow)
        {
            _logger.LogWarning("Password reset attempt with expired token for user: {UserId}", user.Id);
            return false;
        }

        var (passwordValid, _) = PasswordHasher.ValidateComplexity(request.NuevaPassword);
        if (!passwordValid)
        {
            _logger.LogWarning("Password reset with weak password for user: {UserId}", user.Id);
            return false;
        }

        var update = Builders<UsuarioWeb>.Update
            .Set(u => u.PasswordHash, PasswordHasher.Hash(request.NuevaPassword))
            .Set(u => u.ResetPasswordToken, null)
            .Set(u => u.ResetPasswordExpira, null);

        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, update);
        _logger.LogInformation("Password reset successfully for user: {UserId}", user.Id);

        // Revoca todas las sesiones renovables (ver nota en CambiarPasswordAsync).
        await RevokeRefreshTokenChainAsync(user.Id);

        await _emailService.SendPasswordChangedNotificationAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}");

        return true;
    }

    // ── Cambiar Password (logueado) ────────────────────────

    public async Task<bool> CambiarPasswordAsync(string userId, CambiarPasswordRequest request)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == userId);
        if (user == null)
        {
            _logger.LogWarning("Password change attempt for non-existent user: {UserId}", userId);
            return false;
        }

        if (!PasswordHasher.Verify(request.PasswordActual, user.PasswordHash))
        {
            _logger.LogWarning("Password change failed: invalid current password for user: {UserId}", userId);
            return false;
        }

        var (passwordValid, _) = PasswordHasher.ValidateComplexity(request.NuevaPassword);
        if (!passwordValid)
        {
            _logger.LogWarning("Password change with weak password for user: {UserId}", userId);
            return false;
        }

        var update = Builders<UsuarioWeb>.Update
            .Set(u => u.PasswordHash, PasswordHasher.Hash(request.NuevaPassword));

        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == userId, update);
        _logger.LogInformation("Password changed successfully for user: {UserId}", userId);

        // Revoca todas las sesiones renovables. El access token vigente (corto, ~30 min)
        // no se puede invalidar sin su jti; expira por sí solo y no es renovable tras esto.
        await RevokeRefreshTokenChainAsync(userId);

        await _emailService.SendPasswordChangedNotificationAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}");

        return true;
    }

    // ── Token Revocation ──────────────────────────────────

    public async Task RevokeTokenAsync(string jti, DateTime expiresAt)
    {
        await _db.TokenBlacklist.InsertOneAsync(new TokenBlacklist
        {
            Jti = jti,
            ExpiresAt = expiresAt
        });
        _logger.LogInformation("Token revoked: {Jti}", SecurityLog.Fingerprint(jti));
    }

    public async Task<bool> IsTokenRevokedAsync(string jti)
    {
        var blacklisted = await _db.FindFirstOrDefaultAsync(_db.TokenBlacklist, t => t.Jti == jti);
        return blacklisted != null;
    }

    // ── Logout All ─────────────────────────────────────────

    public async Task<(bool success, int revokedCount)> LogoutAllAsync(string currentUserId, string currentUserRole, string? targetUserId = null)
    {
        var actualTarget = targetUserId ?? currentUserId;

        if (targetUserId != null && targetUserId != currentUserId && currentUserRole == "dueno")
        {
            var cuidador = await _db.FindFirstOrDefaultAsync(_db.Cuidadores, c => c.Id == targetUserId);
            if (cuidador != null)
            {
                var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.Id == cuidador.PacienteId);
                if (paciente == null || paciente.UsuarioWebId != currentUserId)
                    return (false, 0);
            }
            else
            {
                return (false, 0);
            }
        }
        else if (targetUserId != null && targetUserId != currentUserId)
        {
            return (false, 0);
        }

        await RevokeRefreshTokenChainAsync(actualTarget);

        var existing = await _db.FindToListAsync(_db.RefreshTokens, t => t.UsuarioId == actualTarget && t.RevokedAt != null);
        var revokedCount = existing.Count;

        _logger.LogInformation("All sessions revoked for user: {UserId} (by: {CurrentUserId})", actualTarget, currentUserId);
        return (true, revokedCount);
    }

    public virtual async Task<bool> ForgotPasswordAsync(string email, string baseUrl)
    {
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Correo == email);
        if (user == null)
        {
            _logger.LogWarning("Forgot password requested for non-existent email: {Email}", SecurityLog.MaskEmail(email));
            return false;
        }

        var resetToken = GenerateRandomToken();
        var update = Builders<UsuarioWeb>.Update
            .Set(u => u.ResetPasswordToken, ComputeSha256Hash(resetToken))
            .Set(u => u.ResetPasswordExpira, DateTime.UtcNow.AddHours(1));

        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, update);

        var resetLink = $"{baseUrl.TrimEnd('/')}/reset-password?token={resetToken}";
        await _emailService.SendPasswordResetAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}", resetLink);

        _logger.LogInformation("Password reset token generated and sent for: {Email}", SecurityLog.MaskEmail(email));
        return true;
    }

    public virtual async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        var hashedToken = ComputeSha256Hash(token);
        var user = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.ResetPasswordToken == hashedToken && u.ResetPasswordExpira > DateTime.UtcNow);
        if (user == null)
        {
            _logger.LogWarning("Reset password failed: invalid or expired token: {Token}", SecurityLog.Fingerprint(token));
            return false;
        }

        var (passwordValid, _) = PasswordHasher.ValidateComplexity(newPassword);
        if (!passwordValid)
        {
            _logger.LogWarning("Reset password failed: weak password for user: {UserId}", user.Id);
            return false;
        }

        var update = Builders<UsuarioWeb>.Update
            .Set(u => u.PasswordHash, PasswordHasher.Hash(newPassword))
            .Set(u => u.ResetPasswordToken, null)
            .Set(u => u.ResetPasswordExpira, null);

        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == user.Id, update);
        _logger.LogInformation("Password reset successfully for user: {UserId}", user.Id);

        await _emailService.SendPasswordChangedNotificationAsync(user.Correo, $"{user.Nombre} {user.ApellidoPaterno}");
        return true;
    }

    // ── Helpers ────────────────────────────────────────────

    private async Task<Dictionary<string, string>?> PacienteIdClaimParaDuenoAsync(string usuarioWebId)
    {
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.UsuarioWebId == usuarioWebId);
        if (paciente == null) return null;
        return new Dictionary<string, string> { { "paciente_id", paciente.Id } };
    }

    private async Task<string> CreateAndStoreRefreshTokenAsync(string userId)
    {
        var refreshToken = GenerateRefreshToken();
        var hashedToken = ComputeSha256Hash(refreshToken);
        await _db.RefreshTokens.InsertOneAsync(new RefreshToken
        {
            UsuarioId = userId,
            Token = hashedToken,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshTokenDays),
        });
        _logger.LogInformation("Refresh token created for user: {UserId}", userId);
        return refreshToken;
    }

    internal string GenerateToken(string id, string email, string role, Dictionary<string, string>? extraClaims = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, id),
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (extraClaims != null)
        {
            foreach (var (claimType, claimValue) in extraClaims)
                claims.Add(new Claim(claimType, claimValue));
        }

        if (role == "paciente")
            claims.Add(new Claim("paciente_id", id));

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expirationMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string RandomNumberString(int length)
    {
        var numbers = new char[length];
        for (int i = 0; i < length; i++)
            numbers[i] = (char)RandomNumberGenerator.GetInt32('0', '9' + 1);
        return new string(numbers);
    }

    private static string GenerateRandomToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_");
    }

    private static string ComputeSha256Hash(string rawInput)
    {
        if (string.IsNullOrEmpty(rawInput)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawInput));
        return Convert.ToHexString(bytes);
    }


}

// ── PBKDF2 Password Hasher ──────────────────────────────

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string hash)
    {
        var parts = hash.Split('.', 3);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var iterations)) return false;

        var salt = Convert.FromBase64String(parts[1]);
        var key = Convert.FromBase64String(parts[2]);
        var computed = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, key.Length);

        return CryptographicOperations.FixedTimeEquals(computed, key);
    }

    public static (bool valid, string error) ValidateComplexity(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return (false, "La contraseña debe tener al menos 8 caracteres");
        if (!password.Any(char.IsUpper))
            return (false, "La contraseña debe contener al menos una mayúscula");
        if (!password.Any(char.IsLower))
            return (false, "La contraseña debe contener al menos una minúscula");
        if (!password.Any(char.IsDigit))
            return (false, "La contraseña debe contener al menos un número");
        if (!password.Any(c => "!@#$%^&*()_+-=[]{}|;':\",./<>?".Contains(c)))
            return (false, "La contraseña debe contener al menos un carácter especial");
        return (true, string.Empty);
    }
}
