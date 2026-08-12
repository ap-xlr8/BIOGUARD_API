using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using BioGuard.Api.DTOs;
using BioGuard.Api.Services;

namespace BioGuard.Api.Controllers;

/// <summary>
/// MÓDULO 1: Autenticación y Acceso
/// ENDPOINT WEB + MÓVIL
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string ProductionRefreshCookie = "__Host-bioguard_refresh";
    private const string DevelopmentRefreshCookie = "bioguard_refresh_dev";
    private readonly AuthService _authService;
    private readonly AuditoriaService _auditoriaService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuthService authService, AuditoriaService auditoriaService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _auditoriaService = auditoriaService;
        _logger = logger;
    }

    private void SetWebRefreshCookie(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var secure = Request.IsHttps;
        Response.Cookies.Append(
            secure ? ProductionRefreshCookie : DevelopmentRefreshCookie,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                MaxAge = TimeSpan.FromDays(7),
                IsEssential = true
            });
    }

    private string? GetWebRefreshCookie()
    {
        return Request.Cookies[ProductionRefreshCookie]
            ?? Request.Cookies[DevelopmentRefreshCookie];
    }

    private void ClearWebRefreshCookie()
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        };
        Response.Cookies.Delete(ProductionRefreshCookie, options);
        Response.Cookies.Delete(DevelopmentRefreshCookie, options);
    }

    private static object ToWebAuthResponse(AuthResponse result) => new
    {
        result.Token,
        result.UserId,
        result.Nombre,
        result.Rol,
        result.Plan,
        result.Requires2FA,
        result.RequiresVerification
    };

    // ── Registro ──────────────────────────────────────────────
    // POST /api/Auth/register [WEB]

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterWebRequest request)
    {
        _logger.LogInformation("Register attempt for email: {Correo}", SecurityLog.MaskEmail(request.Correo));
        var result = await _authService.RegisterWebAsync(request);
        if (result == null)
        {
            _logger.LogWarning("Register failed for email: {Correo} - email exists or invalid plan", SecurityLog.MaskEmail(request.Correo));
            return BadRequest(new { message = "El correo ya existe, plan inválido o contraseña débil" });
        }
        _logger.LogInformation("Register successful for email: {Correo}", SecurityLog.MaskEmail(request.Correo));
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(result.UserId, "registro", "usuarios_web", result.UserId, ip);
        if (result.RequiresVerification)
        {
            return Ok(new { message = "Código de verificación enviado a tu correo", requiresVerification = true, userId = result.UserId, correo = request.Correo });
        }
        return Ok(result);
    }

    // ── Login ─────────────────────────────────────────────────
    // POST /api/Auth/login-web [WEB]

    [HttpPost("login-web")]
    public async Task<IActionResult> LoginWeb([FromBody] LoginWebRequest request)
    {
        _logger.LogInformation("Web login attempt for email: {Correo}", SecurityLog.MaskEmail(request.Correo));
        var result = await _authService.LoginWebAsync(request);
        if (result == null)
        {
            _logger.LogWarning("Web login failed for email: {Correo} - invalid credentials", SecurityLog.MaskEmail(request.Correo));
            var failIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _auditoriaService.RegistrarAsync("unknown", "login_fallido", "usuarios_web", request.Correo, failIp);
            return Unauthorized(new { message = "Credenciales inválidas" });
        }
        _logger.LogInformation("Web login successful for email: {Correo}", SecurityLog.MaskEmail(request.Correo));
        var loginIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(result.UserId, "login", "usuarios_web", result.UserId, loginIp);
        if (result.Requires2FA)
        {
            return Ok(new { message = "Código 2FA enviado al correo", requires2FA = true, userId = result.UserId });
        }
        SetWebRefreshCookie(result.RefreshToken);
        return Ok(ToWebAuthResponse(result));
    }

    // POST /api/Auth/login-google [DISABLED]

    [HttpPost("login-google")]
    public IActionResult LoginGoogle([FromBody] LoginGoogleRequest request)
    {
        _logger.LogWarning("Disabled Google login attempt blocked");
        return StatusCode(StatusCodes.Status410Gone, new { message = "El inicio de sesión con Google ya no está habilitado" });
    }

    // POST /api/Auth/login-codigo [MÓVIL]

    [HttpPost("login-codigo")]
    public async Task<IActionResult> LoginByCodigo([FromBody] LoginCodigoRequest request)
    {
        _logger.LogInformation("Login by codigo attempt: {CodigoAcceso}", SecurityLog.Fingerprint(request.CodigoAcceso));
        var result = await _authService.LoginByCodigoAsync(request);
        if (result == null)
        {
            _logger.LogWarning("Login by codigo failed: {CodigoAcceso}", SecurityLog.Fingerprint(request.CodigoAcceso));
            return Unauthorized(new { message = "Codigo invalido o expirado" });
        }
        _logger.LogInformation("Login by codigo successful");
        return Ok(result);
    }

    // ── 2FA ───────────────────────────────────────────────────
    // POST /api/Auth/2FA/enviar [WEB]

    [HttpPost("2FA/enviar")]
    [HttpPost("enviar-2fa")]
    public async Task<IActionResult> Enviar2FA([FromBody] Enviar2FARequest request)
    {
        _logger.LogInformation("2FA send attempt for email: {Email}", SecurityLog.MaskEmail(request.Correo));
        var result = await _authService.Enviar2FAAsync(request);
        if (!result)
        {
            _logger.LogWarning("2FA send failed for email: {Email} - email not found or inactive", SecurityLog.MaskEmail(request.Correo));
        }
        _logger.LogInformation("2FA code request processed for email: {Email}", SecurityLog.MaskEmail(request.Correo));
        return Ok(new { message = "Si el correo está registrado, recibirás un código" });
    }

    // POST /api/Auth/2FA/verificar [WEB]

    [HttpPost("2FA/verificar")]
    [HttpPost("verificar-2fa")]
    public async Task<IActionResult> Verificar2FA([FromBody] Verificar2FARequest request)
    {
        _logger.LogInformation("2FA verification attempt");
        var result = await _authService.Verificar2FAAsync(request);
        if (result == null)
        {
            _logger.LogWarning("2FA verification failed - invalid or expired code");
            return BadRequest(new { message = "Código inválido o expirado" });
        }
        _logger.LogInformation("2FA verification successful");
        SetWebRefreshCookie(result.RefreshToken);
        return Ok(ToWebAuthResponse(result));
    }

    // ── Refresh Token ──────────────────────────────────────
    // POST /api/Auth/refresh [WEB]

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest? request)
    {
        _logger.LogInformation("Token refresh attempt");
        var bodyRefreshToken = request?.RefreshToken;
        var mobileTokenSupplied = !string.IsNullOrWhiteSpace(bodyRefreshToken);
        var refreshToken = mobileTokenSupplied
            ? bodyRefreshToken
            : GetWebRefreshCookie();
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Unauthorized(new { message = "Refresh token requerido" });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.RefreshTokenAsync(new RefreshTokenRequest(refreshToken), ip);
        if (result == null)
        {
            _logger.LogWarning("Token refresh failed - invalid or expired refresh token");
            return Unauthorized(new { message = "Refresh token inválido o expirado" });
        }
        _logger.LogInformation("Token refresh successful");
        SetWebRefreshCookie(result.RefreshToken);
        return mobileTokenSupplied
            ? Ok(result)
            : Ok(new { result.AccessToken });
    }

    // ── Recuperación de contraseña ────────────────────────────
    // POST /api/Auth/forgot-password [WEB]

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        _logger.LogInformation("Forgot password attempt for email: {Correo}", SecurityLog.MaskEmail(request.Correo));
        await _authService.ForgotPasswordAsync(request);
        return Ok(new { message = "Si el correo está registrado, recibirás un link de recuperación" });
    }

    // POST /api/Auth/reset-password [WEB]

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        _logger.LogInformation("Password reset attempt");
        var result = await _authService.ResetPasswordAsync(request);
        if (!result)
        {
            _logger.LogWarning("Password reset failed - invalid or expired token");
            return BadRequest(new { message = "Token inválido o expirado" });
        }
        _logger.LogInformation("Password reset successful");
        return Ok(new { message = "Contraseña actualizada correctamente" });
    }

    // ── Cambio de contraseña (logueado) ───────────────────────
    // PUT /api/Auth/cambiar-password [WEB]

    [HttpPut("cambiar-password")]
    [Authorize]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized();

        _logger.LogInformation("Password change attempt for user: {UserId}", userId);
        var result = await _authService.CambiarPasswordAsync(userId, request);
        if (!result)
        {
            _logger.LogWarning("Password change failed for user: {UserId} - incorrect current password", userId);
            return BadRequest(new { message = "Password actual incorrecto" });
        }
        _logger.LogInformation("Password changed successfully for user: {UserId}", userId);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(userId, "cambiar_password", "usuarios_web", userId, ip);
        return Ok(new { message = "Contraseña actualizada correctamente" });
    }

    // ── Logout ───────────────────────────────────────────────
    // POST /api/Auth/logout [WEB]

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirst("jti")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var expClaim = User.FindFirst("exp")?.Value;

        if (jti == null) return BadRequest(new { message = "Token inválido" });

        var expiresAt = expClaim != null && long.TryParse(expClaim, out var expSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime
            : DateTime.UtcNow.AddMinutes(30);

        await _authService.RevokeTokenAsync(jti, expiresAt);
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
            await _authService.RevokeRefreshTokenChainAsync(userId);
        ClearWebRefreshCookie();
        _logger.LogInformation("User logged out, token revoked: {Jti}", SecurityLog.Fingerprint(jti));
        userId ??= "unknown";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(userId, "logout", "usuarios_web", userId, ip);
        return Ok(new { message = "Sesión cerrada correctamente" });
    }

    // ── Logout All ───────────────────────────────────────────
    // POST /api/Auth/logout-todo [NUEVO]

    [HttpPost("logout-todo")]
    [Authorize]
    public async Task<IActionResult> LogoutAll([FromBody] LogoutAllRequest? request = null)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (userId == null || role == null) return Unauthorized();

        var targetId = request?.UsuarioObjetivoId;
        _logger.LogInformation("Logout all attempt by user: {UserId} (target: {TargetId})", userId, targetId ?? userId);

        var (success, revokedCount) = await _authService.LogoutAllAsync(userId, role, targetId);
        if (!success)
        {
            _logger.LogWarning("Logout all failed - user {UserId} not authorized to revoke {TargetId}", userId, targetId);
            return Forbid();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(userId, "logout_todo", "refresh_tokens", targetId ?? userId, ip);
        _logger.LogInformation("All sessions revoked for user: {TargetId} (by: {UserId})", targetId ?? userId, userId);
        return Ok(new { message = "Sesiones revocadas", sesionesRevocadas = revokedCount });
    }
}
