using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.Services;
using BioGuard.Api.DTOs;

namespace BioGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = SystemRoles.Dueno)]
public class PagosController : ControllerBase
{
    private readonly PagosService _pagosService;
    private readonly IMongoDbContext _db;
    private readonly AuditoriaService _auditoriaService;
    private readonly IPaymentGateway _stripeGateway;
    private readonly IPaymentGateway _paypalGateway;
    private readonly ILogger<PagosController> _logger;

    public PagosController(PagosService pagosService, IMongoDbContext db,
        IEnumerable<IPaymentGateway> paymentGateways, AuditoriaService auditoriaService,
        ILogger<PagosController> logger)
    {
        _pagosService = pagosService;
        _db = db;
        _auditoriaService = auditoriaService;
        _logger = logger;
        var gateways = paymentGateways.ToList();
        _stripeGateway = gateways.FirstOrDefault(g => g.GetType().Name == "StripePaymentGateway")!;
        _paypalGateway = gateways.FirstOrDefault(g => g.GetType().Name == "PayPalPaymentGateway")!;
    }

    private string? GetUsuarioId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private IPaymentGateway? ResolveGateway(string procesador) => procesador?.ToLower() switch
    {
        "stripe" => _stripeGateway,
        "paypal" => _paypalGateway,
        _ => null
    };

    // POST /api/Pagos/crear-sesion [WEB]

    [HttpPost("crear-sesion")]
    public async Task<IActionResult> CrearSesion([FromBody] CrearSesionPagoRequest request)
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        var gateway = ResolveGateway(request.Procesador);
        if (gateway == null)
            return BadRequest(new { message = "Procesador no válido. Use 'stripe' o 'paypal'" });

        _logger.LogInformation("Creating payment session for user {UsuarioId}, plan {PlanNombre}, procesador {Procesador}", usuarioId, request.PlanNombre, request.Procesador);

        var aliases = PlanCatalog.Aliases(request.PlanNombre);
        var plan = await _db.FindFirstOrDefaultAsync(
            _db.Planes, p => aliases.Contains(p.Nombre) && p.Activo);
        if (plan == null)
        {
            _logger.LogWarning("Invalid plan {PlanNombre} for payment session by user {UsuarioId}", request.PlanNombre, usuarioId);
            return BadRequest(new { message = "Plan no válido" });
        }

        var allowedHosts = new[] { "bioguard.app", "localhost", "127.0.0.1" };
        var host = Request.Host.Host;
        if (!allowedHosts.Any(h => host.Contains(h, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Blocked payment session creation with unexpected Host header: {Host}", Request.Host);
            return BadRequest(new { message = "Host no permitido" });
        }
        var successUrl = $"{Request.Scheme}://{Request.Host}/api/pagos/success-pago";
        var cancelUrl = $"{Request.Scheme}://{Request.Host}/api/pagos/cancel-pago";

        PaymentSessionResult gatewayResult;
        try
        {
            gatewayResult = await gateway.CreateCheckoutSessionAsync(usuarioId, plan, successUrl, cancelUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway error creating checkout session for plan {PlanNombre}", request.PlanNombre);
            return StatusCode(502, new { message = "Error del procesador de pago" });
        }

        if (!gatewayResult.Success)
        {
            _logger.LogWarning("Gateway returned failure for plan {PlanNombre}: {Error}", request.PlanNombre, gatewayResult.Error);
            return StatusCode(502, new { message = gatewayResult.Error ?? "Error del procesador de pago" });
        }

        var pago = await _pagosService.CrearSesionAsync(usuarioId, plan, gatewayResult, request.Procesador);
        if (pago == null)
            return BadRequest(new { message = "Plan no válido" });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "crear_sesion_pago", "pagos", pago.Id, ip);

        return Ok(new
        {
            pagoId = pago.Id,
            sesionUrl = pago.SesionUrl,
            monto = pago.Monto,
            moneda = pago.Moneda,
            message = "Sesión de pago creada"
        });
    }

    // GET /api/Pagos/historial [WEB]

    [HttpGet("historial")]
    public async Task<IActionResult> Historial()
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Getting payment history for user {UsuarioId}", usuarioId);
        var pagos = await _pagosService.ObtenerHistorialAsync(usuarioId);
        var response = pagos.Select(p => new PagoResponse(
            p.Id, p.Monto, p.Moneda, p.Estado, p.FechaPago, p.MetodoPago));
        return Ok(response);
    }

    // GET /api/Pagos/{id}/recibo [WEB]

    [HttpGet("{id}/recibo")]
    public async Task<IActionResult> Recibo(string id)
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Getting receipt for payment {Id}", id);
        var pago = await _pagosService.ObtenerPorIdAsync(id);
        if (pago == null)
        {
            _logger.LogWarning("Payment {Id} not found when getting receipt", id);
            return NotFound();
        }

        if (pago.UsuarioWebId != usuarioId)
        {
            _logger.LogWarning("User {UsuarioId} attempted to access receipt of payment {Id} owned by {OwnerId}", usuarioId, id, pago.UsuarioWebId);
            return Forbid();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "ver_recibo", "pagos", id, ip);

        return Ok(new
        {
            pagoId = pago.Id,
            monto = pago.Monto,
            moneda = pago.Moneda,
            estado = pago.Estado,
            fechaPago = pago.FechaPago,
            descargaUrl = $"/api/pagos/{pago.Id}/recibo/descarga"
        });
    }

    // GET /api/Pagos/{id}/recibo/descarga [WEB]

    [HttpGet("{id}/recibo/descarga")]
    public async Task<IActionResult> DescargarRecibo(string id)
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Downloading receipt for payment {Id}", id);
        var pago = await _pagosService.ObtenerPorIdAsync(id);
        if (pago == null) return NotFound();

        if (pago.UsuarioWebId != usuarioId) return Forbid();

        var plan = await _db.FindFirstOrDefaultAsync(_db.Planes, p => p.Id == pago.PlanId);
        var planNombre = plan?.Nombre ?? "Plan BioGuard";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine("               RECIBO DE PAGO BIOGUARD            ");
        sb.AppendLine("==================================================");
        sb.AppendLine($"ID de Transacción: {pago.Id}");
        sb.AppendLine($"Fecha de Transacción: {pago.FechaPago:dd/MM/yyyy HH:mm:ss} UTC");
        sb.AppendLine($"Método de Pago: {pago.MetodoPago?.ToUpper()}");
        sb.AppendLine($"Estado: {pago.Estado?.ToUpper()}");
        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine($"Concepto: Suscripción - {planNombre}");
        sb.AppendLine($"Monto: {pago.Monto} {pago.Moneda}");
        sb.AppendLine("==================================================");
        sb.AppendLine("       Gracias por confiar en BioGuard.           ");
        sb.AppendLine("==================================================");

        var fileBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "descargar_recibo", "pagos", id, ip);

        return File(fileBytes, "text/plain", $"recibo_{pago.Id}.txt");
    }

    // POST /api/Pagos/cancelar [WEB]

    [HttpPost("cancelar")]
    public async Task<IActionResult> Cancelar()
    {
        var usuarioId = GetUsuarioId();
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        _logger.LogInformation("Cancelling subscription for user {UsuarioId}", usuarioId);

        var result = await _pagosService.CancelarSuscripcionPorUsuarioAsync(usuarioId, _stripeGateway!, _paypalGateway!);
        if (!result)
        {
            _logger.LogWarning("No active subscription to cancel for user {UsuarioId}", usuarioId);
            return BadRequest(new { message = "No hay suscripción activa" });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _auditoriaService.RegistrarAsync(usuarioId, "cancelar_suscripcion", "pagos", usuarioId, ip);
        return Ok(new { message = "Suscripción cancelada" });
    }

    // ── Redirecciones pago ───────────────────────────────────

    [AllowAnonymous]
    [HttpGet("success-pago")]
    public IActionResult SuccessPago()
    {
        return Ok(new { message = "Pago completado exitosamente. Redirigiendo..." });
    }

    [AllowAnonymous]
    [HttpGet("cancel-pago")]
    public IActionResult CancelPago()
    {
        return Ok(new { message = "Pago cancelado." });
    }

    // ── Webhooks (sin autenticación JWT) ──────────────────────

    [AllowAnonymous]
    [HttpPost("webhook/stripe")]
    public async Task<IActionResult> WebhookStripe()
    {
        try
        {
            if (Request.ContentType == null || !Request.ContentType.StartsWith("application/json"))
            {
                _logger.LogWarning("Invalid Content-Type for Stripe webhook: {ContentType}", Request.ContentType);
                return BadRequest(new { error = "Content-Type must be application/json" });
            }

            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var headers = new Dictionary<string, string>
            {
                ["Stripe-Signature"] = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? ""
            };

            if (_stripeGateway == null)
                return StatusCode(502, new { error = "Stripe no configurado" });

            var webhookEvent = await _stripeGateway.ParseWebhookEventAsync(payload, headers);
            if (string.IsNullOrEmpty(webhookEvent.EventId))
            {
                _logger.LogWarning("Stripe webhook signature verification failed");
                return BadRequest(new { received = false, error = "Firma inválida" });
            }

            // Guardar event.id ANTES de procesar para evitar race condition
            var eventIdGuardado = await _pagosService.RegistrarEventoIdSiNoExisteAsync(webhookEvent.EventId);
            if (!eventIdGuardado)
            {
                _logger.LogInformation("Stripe webhook event {EventId} already processed, skipping", webhookEvent.EventId);
                return Ok(new { received = true });
            }

            switch (webhookEvent.Type)
            {
                case "checkout.session.completed":
                case "invoice.paid":
                    if (webhookEvent.SessionId != null)
                    {
                        var completado = await _pagosService.ActualizarPagoCompletadoAsync(
                            webhookEvent.SessionId, webhookEvent.CustomerId, webhookEvent.SubscriptionId);
                        if (completado && webhookEvent.PlanId != null)
                        {
                            var (pago, usuario) = await _pagosService.ObtenerPagoYUsuarioPorSessionIdAsync(webhookEvent.SessionId);
                            if (pago != null && usuario != null)
                            {
                                await _pagosService.ActualizarPlanUsuarioAsync(usuario.Id, webhookEvent.PlanId);
                            }
                        }
                    }
                    break;

                case "customer.subscription.deleted":
                    await HandleSubscriptionCancelledAsync(webhookEvent.SubscriptionId, webhookEvent.CustomerId);
                    break;
            }

            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe webhook error");
            return StatusCode(500, new { error = "Webhook processing failed" });
        }
    }

    [AllowAnonymous]
    [HttpPost("webhook/paypal")]
    public async Task<IActionResult> WebhookPayPal()
    {
        // PayPal temporalmente deshabilitado: no se verifica firma de webhook.
        // Rechazar cualquier evento hasta implementar verificación real.
        _logger.LogWarning("PayPal webhook received but processor is temporarily disabled");
        await Task.CompletedTask;
        return StatusCode(501, new { received = false, error = "PayPal temporalmente no disponible" });

#pragma warning disable CS0162 // Unreachable code (PayPal deshabilitado)
        try
        {
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var headers = new Dictionary<string, string>
            {
                ["Paypal-Auth-Algo"] = Request.Headers["Paypal-Auth-Algo"].FirstOrDefault() ?? "",
                ["Paypal-Cert-Url"] = Request.Headers["Paypal-Cert-Url"].FirstOrDefault() ?? "",
                ["Paypal-Transmission-Id"] = Request.Headers["Paypal-Transmission-Id"].FirstOrDefault() ?? "",
                ["Paypal-Transmission-Sig"] = Request.Headers["Paypal-Transmission-Sig"].FirstOrDefault() ?? "",
                ["Paypal-Transmission-Time"] = Request.Headers["Paypal-Transmission-Time"].FirstOrDefault() ?? ""
            };

            if (_paypalGateway == null)
                return StatusCode(502, new { error = "PayPal no configurado" });

            var webhookEvent = await _paypalGateway.ParseWebhookEventAsync(payload, headers);
            _logger.LogInformation("PayPal webhook received: {Type}, EventId: {EventId}", webhookEvent.Type, webhookEvent.EventId);

            if (string.IsNullOrEmpty(webhookEvent.EventId))
            {
                _logger.LogWarning("PayPal webhook verification failed");
                return BadRequest(new { received = false, error = "Verificación fallida" });
            }

            // Guardar event.id ANTES de procesar para evitar race condition
            var eventIdGuardado = await _pagosService.RegistrarEventoIdSiNoExisteAsync(webhookEvent.EventId);
            if (!eventIdGuardado)
            {
                _logger.LogInformation("PayPal webhook event {EventId} already processed, skipping", webhookEvent.EventId);
                return Ok(new { received = true });
            }

            // Solo PAYMENT.CAPTURE.COMPLETED activa el plan, no ORDER.APPROVED
            if (webhookEvent.Type == "PAYMENT.CAPTURE.COMPLETED" && webhookEvent.SessionId != null)
            {
                var completado = await _pagosService.ActualizarPagoPayPalAsync(webhookEvent.SessionId);
                if (completado && webhookEvent.PlanId != null)
                {
                    var (pago, usuario) = await _pagosService.ObtenerPagoYUsuarioPorPayPalOrderIdAsync(webhookEvent.SessionId);
                    if (pago != null && usuario != null)
                    {
                        await _pagosService.ActualizarPlanUsuarioAsync(usuario.Id, webhookEvent.PlanId);
                    }
                }
            }
            else if (webhookEvent.Type == "BILLING.SUBSCRIPTION.CANCELLED" && webhookEvent.SubscriptionId != null)
            {
                _logger.LogInformation("PayPal subscription cancelled: {SubscriptionId}", webhookEvent.SubscriptionId);
                await HandleSubscriptionCancelledAsync(webhookEvent.SubscriptionId, null);
            }
            else if (webhookEvent.Type == "BILLING.SUBSCRIPTION.SUSPENDED" && webhookEvent.SubscriptionId != null)
            {
                _logger.LogInformation("PayPal subscription suspended: {SubscriptionId}", webhookEvent.SubscriptionId);
                await HandleSubscriptionCancelledAsync(webhookEvent.SubscriptionId, null);
            }
            else if (webhookEvent.Type == "BILLING.SUBSCRIPTION.ACTIVATED" && webhookEvent.SubscriptionId != null)
            {
                _logger.LogInformation("PayPal subscription activated: {SubscriptionId}", webhookEvent.SubscriptionId);
            }

            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal webhook error");
            return StatusCode(500, new { error = "Webhook processing failed" });
#pragma warning restore CS0162
        }
    }

    // ── B2: Downgrade automático ────────────────────────────

    private async Task HandleSubscriptionCancelledAsync(string? subscriptionId, string? customerId)
    {
        _logger.LogInformation("Processing subscription cancelled: {SubscriptionId}", subscriptionId);

        Pago? pago = null;
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            pago = await _db.FindFirstOrDefaultAsync(_db.Pagos, p => p.StripeSubscriptionId == subscriptionId);
            if (pago == null)
                pago = await _db.FindFirstOrDefaultAsync(_db.Pagos, p => p.PayPalOrderId == subscriptionId);
        }
        if (pago == null && !string.IsNullOrEmpty(customerId))
            pago = await _db.FindFirstOrDefaultAsync(_db.Pagos, p => p.StripeCustomerId == customerId);
        if (pago == null)
        {
            _logger.LogWarning("No se encontró pago para subscription {SubscriptionId}", subscriptionId);
            return;
        }

        var usuario = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == pago.UsuarioWebId);
        if (usuario == null)
        {
            _logger.LogWarning("Usuario no encontrado para downgrade");
            return;
        }

        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.UsuarioWebId == usuario.Id);
        var pacienteId = paciente?.Id;

        await _pagosService.DowngradeToFreeAsync(usuario.Id);
        if (!string.IsNullOrEmpty(pacienteId))
        {
            await _pagosService.RevocarTokensCuidadorAsync(pacienteId);
        }
        await _pagosService.OcultarHistorialAntiguoAsync(usuario.Id, 1); // Free retiene 1 mes

        _logger.LogInformation("Downgrade completado para usuario {UsuarioId}", usuario.Id);
    }
}
