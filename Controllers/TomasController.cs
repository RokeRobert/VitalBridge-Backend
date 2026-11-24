// ============================================================================
// CONTROLADOR DE TOMAS Y SEGUIMIENTO DIARIO (TomasController.cs)
// ============================================================================
// Administra las tomas programadas y el registro de confirmación de medicamentos
// para el paciente en VitalBridge:
// 1. Obtener tomas de hoy: GET /api/tomas/hoy?pacienteId={guid}
// 2. Obtener historial de tomas por fecha: GET /api/tomas/historial?pacienteId={guid}&fecha=YYYY-MM-DD
// 3. Confirmar toma realizada: POST /api/tomas/confirmar
// ============================================================================

using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.DTOs;
using VitalBridge_Backend.Extensions;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Controllers;

[Authorize]
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
    // 1. OBTENER TOMAS DEL DÍA DE HOY O HISTORIAL POR FECHA
    // Endpoints:
    //   GET /api/tomas/hoy?pacienteId={guid}&fecha=YYYY-MM-DD
    //   GET /api/tomas/historial?pacienteId={guid}&fecha=YYYY-MM-DD
    // ========================================================================
    [HttpGet("hoy")]
    [HttpGet("historial")]
    public async Task<IActionResult> ObtenerTomas(
        [FromQuery] Guid? pacienteId,
        [FromQuery] DateTime? fecha,
        [FromHeader(Name = "X-User-Id")] string? userIdHeader)
    {
        var usuarioIdAutenticado = User.ObtenerUsuarioId(Request);

        var query = _context.Horarios
            .Include(h => h.Tratamiento)
                .ThenInclude(t => t!.UnidadMedida)
            .Include(h => h.Toma)
                .ThenInclude(t => t!.EstadoToma)
            .AsQueryable();

        // Si es consulta en vivo (hoy sin fecha histórica específica), filtramos por tratamientos activos.
        // Si es consulta de historial por fecha, permitimos mostrar tomas de tratamientos finalizados.
        if (!fecha.HasValue)
        {
            query = query.Where(h => h.Tratamiento != null && h.Tratamiento.IdEstadoTratamiento == 1);
        }

        // Filtrado de seguridad: solo consultar datos de los pacientes vinculados al cuidador
        if (pacienteId.HasValue && pacienteId.Value != Guid.Empty)
        {
            query = query.Where(h => h.IdPaciente == pacienteId.Value);
        }
        else if (usuarioIdAutenticado.HasValue && usuarioIdAutenticado.Value != Guid.Empty)
        {
            query = query.Where(h => h.IdUsuario == usuarioIdAutenticado.Value || h.IdPaciente == usuarioIdAutenticado.Value);
        }
        else
        {
            return BadRequest(new
            {
                exito = false,
                mensaje = "El parámetro pacienteId o la autenticación del usuario es obligatoria."
            });
        }

        // Definimos la ventana de tiempo. Si no se pasa fecha, usamos la fecha local del cliente
        // calculada a partir de UTC con ajuste para zonas horarias de México/América (UTC-8 a UTC-6)
        var ahoraLocal = DateTime.UtcNow.AddHours(-8);
        var fechaReferencia = (fecha ?? ahoraLocal).Date;
        var inicioDia = DateTime.SpecifyKind(fechaReferencia, DateTimeKind.Utc);
        var finDia = inicioDia.AddDays(1);

        var horarios = await query
            .Where(h => h.Dia >= inicioDia && h.Dia < finDia)
            .OrderBy(h => h.Dia)
            .ToListAsync();

        // Si no hay horarios exactos en la ventana estricta de 24h y es consulta de hoy,
        // consultar una ventana flexible para absorber desfases de zona horaria
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
                horaTomada = h.Toma.HoraTomada.Kind == DateTimeKind.Utc
                    ? h.Toma.HoraTomada
                    : DateTime.SpecifyKind(h.Toma.HoraTomada, DateTimeKind.Utc);
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
    [AllowAnonymous] // Permitir al modo paciente registrar su toma sin necesidad de sesión de cuidador
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
        int idEstadoTomada = 1;
        var estadoCatalogo = await _context.EstadosToma
            .FirstOrDefaultAsync(e => e.NombreEstado.ToLower() == "tomada" || e.IdEstadoToma == 1);

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
