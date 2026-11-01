// ============================================================================
// CONTROLADOR DE ALERTAS DE EMERGENCIA SOS (AlertasController.cs)
// ============================================================================
// Gestiona las señales de auxilio emitidas por pacientes en VitalBridge:
// 1. Emitir alerta de emergencia: POST /api/alertas/sos
// 2. Consultar alertas activas: GET /api/alertas/activas
// 3. Resolver/atender alerta: POST /api/alertas/resolver
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.Models;

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

[ApiController]
[Route("api/[controller]")] // Ruta base: /api/alertas
public class AlertasController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AlertasController> _logger;

    public AlertasController(ApplicationDbContext context, ILogger<AlertasController> logger)
    {
        _context = context;
        _logger = logger;
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
            IdEstadoAlerta = 1 // 1 = Activa
        };

        _context.Alertas.Add(nuevaAlerta);
        await _context.SaveChangesAsync();

        _logger.LogWarning("ALERTA SOS EMITIDA: Paciente {Nombre} ({IdPaciente})", paciente.NombrePaciente, dto.IdPaciente);

        return StatusCode(201, new
        {
            idAlerta = nuevaAlerta.IdAlerta,
            idPaciente = nuevaAlerta.IdPaciente,
            nombrePaciente = $"{paciente.NombrePaciente} {paciente.ApellidosPacientes}".Trim(),
            fechaCreacion = nuevaAlerta.FechaCreacion,
            idEstadoAlerta = nuevaAlerta.IdEstadoAlerta,
            estado = "Activa"
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
        var query = _context.Alertas
            .Include(a => a.Paciente)
            .Include(a => a.EstadoAlerta)
            .Where(a => a.IdEstadoAlerta == 1); // Solo alertas activas

        if (idPaciente.HasValue && idPaciente.Value != Guid.Empty)
        {
            query = query.Where(a => a.IdPaciente == idPaciente.Value);
        }
        else
        {
            Guid? cuidadorId = idUsuario;
            if (!cuidadorId.HasValue && Guid.TryParse(userIdHeader, out var parsedId))
            {
                cuidadorId = parsedId;
            }

            if (cuidadorId.HasValue && cuidadorId.Value != Guid.Empty)
            {
                // Filtrar alertas que pertenezcan a los pacientes de este cuidador
                query = query.Where(a => a.Paciente != null && a.Paciente.IdUsuario == cuidadorId.Value);
            }
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
            fechaCreacion = a.FechaCreacion,
            idEstadoAlerta = a.IdEstadoAlerta,
            estado = a.EstadoAlerta?.NombreEstado ?? "Activa"
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
}
