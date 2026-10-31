// ============================================================================
// CONTROLADOR DE TRATAMIENTOS MÉDICOS (TratamientosController.cs)
// ============================================================================
// Permite consultar los tratamientos de un paciente y dar de baja tratamientos.
// Endpoints:
// - GET    /api/tratamientos?pacienteId={guid} -> Consulta tratamientos activos
// - DELETE /api/tratamientos/{id}              -> Elimina tratamiento y sus horarios
// ============================================================================

using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Controllers;

[ApiController]
[Route("api/[controller]")] // Ruta base: /api/tratamientos
public class TratamientosController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public TratamientosController(ApplicationDbContext context)
    {
        _context = context;
    }

    // ========================================================================
    // 1. OBTENER TRATAMIENTOS DE UN PACIENTE
    // Endpoint: GET /api/tratamientos?pacienteId={guid}
    // ========================================================================
    [HttpGet]
    public async Task<IActionResult> ObtenerTratamientos([FromQuery] Guid? pacienteId)
    {
        var query = _context.Tratamientos
            .Include(t => t.UnidadMedida)
            .Include(t => t.Horarios)
            .Where(t => t.IdEstadoTratamiento == 1); // Solo tratamientos activos

        if (pacienteId.HasValue && pacienteId.Value != Guid.Empty)
        {
            query = query.Where(t => t.IdPaciente == pacienteId.Value);
        }

        var tratamientos = await query
            .OrderByDescending(t => t.FechaInicio)
            .ToListAsync();

        var resultado = tratamientos.Select(t => new
        {
            idTratamiento = t.IdTratamiento,
            idPaciente = t.IdPaciente,
            idUsuario = t.IdUsuario,
            medicamento = t.Medicamento,
            cantidad = t.Cantidad,
            unidadMedida = t.UnidadMedida != null ? t.UnidadMedida.Abreviacion : "tableta",
            idUnidadMedida = t.IdUnidadMedida,
            frecuencia = $"Cada {t.Frecuencia.Hours} horas",
            fechaInicio = t.FechaInicio.ToString("yyyy-MM-dd"),
            fechaFinal = t.FechaFinal?.ToString("yyyy-MM-dd"),
            indicaciones = t.Indicaciones,
            activo = t.IdEstadoTratamiento == 1,
            horarios = t.Horarios
                .Select(h => h.Dia.ToString("hh:mm tt", CultureInfo.InvariantCulture))
                .Distinct()
                .ToList(),
            presentacion = t.UnidadMedida?.NombreUnidad ?? "Tableta"
        }).ToList();

        return Ok(resultado);
    }

    // ========================================================================
    // 2. ELIMINAR TRATAMIENTO MÉDICO (Reflejo inmediato en el paciente)
    // Endpoint: DELETE /api/tratamientos/{id}
    // ========================================================================
    [HttpDelete("{id}")]
    public async Task<IActionResult> EliminarTratamiento(int id)
    {
        var tratamiento = await _context.Tratamientos
            .Include(t => t.Horarios)
                .ThenInclude(h => h.Toma)
            .FirstOrDefaultAsync(t => t.IdTratamiento == id);

        if (tratamiento == null)
        {
            return NotFound(new
            {
                exito = false,
                mensaje = $"No se encontró el tratamiento con ID {id}."
            });
        }

        // Al eliminar el tratamiento, también removemos sus horarios asociados
        // para que dejen de figurar en la pantalla del paciente de inmediato.
        if (tratamiento.Horarios.Any())
        {
            _context.Horarios.RemoveRange(tratamiento.Horarios);
        }

        _context.Tratamientos.Remove(tratamiento);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            exito = true,
            mensaje = $"Tratamiento '{tratamiento.Medicamento}' eliminado exitosamente."
        });
    }
}
