using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace BioGuard.Api.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendVerificationCodeAsync(string toEmail, string nombre, string code)
    {
        var subject = "BioGuard - Verifica tu correo electrónico";
        var body = $"""
            <div style="background-color: #0b0f19; padding: 40px 20px; font-family: Arial, sans-serif; text-align: center;">
                <div style="max-width: 500px; margin: 0 auto; background-color: #131926; border: 1.5px solid #202b3d; border-radius: 16px; padding: 32px; text-align: left; box-shadow: 0 8px 30px rgba(0,0,0,0.3);">
                    <div style="text-align: center; margin-bottom: 24px;">
                        <div style="font-size: 24px; font-weight: 800; color: #00E676; letter-spacing: 2px;">BIOGUARD</div>
                        <div style="font-size: 10px; color: #6b7d99; letter-spacing: 1px; margin-top: 4px;">MONITOREO METABÓLICO INTELIGENTE</div>
                    </div>
                    <div style="color: #e2e8f0; font-size: 15px; line-height: 1.6;">
                        <p style="margin-top: 0;">Hola <strong style="color: #ffffff;">{nombre}</strong>,</p>
                        <p>Gracias por registrarte en BioGuard. Para completar tu registro y activar tu cuenta, utiliza el siguiente código de verificación de un solo uso:</p>
                    </div>
                    <div style="background: linear-gradient(135deg, #182235 0%, #1c2a42 100%); border: 1px solid #283a54; padding: 24px; text-align: center; border-radius: 12px; margin: 28px 0;">
                        <span style="font-size: 36px; font-weight: 800; letter-spacing: 10px; color: #00E676; font-family: monospace; padding-left: 10px;">{code}</span>
                    </div>
                    <div style="color: #94a3b8; font-size: 13px; line-height: 1.5;">
                        <p style="margin-bottom: 4px;">⚠️ Este código es válido por <strong>10 minutos</strong>. Por tu seguridad, no compartas este código con nadie.</p>
                        <p style="margin-top: 0;">Si no solicitaste este registro, puedes ignorar este correo de forma segura.</p>
                    </div>
                    <hr style="border: none; border-top: 1px solid #202b3d; margin: 28px 0;">
                    <div style="text-align: center; color: #64748b; font-size: 11px; line-height: 1.4;">
                        <p style="margin: 0;">Este es un correo automático, por favor no respondas a esta dirección.</p>
                        <p style="margin: 4px 0 0 0;">&copy; {DateTime.UtcNow.Year} BioGuard. Todos los derechos reservados.</p>
                    </div>
                </div>
            </div>
            """;
        return await SendEmailAsync(toEmail, subject, body);
    }

    public async Task<bool> SendPasswordResetAsync(string toEmail, string nombre, string resetLink)
    {
        var subject = "BioGuard - Recupera tu contraseña";
        var body = $"""
            <div style="background-color: #0b0f19; padding: 40px 20px; font-family: Arial, sans-serif; text-align: center;">
                <div style="max-width: 500px; margin: 0 auto; background-color: #131926; border: 1.5px solid #202b3d; border-radius: 16px; padding: 32px; text-align: left; box-shadow: 0 8px 30px rgba(0,0,0,0.3);">
                    <div style="text-align: center; margin-bottom: 24px;">
                        <div style="font-size: 24px; font-weight: 800; color: #00E676; letter-spacing: 2px;">BIOGUARD</div>
                        <div style="font-size: 10px; color: #6b7d99; letter-spacing: 1px; margin-top: 4px;">MONITOREO METABÓLICO INTELIGENTE</div>
                    </div>
                    <div style="color: #e2e8f0; font-size: 15px; line-height: 1.6;">
                        <p style="margin-top: 0;">Hola <strong style="color: #ffffff;">{nombre}</strong>,</p>
                        <p>Recibimos una solicitud para restablecer la contraseña de tu cuenta de BioGuard. Haz clic en el botón de abajo para configurar una nueva contraseña:</p>
                    </div>
                    <div style="text-align: center; margin: 28px 0;">
                        <a href="{resetLink}" style="display: inline-block; background-color: #00E676; color: #0b0f19; font-weight: bold; font-size: 14px; padding: 12px 28px; text-decoration: none; border-radius: 8px;">Restablecer Contraseña</a>
                    </div>
                    <div style="color: #94a3b8; font-size: 13px; line-height: 1.5;">
                        <p style="margin-bottom: 4px;">⚠️ Este enlace es válido por <strong>1 hora</strong>. Si el botón no funciona, copia y pega este enlace en tu navegador:</p>
                        <p style="word-break: break-all; color: #00E676; font-size: 11px;">{resetLink}</p>
                        <p style="margin-top: 8px;">Si no solicitaste esto, puedes ignorar este correo de forma segura.</p>
                    </div>
                    <hr style="border: none; border-top: 1px solid #202b3d; margin: 28px 0;">
                    <div style="text-align: center; color: #64748b; font-size: 11px; line-height: 1.4;">
                        <p style="margin: 0;">Este es un correo automático, por favor no respondas a esta dirección.</p>
                        <p style="margin: 4px 0 0 0;">&copy; {DateTime.UtcNow.Year} BioGuard. Todos los derechos reservados.</p>
                    </div>
                </div>
            </div>
            """;
        return await SendEmailAsync(toEmail, subject, body);
    }

    public async Task<bool> SendPasswordChangedNotificationAsync(string toEmail, string nombre)
    {
        var subject = "BioGuard - Contraseña actualizada";
        var body = $"""
            <div style="background-color: #0b0f19; padding: 40px 20px; font-family: Arial, sans-serif; text-align: center;">
                <div style="max-width: 500px; margin: 0 auto; background-color: #131926; border: 1.5px solid #202b3d; border-radius: 16px; padding: 32px; text-align: left; box-shadow: 0 8px 30px rgba(0,0,0,0.3);">
                    <div style="text-align: center; margin-bottom: 24px;">
                        <div style="font-size: 24px; font-weight: 800; color: #00E676; letter-spacing: 2px;">BIOGUARD</div>
                        <div style="font-size: 10px; color: #6b7d99; letter-spacing: 1px; margin-top: 4px;">MONITOREO METABÓLICO INTELIGENTE</div>
                    </div>
                    <div style="color: #e2e8f0; font-size: 15px; line-height: 1.6;">
                        <p style="margin-top: 0;">Hola <strong style="color: #ffffff;">{nombre}</strong>,</p>
                        <p>Te notificamos que la contraseña de tu cuenta de BioGuard ha sido <strong>actualizada con éxito</strong>.</p>
                    </div>
                    <div style="text-align: center; margin: 28px 0;">
                        <div style="display: inline-block; width: 64px; height: 64px; line-height: 64px; border-radius: 32px; background-color: rgba(0, 230, 118, 0.1); border: 2px solid #00E676; color: #00E676; font-size: 32px; font-weight: bold;">✓</div>
                    </div>
                    <div style="color: #f87171; background-color: rgba(239, 68, 68, 0.1); border: 1px solid rgba(239, 68, 68, 0.2); padding: 16px; border-radius: 8px; font-size: 13px; line-height: 1.5;">
                        <strong>¿No fuiste tú?</strong> Si no realizaste este cambio, por favor ponte en contacto con nuestro equipo de soporte técnico inmediatamente.
                    </div>
                    <hr style="border: none; border-top: 1px solid #202b3d; margin: 28px 0;">
                    <div style="text-align: center; color: #64748b; font-size: 11px; line-height: 1.4;">
                        <p style="margin: 0;">Este es un correo automático, por favor no respondas a esta dirección.</p>
                        <p style="margin: 4px 0 0 0;">&copy; {DateTime.UtcNow.Year} BioGuard. Todos los derechos reservados.</p>
                    </div>
                </div>
            </div>
            """;
        return await SendEmailAsync(toEmail, subject, body);
    }

    private async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        try
        {
            var host = FallbackIfEmpty(_config["Smtp:Host"], Environment.GetEnvironmentVariable("SMTP_HOST"));
            var portStr = FallbackIfEmpty(_config["Smtp:Port"], Environment.GetEnvironmentVariable("SMTP_PORT"));
            var user = FallbackIfEmpty(_config["Smtp:User"], Environment.GetEnvironmentVariable("SMTP_USER"));
            var pass = FallbackIfEmpty(_config["Smtp:Password"], Environment.GetEnvironmentVariable("SMTP_PASSWORD"));
            var from = FallbackIfEmpty(_config["Smtp:From"], Environment.GetEnvironmentVariable("SMTP_FROM"));

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                _logger.LogWarning("SMTP not configured - email to {Email} skipped. Subject: {Subject}", toEmail, subject);
                return false;
            }

            var port = int.TryParse(portStr, out var p) ? p : 587;

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from ?? user));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(user, pass);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {Email}, subject: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }

    private static string? FallbackIfEmpty(string? value, string? fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
