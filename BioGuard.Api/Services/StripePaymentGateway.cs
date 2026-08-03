using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using PlanModel = BioGuard.Api.Models.Plan;

namespace BioGuard.Api.Services;

public class StripePaymentGateway : IPaymentGateway
{
    private readonly ILogger<StripePaymentGateway> _logger;
    private readonly StripeOptions _options;

    public StripePaymentGateway(IOptions<StripeOptions> options, ILogger<StripePaymentGateway> logger, IConfiguration config)
    {
        _options = options.Value;
        _logger = logger;
        var envKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        if (!string.IsNullOrEmpty(envKey))
            _options.SecretKey = envKey;
        var envWebhook = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
        if (!string.IsNullOrEmpty(envWebhook))
            _options.WebhookSecret = envWebhook;
        StripeConfiguration.ApiKey = _options.SecretKey;
        if (string.IsNullOrEmpty(_options.SecretKey))
            _logger.LogWarning("Stripe SecretKey is not configured. Payment sessions will fail.");
        if (string.IsNullOrEmpty(_options.WebhookSecret))
            _logger.LogWarning("Stripe WebhookSecret is not configured. Webhook signature verification will fail.");
    }

    public async Task<PaymentSessionResult> CreateCheckoutSessionAsync(string usuarioId, PlanModel plan, string successUrl, string cancelUrl)
    {
        try
        {
            var options = new SessionCreateOptions
            {
                CustomerEmail = null,
                ClientReferenceId = usuarioId,
                Metadata = new Dictionary<string, string>
                {
                    { "usuario_id", usuarioId },
                    { "plan_id", plan.Id },
                    { "plan_nombre", plan.Nombre }
                },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = plan.PrecioMoneda.ToLower(),
                            UnitAmountDecimal = plan.Precio * 100,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"BioGuard - Plan {plan.Nombre}",
                                Description = plan.Descripcion
                            },
                            Recurring = plan.EsSuscripcion
                                ? new SessionLineItemPriceDataRecurringOptions { Interval = "month" }
                                : null
                        },
                        Quantity = 1
                    }
                },
                Mode = plan.EsSuscripcion ? "subscription" : "payment",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            _logger.LogInformation("Stripe checkout session created: {SessionId}", session.Id);
            return new PaymentSessionResult(true, session.Id, session.Url, session.SubscriptionId, null);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating checkout session");
            return new PaymentSessionResult(false, null, null, null, ex.Message);
        }
    }

    public async Task<bool> VerifyWebhookSignatureAsync(string payload, IReadOnlyDictionary<string, string> headers)
    {
        try
        {
            var signature = headers.GetValueOrDefault("Stripe-Signature", "");
            EventUtility.ConstructEvent(payload, signature, _options.WebhookSecret);
            return true;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return false;
        }
    }

    public async Task<PaymentWebhookEvent> ParseWebhookEventAsync(string payload, IReadOnlyDictionary<string, string> headers)
    {
        try
        {
            var signature = headers.GetValueOrDefault("Stripe-Signature", "");
            var stripeEvent = EventUtility.ConstructEvent(payload, signature, _options.WebhookSecret);

            string? sessionId = null, subscriptionId = null, customerId = null, planId = null;
            if (stripeEvent.Data.Object is Session session)
            {
                sessionId = session.Id;
                subscriptionId = session.SubscriptionId;
                customerId = session.CustomerId;
                planId = session.Metadata?.GetValueOrDefault("plan_id");
            }
            else if (stripeEvent.Data.Object is Invoice invoice)
            {
                // invoice.paid / renovaciones: el objeto es Invoice, no Session
                subscriptionId = invoice.SubscriptionId;
                customerId = invoice.CustomerId;
                sessionId = invoice.Id;
            }

            return new PaymentWebhookEvent(
                EventId: stripeEvent.Id,
                Type: stripeEvent.Type,
                SessionId: sessionId,
                SubscriptionId: subscriptionId,
                CustomerId: customerId,
                Status: stripeEvent.Type switch
                {
                    "checkout.session.completed" => "completado",
                    "checkout.session.expired" => "expirado",
                    "invoice.paid" => "renovado",
                    "customer.subscription.deleted" => "cancelado",
                    _ => "desconocido"
                },
                PlanId: planId
            );
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error parsing Stripe webhook event");
            return new PaymentWebhookEvent("", "", null, null, null, "error", null);
        }
    }

    public async Task<bool> CancelSubscriptionAsync(string subscriptionId)
    {
        try
        {
            var service = new SubscriptionService();
            await service.CancelAsync(subscriptionId);
            _logger.LogInformation("Stripe subscription cancelled: {SubscriptionId}", subscriptionId);
            return true;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error cancelling Stripe subscription");
            return false;
        }
    }
}
