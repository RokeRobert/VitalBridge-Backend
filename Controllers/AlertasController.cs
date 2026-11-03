// ============================================================================
// CONTROLADOR DE ALERTAS DE EMERGENCIA SOS (AlertasController.cs)
// ============================================================================
// Gestiona las señales de auxilio emitidas por pacientes en VitalBridge:
// 1. Emitir alerta de emergencia: POST /api/alertas/sos
// 2. Consultar alertas activas: GET /api/alertas/activas
// 3. Resolver/atender alerta: POST /api/alertas/resolver
// 4. Registrar Token FCM para cuidador: POST /api/alertas/fcm-token
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.Models;
using VitalBridge_Backend.Services;

namespace VitalBridge_Backend.Controllers;

public class EmitirAlertaDto
{
    public Guid IdPaciente { get; set; }
    public string? Motivo { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
}

public class ResolverAlertaDto
{
    public int IdAlerta { get; set; }
    public string? Resolucion { get; set; }
    public DateTime? FechaResolucion { get; set; }
}

public class RegistrarFcmTokenDto
{
    public string Token { get; set; } = string.Empty;
    public Guid? IdUsuario { get; set; }
}

[ApiController]
[Route("api/[controller]")] // Ruta base: /api/alertas
public class AlertasController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AlertasController> _logger;
    private readonly FirebaseNotificationService _firebaseService;

    public AlertasController(
        ApplicationDbContext context,
        ILogger<AlertasController> logger,
        FirebaseNotificationService firebaseService)
    {
        _context = context;
        _logger = logger;
        _firebaseService = firebaseService;
    }

    // ========================================================================
    // 1. EMITIR ALERTA SOS DESDE EL MODO PACIENTE
    // Endpoint: POST /api/alertas/sos
    // ========================================================================
    [HttpPost("sos")]
    public async Task<IActionResult> EmitirAlerta([FromBody] EmitirAlertaDto dto)
    {
        if (dto.IdPaciente == Guid.Empty)
        {
            return BadRequest(new { exito = false, mensaje = "El ID del paciente es requerido." });
        }

        var paciente = await _context.Pacientes.FindAsync(dto.IdPaciente);
        if (paciente == null)
        {
            return NotFound(new { exito = false, mensaje = "Paciente no encontrado." });
        }

        var nuevaAlerta = new Alerta
        {
            IdPaciente = dto.IdPaciente,
            FechaCreacion = DateTime.UtcNow,
            IdEstadoAlerta = 1, // 1 = Activa
            Latitud = dto.Latitud,
            Longitud = dto.Longitud
        };

        _context.Alertas.Add(nuevaAlerta);
        await _context.SaveChangesAsync();

        var nombreCompleto = $"{paciente.NombrePaciente} {paciente.ApellidosPacientes}".Trim();
        _logger.LogWarning("ALERTA SOS EMITIDA: Paciente {Nombre} ({IdPaciente}) con GPS ({Lat}, {Lng})",
            nombreCompleto, dto.IdPaciente, dto.Latitud, dto.Longitud);

        // Despachar notificación push de emergencia vía Firebase Cloud Messaging
        try
        {
            var tokensCuidador = await _context.TokensUsuario
                .Where(t => t.IdUsuario == paciente.IdUsuario)
                .Select(t => t.Token)
                .ToListAsync();

            if (tokensCuidador.Any())
            {
                await _firebaseService.EnviarNotificacionSosAsync(
                    tokensCuidador,
                    nombreCompleto,
                    dto.Latitud,
                    dto.Longitud,
                    nuevaAlerta.IdAlerta);
            }
            else
            {
                _logger.LogWarning("No se encontraron tokens FCM registrados para el cuidador responsable {IdUsuario}", paciente.IdUsuario);
            }
        }
        catch (Exception exPush)
        {
            _logger.LogError(exPush, "Error al enviar notificación push de alerta SOS");
        }

        return StatusCode(201, new
        {
            idAlerta = nuevaAlerta.IdAlerta,
            idPaciente = nuevaAlerta.IdPaciente,
            nombrePaciente = nombreCompleto,
            fechaCreacion = DateTime.SpecifyKind(nuevaAlerta.FechaCreacion, DateTimeKind.Utc),
            idEstadoAlerta = nuevaAlerta.IdEstadoAlerta,
            estado = "Activa",
            latitud = nuevaAlerta.Latitud,
            longitud = nuevaAlerta.Longitud
        });
    }

    // ========================================================================
    // 2. CONSULTAR ALERTAS ACTIVAS PARA EL CUIDADOR
    // Endpoint: GET /api/alertas/activas
    // ========================================================================
    [HttpGet("activas")]
    public async Task<IActionResult> ObtenerAlertasActivas(
        [FromHeader(Name = "X-User-Id")] string? userIdHeader,
        [FromQuery] Guid? idUsuario,
        [FromQuery] Guid? idPaciente)
    {
        Guid? cuidadorId = idUsuario;
        if (!cuidadorId.HasValue && Guid.TryParse(userIdHeader, out var parsedId))
        {
            cuidadorId = parsedId;
        }

        if ((!idPaciente.HasValue || idPaciente.Value == Guid.Empty) &&
            (!cuidadorId.HasValue || cuidadorId.Value == Guid.Empty))
        {
            return Ok(new List<object>());
        }

        var query = _context.Alertas
            .Include(a => a.Paciente)
            .Include(a => a.EstadoAlerta)
            .Where(a => a.IdEstadoAlerta == 1); // Solo alertas activas

        if (idPaciente.HasValue && idPaciente.Value != Guid.Empty)
        {
            query = query.Where(a => a.IdPaciente == idPaciente.Value);
        }
        else if (cuidadorId.HasValue && cuidadorId.Value != Guid.Empty)
        {
            // Filtrar alertas que pertenezcan a los pacientes de este cuidador
            query = query.Where(a => a.Paciente != null && a.Paciente.IdUsuario == cuidadorId.Value);
        }

        var alertas = await query
            .OrderByDescending(a => a.FechaCreacion)
            .ToListAsync();

        var resultado = alertas.Select(a => new
        {
            idAlerta = a.IdAlerta,
            idPaciente = a.IdPaciente,
            nombrePaciente = a.Paciente != null
                ? $"{a.Paciente.NombrePaciente} {a.Paciente.ApellidosPacientes}".Trim()
                : "Paciente",
            fechaCreacion = DateTime.SpecifyKind(a.FechaCreacion, DateTimeKind.Utc),
            idEstadoAlerta = a.IdEstadoAlerta,
            estado = a.EstadoAlerta?.NombreEstado ?? "Activa",
            latitud = a.Latitud,
            longitud = a.Longitud
        }).ToList();

        return Ok(resultado);
    }

    // ========================================================================
    // 3. RESOLVER / ATENDER ALERTA
    // Endpoint: POST /api/alertas/resolver
    // ========================================================================
    [HttpPost("resolver")]
    public async Task<IActionResult> ResolverAlerta([FromBody] ResolverAlertaDto dto)
    {
        if (dto.IdAlerta <= 0)
        {
            return BadRequest(new { exito = false, mensaje = "ID de alerta inválido." });
        }

        var alerta = await _context.Alertas
            .Include(a => a.Paciente)
            .FirstOrDefaultAsync(a => a.IdAlerta == dto.IdAlerta);

        if (alerta == null)
        {
            return NotFound(new { exito = false, mensaje = "Alerta no encontrada." });
        }

        // Marcar como 2: Atendida
        alerta.IdEstadoAlerta = 2;
        await _context.SaveChangesAsync();

        _logger.LogInformation("ALERTA SOS RESUELTA: Alerta ID {IdAlerta}", dto.IdAlerta);

        return Ok(new
        {
            exito = true,
            mensaje = "Alerta resuelta exitosamente.",
            idAlerta = alerta.IdAlerta,
            idEstadoAlerta = alerta.IdEstadoAlerta,
            estado = "Atendida"
        });
    }

    // ========================================================================
    // 4. REGISTRAR TOKEN FCM DE DISPOSITIVO PARA CUIDADOR
    // Endpoint: POST /api/alertas/fcm-token
    // ========================================================================
    [HttpPost("fcm-token")]
    public async Task<IActionResult> RegistrarFcmToken(
        [FromBody] RegistrarFcmTokenDto dto,
        [FromHeader(Name = "X-User-Id")] string? userIdHeader)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
        {
            return BadRequest(new { exito = false, mensaje = "El token FCM es requerido." });
        }

        Guid usuarioId = Guid.Empty;
        if (dto.IdUsuario.HasValue && dto.IdUsuario.Value != Guid.Empty)
        {
            usuarioId = dto.IdUsuario.Value;
        }
        else if (Guid.TryParse(userIdHeader, out var parsedId))
        {
            usuarioId = parsedId;
        }

        if (usuarioId == Guid.Empty)
        {
            return BadRequest(new { exito = false, mensaje = "El ID del usuario cuidador es requerido." });
        }

        var tokenExistente = await _context.TokensUsuario.FindAsync(dto.Token.Trim());
        if (tokenExistente == null)
        {
            _context.TokensUsuario.Add(new TokenUsuario
            {
                Token = dto.Token.Trim(),
                IdUsuario = usuarioId,
                FechaCreacion = DateTime.UtcNow
            });
        }
        else
        {
            tokenExistente.IdUsuario = usuarioId;
            tokenExistente.FechaCreacion = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Token FCM registrado exitosamente para el usuario {IdUsuario}", usuarioId);

        return Ok(new { exito = true, mensaje = "Token FCM registrado exitosamente." });
    }
}
