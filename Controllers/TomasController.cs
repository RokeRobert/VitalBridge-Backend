// ============================================================================
// CONTROLADOR DE TOMAS Y HORARIOS DE MEDICAMENTOS (TomasController.cs)
// ============================================================================
// Administra las tomas programadas y el registro de confirmación de medicamentos
// para el paciente en VitalBridge.
// Endpoints:
// - GET  /api/tomas/hoy?pacienteId={guid} -> Consulta tomas del día con estado
// - POST /api/tomas/confirmar             -> Registra o actualiza toma confirmada
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

    public TomasController(ApplicationDbContext context)
    {
        _context = context;
    }

    // ========================================================================
    // 1. OBTENER TOMAS DEL DÍA DE HOY PARA UN PACIENTE
    // Endpoint: GET /api/tomas/hoy?pacienteId={guid}
    // ========================================================================
    [HttpGet("hoy")]
    public async Task<IActionResult> ObtenerTomasHoy(
        [FromQuery] Guid? pacienteId,
        [FromQuery] DateTime? fecha)
    {
        if (!pacienteId.HasValue || pacienteId.Value == Guid.Empty)
        {
            return BadRequest(new
            {
                exito = false,
                mensaje = "El parámetro pacienteId es obligatorio y debe ser un GUID válido."
            });
        }

        // Definimos el rango del día (por defecto UTC de hoy)
        var fechaReferencia = (fecha ?? DateTime.UtcNow).Date;
        var inicioDia = fechaReferencia;
        var finDia = fechaReferencia.AddDays(1);

        // Consultamos la tabla 'horario' con sus relaciones (Eager Loading)
        // para traer datos del medicamento, dosis y si ya fue tomada.
        var horarios = await _context.Horarios
            .Include(h => h.Tratamiento)
                .ThenInclude(t => t!.UnidadMedida)
            .Include(h => h.Toma)
                .ThenInclude(t => t!.EstadoToma)
            .Where(h => h.IdPaciente == pacienteId.Value &&
                        h.Tratamiento != null &&
                        h.Tratamiento.IdEstadoTratamiento == 1 &&
                        h.Dia >= inicioDia && h.Dia < finDia)
            .OrderBy(h => h.Dia)
            .ToListAsync();

        // Mapeamos a DTO para enviar un formato limpio y seguro a Flutter
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

        // 1. Verificar existencia del registro de horario
        var horario = await _context.Horarios
            .Include(h => h.Tratamiento)
            .FirstOrDefaultAsync(h => h.IdHorario == dto.IdHorario);

        if (horario == null)
        {
            return NotFound(new
            {
                exito = false,
                mensaje = $"No se encontró el horario con ID {dto.IdHorario}."
            });
        }

        // 2. Determinar IdEstadoToma
        // Especificación del proyecto: IdEstadoToma = 2 (Tomada)
        // Verificamos si en catálogo existe estado 'Tomada' o usamos 2 por defecto
        int idEstadoTomada = 2;
        var estadoCatalogo = await _context.EstadosToma
            .FirstOrDefaultAsync(e => e.IdEstadoToma == 2 || e.NombreEstado.ToLower() == "tomada");

        if (estadoCatalogo != null)
        {
            idEstadoTomada = estadoCatalogo.IdEstadoToma;
        }

        var horaRegistro = dto.HoraTomada ?? DateTime.UtcNow;

        // 3. Registrar o actualizar en la tabla 'toma'
        var tomaExistente = await _context.Tomas
            .FirstOrDefaultAsync(t => t.IdHorario == dto.IdHorario);

        if (tomaExistente != null)
        {
            tomaExistente.IdEstadoToma = idEstadoTomada;
            tomaExistente.HoraTomada = horaRegistro;
            if (!string.IsNullOrWhiteSpace(dto.Observaciones))
            {
                tomaExistente.Observaciones = dto.Observaciones;
            }
            _context.Tomas.Update(tomaExistente);
        }
        else
        {
            var nuevaToma = new Toma
            {
                IdHorario = dto.IdHorario,
                IdEstadoToma = idEstadoTomada,
                HoraTomada = horaRegistro,
                Observaciones = dto.Observaciones
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

