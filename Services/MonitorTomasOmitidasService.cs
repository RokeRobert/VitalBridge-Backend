using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Services;

/// <summary>
/// Servicio en segundo plano que verifica cada minuto si algún paciente
/// superó los 15 minutos de retraso sin confirmar la toma de su medicamento
/// y notifica por FCM al cuidador responsable.
/// </summary>
public class MonitorTomasOmitidasService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FirebaseNotificationService _firebaseService;
    private readonly ILogger<MonitorTomasOmitidasService> _logger;

    public MonitorTomasOmitidasService(
        IServiceScopeFactory scopeFactory,
        FirebaseNotificationService firebaseService,
        ILogger<MonitorTomasOmitidasService> logger)
    {
        _scopeFactory = scopeFactory;
        _firebaseService = firebaseService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Esperar unos segundos al arranque para asegurar que DbSeeder y tablas estén listos
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerificarTomasOmitidasAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en MonitorTomasOmitidasService al verificar tomas omitidas.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task VerificarTomasOmitidasAsync(CancellationToken ct)
    {
        var ahoraLocal = ObtenerHoraLocalTijuana();
        var ahoraWallClockUtc = DateTime.SpecifyKind(ahoraLocal, DateTimeKind.Utc);
        var limiteSuperior = ahoraWallClockUtc.AddMinutes(-15);
        var limiteInferior = ahoraWallClockUtc.AddMinutes(-90);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var horariosPendientes = await context.Horarios
            .Include(h => h.Paciente)
            .Include(h => h.Tratamiento)
                .ThenInclude(t => t!.UnidadMedida)
            .Include(h => h.Toma)
            .Where(h =>
                h.Tratamiento != null && h.Tratamiento.IdEstadoTratamiento == 1 &&
                h.Paciente != null && h.Paciente.IdEstadoPaciente == 1 &&
                h.Dia >= limiteInferior && h.Dia <= limiteSuperior &&
                (h.Toma == null || h.Toma.IdEstadoToma == 1) &&
                !context.AlertasTomaOmitida.Any(a => a.IdHorario == h.IdHorario))
            .ToListAsync(ct);

        if (!horariosPendientes.Any()) return;

        foreach (var h in horariosPendientes)
        {
            if (ct.IsCancellationRequested) break;

            var tokensCuidador = await context.TokensUsuario
                .Where(t => t.IdUsuario == h.IdUsuario)
                .Select(t => t.Token)
                .ToListAsync(ct);

            var nombrePaciente = h.Paciente != null
                ? $"{h.Paciente.NombrePaciente} {h.Paciente.ApellidosPacientes}".Trim()
                : "El paciente";

            var medicamento = h.Tratamiento?.Medicamento ?? "Medicamento";
            var dosis = h.Tratamiento != null
                ? $"{h.Tratamiento.Cantidad.ToString("0.##", CultureInfo.InvariantCulture)} {h.Tratamiento.UnidadMedida?.Abreviacion ?? h.Tratamiento.UnidadMedida?.NombreUnidad ?? "tableta"}"
                : "1 dosis";
            var horaProgramada = h.Dia.ToString("hh:mm tt", CultureInfo.InvariantCulture);

            if (tokensCuidador.Any())
            {
                await _firebaseService.EnviarNotificacionTomaOmitidaAsync(
                    tokensCuidador,
                    nombrePaciente,
                    medicamento,
                    dosis,
                    horaProgramada,
                    h.IdHorario,
                    h.IdPaciente);
            }

            // Registrar que este horario ya fue notificado para evitar duplicados
            context.AlertasTomaOmitida.Add(new AlertaTomaOmitida
            {
                IdHorario = h.IdHorario,
                FechaNotificacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync(ct);
        }
    }

    private static DateTime ObtenerHoraLocalTijuana()
    {
        foreach (var tzId in new[] { "America/Tijuana", "Pacific Standard Time" })
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch { }
        }
        return DateTime.UtcNow.AddHours(-8);
    }
}
