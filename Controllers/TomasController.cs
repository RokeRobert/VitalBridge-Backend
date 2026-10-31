// ============================================================================
// CONTROLADOR DE TOMAS Y SEGUIMIENTO DIARIO (TomasController.cs)
// ============================================================================
// Administra las tomas programadas y el registro de confirmación de medicamentos
// para el paciente en VitalBridge:
// 1. Obtener tomas de hoy: GET /api/tomas/hoy?pacienteId={guid}
// 2. Confirmar toma realizada: POST /api/tomas/confirmar
// ============================================================================

using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.DTOs;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Controllers;

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
    // 1. OBTENER TOMAS DEL DÍA DE HOY PARA UN PACIENTE
    // Endpoint: GET /api/tomas/hoy?pacienteId={guid}
    // ========================================================================
    [HttpGet("hoy")]
    public async Task<IActionResult> ObtenerTomasHoy(
        [FromQuery] Guid? pacienteId,
        [FromQuery] DateTime? fecha,
        [FromHeader(Name = "X-User-Id")] string? userIdHeader)
    {
        var query = _context.Horarios
            .Include(h => h.Tratamiento)
                .ThenInclude(t => t!.UnidadMedida)
            .Include(h => h.Toma)
                .ThenInclude(t => t!.EstadoToma)
            .Where(h => h.Tratamiento != null && h.Tratamiento.IdEstadoTratamiento == 1);

        // Filtrado por paciente o cuidador
        if (pacienteId.HasValue && pacienteId.Value != Guid.Empty)
        {
            query = query.Where(h => h.IdPaciente == pacienteId.Value);
        }
        else if (Guid.TryParse(userIdHeader, out var parsedUser) && parsedUser != Guid.Empty)
        {
            query = query.Where(h => h.IdUsuario == parsedUser || h.IdPaciente == parsedUser);
        }
        else
        {
            return BadRequest(new
            {
                exito = false,
                mensaje = "El parámetro pacienteId o el encabezado X-User-Id es obligatorio."
            });
        }

        // Definimos la ventana de tiempo (por defecto UTC del día indicado o hoy)
        var fechaReferencia = (fecha ?? DateTime.UtcNow).Date;
        var inicioDia = DateTime.SpecifyKind(fechaReferencia, DateTimeKind.Utc);
        var finDia = inicioDia.AddDays(1);

        var horarios = await query
            .Where(h => h.Dia >= inicioDia && h.Dia < finDia)
            .OrderBy(h => h.Dia)
            .ToListAsync();

        // Si no hay horarios exactos en la ventana estricta de 24h, consultar una ventana flexible
        // para absorber desfases de zona horaria entre servidor y dispositivo móvil
        if (!horarios.Any() && !fecha.HasValue)
        {
            var ventanaInicio = inicioDia.AddHours(-12);
            var ventanaFin = finDia.AddHours(12);

            horarios = await query
                .Where(h => h.Dia >= ventanaInicio && h.Dia <= ventanaFin)
                .OrderBy(h => h.Dia)
                .ToListAsync();
        }

        // Mapeo seguro a TomaRespuestaDto
        var resultado = horarios.Select(h =>
        {
            var dosis = h.Tratamiento != null
                ? $"{h.Tratamiento.Cantidad.ToString("0.##", CultureInfo.InvariantCulture)} {h.Tratamiento.UnidadMedida?.Abreviacion ?? h.Tratamiento.UnidadMedida?.NombreUnidad ?? "tableta"}"
                : "1 tableta";

            var horaFormateada = h.Dia.ToString("hh:mm tt", CultureInfo.InvariantCulture);

            string estado = "Pendiente";
            DateTime? horaTomada = null;
            string? observaciones = null;

            if (h.Toma != null)
            {
                estado = h.Toma.EstadoToma?.NombreEstado ?? "Tomada";
                horaTomada = h.Toma.HoraTomada;
                observaciones = h.Toma.Observaciones;
            }

            return new TomaRespuestaDto
            {
                IdHorario = h.IdHorario,
                IdTratamiento = h.IdTratamiento,
                Medicamento = h.Tratamiento?.Medicamento ?? "Medicamento",
                Dosis = dosis,
                HoraProgramada = horaFormateada,
                HoraTomada = horaTomada,
                Estado = estado,
                Observaciones = observaciones,
                Indicaciones = h.Tratamiento?.Indicaciones
            };
        }).ToList();

        return Ok(resultado);
    }

    // ========================================================================
    // 2. CONFIRMAR TOMA DE MEDICAMENTO
    // Endpoint: POST /api/tomas/confirmar
    // ========================================================================
    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarToma([FromBody] ConfirmarTomaDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (dto.IdHorario <= 0)
        {
            return BadRequest(new { exito = false, mensaje = "El ID de horario es inválido." });
        }

        // 1. Verificar existencia del registro de horario
        var horario = await _context.Horarios
            .Include(h => h.Tratamiento)
            .Include(h => h.Toma)
            .FirstOrDefaultAsync(h => h.IdHorario == dto.IdHorario);

        if (horario == null)
        {
            return NotFound(new
            {
                exito = false,
                mensaje = $"No se encontró el horario con ID {dto.IdHorario}."
            });
        }

        // 2. Resolver IdEstadoToma para estado 'Tomada'
        int idEstadoTomada = 2; // Valor estándar en catálogo
        var estadoCatalogo = await _context.EstadosToma
            .FirstOrDefaultAsync(e => e.NombreEstado.ToLower() == "tomada" || e.IdEstadoToma == 2 || e.IdEstadoToma == 1);

        if (estadoCatalogo != null)
        {
            idEstadoTomada = estadoCatalogo.IdEstadoToma;
        }

        var horaRegistro = DateTime.SpecifyKind(dto.HoraTomada ?? DateTime.UtcNow, DateTimeKind.Utc);

        // 3. Registrar o actualizar en la tabla 'toma'
        if (horario.Toma != null)
        {
            horario.Toma.IdEstadoToma = idEstadoTomada;
            horario.Toma.HoraTomada = horaRegistro;
            if (!string.IsNullOrWhiteSpace(dto.Observaciones))
            {
                horario.Toma.Observaciones = dto.Observaciones.Trim();
            }
            _context.Tomas.Update(horario.Toma);
        }
        else
        {
            var nuevaToma = new Toma
            {
                IdHorario = dto.IdHorario,
                IdEstadoToma = idEstadoTomada,
                HoraTomada = horaRegistro,
                Observaciones = dto.Observaciones?.Trim()
            };
            _context.Tomas.Add(nuevaToma);
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            exito = true,
            mensaje = "Toma de medicamento registrada correctamente.",
            idHorario = dto.IdHorario,
            estado = "Tomada",
            horaTomada = horaRegistro
        });
    }
}
