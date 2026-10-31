// ============================================================================
// CONTROLADOR DE TOMAS Y SEGUIMIENTO DIARIO (TomasController.cs)
// ============================================================================
// Administra las tomas programadas para el día y la confirmación de ingesta
// de medicamentos por parte del paciente o cuidador:
// 1. Obtener tomas de hoy: GET /api/tomas/hoy?pacienteId={guid}
// 2. Confirmar toma realizada: POST /api/tomas/confirmar
// ============================================================================

using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Controllers;

public class ConfirmarTomaDto
{
    public int IdHorario { get; set; }
    public DateTime? HoraTomada { get; set; }
    public string? Observaciones { get; set; }
}

[ApiController]
[Route("api/[controller]")] // Ruta base: /api/tomas
public class TomasController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TomasController> _logger;

    public TomasController(ApplicationDbContext context, ILogger<TomasController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ========================================================================
    // 1. OBTENER TOMAS DEL DÍA DE HOY
    // Endpoint: GET /api/tomas/hoy?pacienteId={guid}
    // ========================================================================
    [HttpGet("hoy")]
    public async Task<IActionResult> ObtenerTomasHoy(
        [FromQuery] Guid? pacienteId,
        [FromHeader(Name = "X-User-Id")] string? userIdHeader)
    {
        var query = _context.Horarios
            .Include(h => h.Tratamiento)
                .ThenInclude(t => t!.UnidadMedida)
            .Include(h => h.Toma)
                .ThenInclude(t => t!.EstadoToma)
            .Where(h => h.Tratamiento != null && h.Tratamiento.IdEstadoTratamiento == 1);

        if (pacienteId.HasValue && pacienteId.Value != Guid.Empty)
        {
            query = query.Where(h => h.IdPaciente == pacienteId.Value);
        }
        else if (Guid.TryParse(userIdHeader, out var parsedUser))
        {
            query = query.Where(h => h.IdUsuario == parsedUser);
        }

        // Ventana del día de hoy en UTC
        var hoyUtc = DateTime.UtcNow.Date;
        var mañanaUtc = hoyUtc.AddDays(1);

        var horariosHoy = await query
            .Where(h => h.Dia >= hoyUtc && h.Dia < mañanaUtc)
            .OrderBy(h => h.Dia)
            .ToListAsync();

        // Si no hay horarios exactos para la fecha UTC de hoy pero hay tratamientos activos,
        // buscar tomas en una ventana flexible de 24 horas alrededor de hoy
        if (!horariosHoy.Any())
        {
            var ventanaInicio = hoyUtc.AddHours(-12);
            var ventanaFin = hoyUtc.AddHours(36);

            horariosHoy = await query
                .Where(h => h.Dia >= ventanaInicio && h.Dia <= ventanaFin)
                .OrderBy(h => h.Dia)
                .ToListAsync();
        }

        var tomasResultado = horariosHoy.Select(h =>
        {
            var trat = h.Tratamiento!;
            var unidad = trat.UnidadMedida?.Abreviacion ?? trat.UnidadMedida?.NombreUnidad ?? "dosis";
            var dosisTexto = $"{trat.Cantidad:0.##} {unidad}";

            var esTomada = h.Toma != null && h.Toma.IdEstadoToma == 1;

            return new
            {
                idHorario = h.IdHorario,
                idTratamiento = trat.IdTratamiento,
                medicamento = trat.Medicamento,
                dosis = dosisTexto,
                horaProgramada = h.Dia.ToString("h:mm tt", CultureInfo.InvariantCulture),
                horaTomada = h.Toma?.HoraTomada,
                estado = esTomada ? "Tomada" : "Pendiente",
                observaciones = h.Toma?.Observaciones
            };
        }).ToList();

        return Ok(tomasResultado);
    }

    // ========================================================================
    // 2. CONFIRMAR TOMA DE MEDICAMENTO
    // Endpoint: POST /api/tomas/confirmar
    // ========================================================================
    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarToma([FromBody] ConfirmarTomaDto dto)
    {
        if (dto.IdHorario <= 0)
        {
            return BadRequest(new { exito = false, mensaje = "El ID de horario es inválido." });
        }

        var horario = await _context.Horarios
            .Include(h => h.Toma)
            .FirstOrDefaultAsync(h => h.IdHorario == dto.IdHorario);

        if (horario == null)
        {
            return NotFound(new { exito = false, mensaje = "Horario de toma no encontrado." });
        }

        var fechaToma = dto.HoraTomada ?? DateTime.UtcNow;
        if (horario.Toma == null)
        {
            var nuevaToma = new Toma
            {
                IdHorario = dto.IdHorario,
                IdEstadoToma = 1, // 1 = Tomada
                HoraTomada = DateTime.SpecifyKind(fechaToma, DateTimeKind.Utc),
                Observaciones = dto.Observaciones?.Trim()
            };
            _context.Tomas.Add(nuevaToma);
        }
        else
        {
            horario.Toma.IdEstadoToma = 1; // 1 = Tomada
            horario.Toma.HoraTomada = DateTime.SpecifyKind(fechaToma, DateTimeKind.Utc);
            if (!string.IsNullOrWhiteSpace(dto.Observaciones))
            {
                horario.Toma.Observaciones = dto.Observaciones.Trim();
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            exito = true,
            mensaje = "Toma registrada exitosamente."
        });
    }
}

