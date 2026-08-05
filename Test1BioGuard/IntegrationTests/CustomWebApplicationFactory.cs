using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;
using BioGuard.Api.Services;
using QuestPDF.Infrastructure;

namespace Test1BioGuard.IntegrationTests.TestGateways
{
    public class StripePaymentGateway : IPaymentGateway
    {
        public Task<PaymentSessionResult> CreateCheckoutSessionAsync(
            string usuarioId, Plan plan, string successUrl, string cancelUrl)
            => Task.FromResult(new PaymentSessionResult(true, "cs_test_123", "https://checkout.stripe.com/test", null, null));

        public Task<bool> VerifyWebhookSignatureAsync(string payload, IReadOnlyDictionary<string, string> headers)
            => Task.FromResult(true);

        public Task<PaymentWebhookEvent> ParseWebhookEventAsync(string payload, IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null || !headers.ContainsKey("Stripe-Signature") || string.IsNullOrEmpty(headers["Stripe-Signature"]))
                return Task.FromResult(new PaymentWebhookEvent("", "", null, null, null, "", null));
            return Task.FromResult(new PaymentWebhookEvent("evt_test", "checkout.session.completed", "cs_test_123", null, "cus_test", "complete", null));
        }

        public Task<bool> CancelSubscriptionAsync(string subscriptionId)
            => Task.FromResult(true);
    }

    public class PayPalPaymentGateway : IPaymentGateway
    {
        public Task<PaymentSessionResult> CreateCheckoutSessionAsync(
            string usuarioId, Plan plan, string successUrl, string cancelUrl)
            => Task.FromResult(new PaymentSessionResult(true, "paypal_test_123", "https://paypal.com/checkout/test", null, null));

        public Task<bool> VerifyWebhookSignatureAsync(string payload, IReadOnlyDictionary<string, string> headers)
            => Task.FromResult(true);

        public Task<PaymentWebhookEvent> ParseWebhookEventAsync(string payload, IReadOnlyDictionary<string, string> headers)
            => Task.FromResult(new PaymentWebhookEvent("evt_test", "CHECKOUT.ORDER.APPROVED", "paypal_test_123", null, "cus_test", "COMPLETED", null));

        public Task<bool> CancelSubscriptionAsync(string subscriptionId)
            => Task.FromResult(true);
    }
}

namespace Test1BioGuard.IntegrationTests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<BioGuard.Api.Program>
    {
        public Mock<IMongoDbContext> MockDbContext { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("MONGODB_CONNECTION_STRING", "mongodb://localhost:27017");
            Environment.SetEnvironmentVariable("JWT_SECRET_KEY", "BioGuard-Test-Secret-Key-Only-For-Unit-Tests-0123456789");
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", "sk_test_mock");
            Environment.SetEnvironmentVariable("PAYPAL_CLIENT_ID", "mock_client_id");
            Environment.SetEnvironmentVariable("PAYPAL_CLIENT_SECRET", "mock_client_secret");

builder.UseEnvironment("Testing");

        QuestPDF.Settings.License = LicenseType.Community;

        builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IMongoDbContext));
                if (descriptor != null) services.Remove(descriptor);

                var configDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(MongoDbConfig));
                if (configDescriptor != null) services.Remove(configDescriptor);

                MockDbContext.Setup(db => db.Planes).Returns(new Mock<IMongoCollection<Plan>>().Object);
                MockDbContext.Setup(db => db.UsuariosWeb).Returns(new Mock<IMongoCollection<UsuarioWeb>>().Object);
                MockDbContext.Setup(db => db.Pacientes).Returns(new Mock<IMongoCollection<Paciente>>().Object);
                MockDbContext.Setup(db => db.Cuidadores).Returns(new Mock<IMongoCollection<Cuidador>>().Object);
                MockDbContext.Setup(db => db.Dispositivos).Returns(new Mock<IMongoCollection<Dispositivo>>().Object);
                MockDbContext.Setup(db => db.LecturasSensores).Returns(new Mock<IMongoCollection<LecturaSensor>>().Object);
                MockDbContext.Setup(db => db.EventosMetabolicos).Returns(new Mock<IMongoCollection<EventoMetabolico>>().Object);
                MockDbContext.Setup(db => db.TrackingGps).Returns(new Mock<IMongoCollection<TrackingGps>>().Object);
                MockDbContext.Setup(db => db.Notificaciones).Returns(new Mock<IMongoCollection<Notificacion>>().Object);
                MockDbContext.Setup(db => db.Auditoria).Returns(new Mock<IMongoCollection<Auditoria>>().Object);
                MockDbContext.Setup(db => db.Pagos).Returns(new Mock<IMongoCollection<Pago>>().Object);
                MockDbContext.Setup(db => db.PrediccionesMl).Returns(new Mock<IMongoCollection<PrediccionMl>>().Object);
                MockDbContext.Setup(db => db.ModelosMl).Returns(new Mock<IMongoCollection<ModeloMl>>().Object);
                MockDbContext.Setup(db => db.FcmTokens).Returns(new Mock<IMongoCollection<FcmToken>>().Object);
                MockDbContext.Setup(db => db.RefreshTokens).Returns(new Mock<IMongoCollection<RefreshToken>>().Object);
                MockDbContext.Setup(db => db.Medicamentos).Returns(new Mock<IMongoCollection<Medicamento>>().Object);
                MockDbContext.Setup(db => db.Alertas).Returns(new Mock<IMongoCollection<Alerta>>().Object);
                MockDbContext.Setup(db => db.ReportesCompartidos).Returns(new Mock<IMongoCollection<ReporteCompartido>>().Object);
                MockDbContext.Setup(db => db.EventosProcesados).Returns(new Mock<IMongoCollection<EventoProcesado>>().Object);
                MockDbContext.Setup(db => db.TokenBlacklist).Returns(new Mock<IMongoCollection<TokenBlacklist>>().Object);
                MockDbContext.Setup(db => db.TicketsSoporte).Returns(new Mock<IMongoCollection<TicketSoporte>>().Object);

                var defaultPaciente = new Paciente
                {
                    Id = "123456789012345678901234",
                    UsuarioWebId = "user123",
                    Nombre = "Paciente Test",
                    CodigoAccesoQr = "TESTQR123"
                };

                var defaultUsuario = new UsuarioWeb
                {
                    Id = "user123",
                    Nombre = "Dueno Test",
                    Correo = "dueno@test.com",
                    PlanId = "plan_premium"
                };

                var defaultPlan = new Plan
                {
                    Id = "plan_premium",
                    Nombre = "Premium",
                    Precio = 199m,
                    PrecioMoneda = "MXN",
                    LimiteCuidadores = 3,
                    LimitePacientes = 5,
                    DiasHistorial = 365,
                    GpsContinuo = true,
                    AiConsole = true
                };

                MockDbContext.Setup(db => db.FindFirstOrDefaultAsync(
                        It.IsAny<IMongoCollection<Paciente>>(),
                        It.IsAny<System.Linq.Expressions.Expression<Func<Paciente, bool>>>()))
                    .ReturnsAsync(defaultPaciente);

                MockDbContext.Setup(db => db.FindFirstOrDefaultAsync(
                        It.IsAny<IMongoCollection<UsuarioWeb>>(),
                        It.IsAny<System.Linq.Expressions.Expression<Func<UsuarioWeb, bool>>>()))
                    .ReturnsAsync(defaultUsuario);

                MockDbContext.Setup(db => db.FindFirstOrDefaultAsync(
                        It.IsAny<IMongoCollection<Plan>>(),
                        It.IsAny<System.Linq.Expressions.Expression<Func<Plan, bool>>>()))
                    .ReturnsAsync(defaultPlan);

                MockDbContext.Setup(db => db.FindFirstOrDefaultAsync(
                        It.IsAny<IMongoCollection<Dispositivo>>(),
                        It.IsAny<System.Linq.Expressions.Expression<Func<Dispositivo, bool>>>()))
                    .ReturnsAsync((Dispositivo?)null);

                services.AddSingleton(MockDbContext.Object);
                services.AddSingleton(new MongoDbConfig
                {
                    ConnectionString = "mongodb://localhost:27017",
                    DatabaseName = "bioguard_test"
                });

                var mockEmailService = new Mock<IEmailService>();
                mockEmailService.Setup(s => s.SendVerificationCodeAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(true);
                mockEmailService.Setup(s => s.SendPasswordResetAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(true);
                services.AddSingleton(mockEmailService.Object);

var mockImageStorage = new Mock<IImageStorageService>();
            mockImageStorage.Setup(s => s.UploadAsync(
                    It.Is<string>(b => b == "base64fotodata"), It.IsAny<string?>()))
                .ReturnsAsync(new ImageUploadResult(true, "https://img.test/photo.jpg", null));
            mockImageStorage.Setup(s => s.UploadAsync(
                    It.Is<string>(b => b != "base64fotodata"), It.IsAny<string?>()))
                .ReturnsAsync(new ImageUploadResult(false, null, "Formato o tamaÃ±o invÃ¡lido"));
            services.AddSingleton(mockImageStorage.Object);

                var mockPlanLimite = new Mock<IPlanLimiteService>();
                mockPlanLimite.Setup(s => s.VerificarLimiteCuidadoresAsync(
                        It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(new PlanLimiteResult(true));
                mockPlanLimite.Setup(s => s.VerificarDiasHistorialAsync(
                        It.IsAny<string>(), It.IsAny<int>()))
                    .ReturnsAsync(new PlanLimiteResult(true));
                mockPlanLimite.Setup(s => s.VerificarGpsContinuoAsync(
                        It.IsAny<string>()))
                    .ReturnsAsync(new PlanLimiteResult(true));
                mockPlanLimite.Setup(s => s.VerificarAiConsoleAsync(
                        It.IsAny<string>()))
                    .ReturnsAsync(new PlanLimiteResult(true));
                mockPlanLimite.Setup(s => s.VerificarExportacionReportesAsync(
                        It.IsAny<string>()))
                    .ReturnsAsync(new PlanLimiteResult(true));
                mockPlanLimite.Setup(s => s.VerificarGuardianNocturnoAsync(
                        It.IsAny<string>()))
                    .ReturnsAsync(new PlanLimiteResult(true));
                services.AddSingleton(mockPlanLimite.Object);

                var paymentGateways = services.Where(d => d.ServiceType == typeof(IPaymentGateway)).ToList();
                foreach (var g in paymentGateways) services.Remove(g);

                services.AddSingleton<IPaymentGateway>(new TestGateways.StripePaymentGateway());
                services.AddSingleton<IPaymentGateway>(new TestGateways.PayPalPaymentGateway());

                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.MapInboundClaims = false;
                });
            });
        }
    }
}