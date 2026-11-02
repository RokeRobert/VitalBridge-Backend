using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace VitalBridge_Backend.Services;

public class FirebaseNotificationService
{
    private readonly ILogger<FirebaseNotificationService> _logger;
    private readonly bool _inicializado;

    public FirebaseNotificationService(ILogger<FirebaseNotificationService> logger)
    {
        _logger = logger;

        try
        {
            if (FirebaseApp.DefaultInstance != null)
            {
                _inicializado = true;
                return;
            }

            GoogleCredential? credential = null;

            // 1. Intentar cargar desde variable de entorno (Recomendado para Render en producción)
            var jsonEnv = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_JSON");
            if (!string.IsNullOrWhiteSpace(jsonEnv))
            {
                credential = GoogleCredential.FromJson(jsonEnv);
                _logger.LogInformation("Firebase Admin SDK inicializado desde variable de entorno FIREBASE_CREDENTIALS_JSON.");
            }
            else
            {
                // 2. Intentar buscar archivos de credenciales en el directorio del proyecto
                var directorios = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
                foreach (var dir in directorios)
                {
                    var archivos = Directory.GetFiles(dir, "*firebase*.json");
                    if (archivos.Length > 0)
                    {
                        var rutaArchivo = archivos[0];
                        credential = GoogleCredential.FromFile(rutaArchivo);
                        _logger.LogInformation("Firebase Admin SDK inicializado desde archivo: {Ruta}", Path.GetFileName(rutaArchivo));
                        break;
                    }
                }
            }

            if (credential != null)
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = credential
                });
                _inicializado = true;
            }
            else
            {
                _logger.LogWarning("No se encontraron credenciales de Firebase (archivo *firebase*.json o variable FIREBASE_CREDENTIALS_JSON). Notificaciones push deshabilitadas.");
                _inicializado = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al inicializar Firebase Admin SDK.");
            _inicializado = false;
        }
    }

    public async Task<int> EnviarNotificacionSosAsync(
        List<string> tokens,
        string nombrePaciente,
        double? latitud,
        double? longitud,
        int idAlerta)
    {
        if (!_inicializado || tokens == null || tokens.Count == 0)
        {
            _logger.LogWarning("Envío SOS omitido: Firebase no inicializado o lista de tokens vacía.");
            return 0;
        }

        var tokensUnicos = tokens.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        if (tokensUnicos.Count == 0) return 0;

        try
        {
            // Se envía como mensaje de datos de alta prioridad (Data Message con Priority = High).
            // Esto garantiza que Android despierte el proceso de fondo de la app incluso si está
            // cerrada o con pantalla bloqueada, permitiendo que el handler nativo ejecute la alarma
            // en pantalla completa con sonido y vibración.
            var message = new MulticastMessage
            {
                Tokens = tokensUnicos,
                Data = new Dictionary<string, string>
                {
                    { "tipo", "SOS" },
                    { "titulo", "¡ALERTA DE EMERGENCIA SOS!" },
                    { "cuerpo", $"El paciente {nombrePaciente} requiere asistencia urgente." },
                    { "idAlerta", idAlerta.ToString() },
                    { "nombrePaciente", nombrePaciente },
                    { "latitud", latitud?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "" },
                    { "longitud", longitud?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "" },
                    { "fechaHora", DateTime.UtcNow.ToString("o") }
                },
                Android = new AndroidConfig
                {
                    Priority = Priority.High
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            _logger.LogInformation(
                "Notificación SOS enviada vía FCM (Data Message de alta prioridad). Éxitos: {Exitos}, Fallos: {Fallos} (Total tokens: {Total})",
                response.SuccessCount, response.FailureCount, tokensUnicos.Count);

            return response.SuccessCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al despachar notificación SOS mediante Firebase Cloud Messaging.");
            return 0;
        }
    }
}
