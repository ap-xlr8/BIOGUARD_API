using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BioGuard.Api.Services;

public interface IFCMService
{
    Task<bool> EnviarNotificacionAsync(string token, string titulo, string cuerpo, Dictionary<string, string>? datos = null, bool altaPrioridad = false);
    Task<int> EnviarMulticastAsync(List<string> tokens, string titulo, string cuerpo, Dictionary<string, string>? datos = null, bool altaPrioridad = false);
    Task<bool> EnviarATopicAsync(string topic, string titulo, string cuerpo, Dictionary<string, string>? datos = null);
}

public class FCMService : IFCMService
{
    private readonly ILogger<FCMService> _logger;
    private readonly IConfiguration _config;
    private readonly bool _inicializado;

    public FCMService(IConfiguration config, ILogger<FCMService> logger)
    {
        _config = config;
        _logger = logger;
        _inicializado = InicializarFirebase();
    }

    private bool InicializarFirebase()
    {
        try
        {
            var credPath = _config["Firebase:CredentialsPath"] ?? Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_PATH");
            var projectId = _config["Firebase:ProjectId"] ?? Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID");

            if (string.IsNullOrWhiteSpace(credPath) || !File.Exists(credPath))
            {
                _logger.LogWarning("Firebase credentials not configured. FCM disabled.");
                return false;
            }

            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions
                {
#pragma warning disable CS0618
                    Credential = GoogleCredential.FromFile(credPath),
#pragma warning restore CS0618
                    ProjectId = projectId
                });
            }

            _logger.LogInformation("Firebase Admin SDK initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Firebase Admin SDK");
            return false;
        }
    }

    public async Task<bool> EnviarNotificacionAsync(string token, string titulo, string cuerpo, Dictionary<string, string>? datos = null, bool altaPrioridad = false)
    {
        if (!_inicializado || string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var mensaje = new Message
            {
                Fid = token,
                Notification = new Notification { Title = titulo, Body = cuerpo },
                Data = datos ?? new Dictionary<string, string>(),
                Android = new AndroidConfig
                {
                    Priority = altaPrioridad ? Priority.High : Priority.Normal,
                    Notification = new AndroidNotification
                    {
                        ChannelId = altaPrioridad ? "alertas_criticas" : "alertas_preventivas",
                        DefaultSound = altaPrioridad,
                        Sound = altaPrioridad ? "alarm_sound" : "default"
                    }
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps
                    {
                        Alert = new ApsAlert { Title = titulo, Body = cuerpo },
                        Sound = altaPrioridad ? "critical" : "default",
                        ContentAvailable = true,
                        Category = altaPrioridad ? "ALERTA_CRITICA" : "ALERTA_PREVENTIVA"
                    }
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendAsync(mensaje);
            _logger.LogInformation("FCM sent: {MessageId}", response);
            return true;
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogWarning("FCM error: {ErrorCode} - {Message}", ex.ErrorCode, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending FCM notification");
            return false;
        }
    }

    public async Task<int> EnviarMulticastAsync(List<string> tokens, string titulo, string cuerpo, Dictionary<string, string>? datos = null, bool altaPrioridad = false)
    {
        if (!_inicializado || tokens == null || tokens.Count == 0) return 0;

        try
        {
            var mensaje = new MulticastMessage
            {
                Fids = tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
                Notification = new Notification { Title = titulo, Body = cuerpo },
                Data = datos ?? new Dictionary<string, string>(),
                Android = new AndroidConfig
                {
                    Priority = altaPrioridad ? Priority.High : Priority.Normal,
                    Notification = new AndroidNotification
                    {
                        ChannelId = altaPrioridad ? "alertas_criticas" : "alertas_preventivas"
                    }
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(mensaje);
            _logger.LogInformation("FCM multicast: {Success}/{Total}", response.SuccessCount, tokens.Count);
            return response.SuccessCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FCM multicast");
            return 0;
        }
    }

    public async Task<bool> EnviarATopicAsync(string topic, string titulo, string cuerpo, Dictionary<string, string>? datos = null)
    {
        if (!_inicializado || string.IsNullOrWhiteSpace(topic)) return false;

        try
        {
            var mensaje = new Message
            {
                Topic = topic,
                Notification = new Notification { Title = titulo, Body = cuerpo },
                Data = datos ?? new Dictionary<string, string>()
            };

            var response = await FirebaseMessaging.DefaultInstance.SendAsync(mensaje);
            _logger.LogInformation("FCM topic {Topic}: {MessageId}", topic, response);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending FCM to topic {Topic}", topic);
            return false;
        }
    }
}
