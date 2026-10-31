// ============================================================================
// CONTROLADOR DE TRATAMIENTOS MÉDICOS (TratamientosController.cs)
// ============================================================================
// Administra el ciclo de vida de los medicamentos y recetas asignadas al paciente:
// 1. Consulta de tratamientos activos (GET /api/tratamientos?pacienteId={guid}).
// 2. Registro de tratamiento y generación automática de horarios (POST /api/tratamientos).
// 3. Modificación de un tratamiento existente (PUT /api/tratamientos/{id}).
// 4. Baja lógica / Soft Delete cambiando IdEstadoTratamiento = 2 (DELETE /api/tratamientos/{id}).
// ============================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.DTOs;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Controllers;

[ApiController]
[Route("api/[controller]")] // Ruta base: /api/tratamientos
public class TratamientosController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TratamientosController> _logger;

    public TratamientosController(ApplicationDbContext context, ILogger<TratamientosController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ========================================================================
    // 1. OBTENER TRATAMIENTOS ACTIVOS DEL PACIENTE
    // Endpoint: GET /api/tratamientos?pacienteId={guid}
    // ========================================================================
    [HttpGet]
    public async Task<IActionResult> ObtenerTratamientos(
        [FromQuery] Guid? pacienteId,
        [FromHeader(Name = "X-User-Id")] string? userIdHeader)
    {
        var query = _context.Tratamientos
            .Include(t => t.UnidadMedida)
            .Include(t => t.Horarios)
            .Where(t => t.IdEstadoTratamiento == 1); // Solo tratamientos activos

        if (pacienteId.HasValue && pacienteId.Value != Guid.Empty)
        {
            query = query.Where(t => t.IdPaciente == pacienteId.Value);
        }
        else if (Guid.TryParse(userIdHeader, out var parsedUserId))
        {
            // Si no se pasó pacienteId pero sí el ID del cuidador, obtener tratamientos de sus pacientes
            query = query.Where(t => t.IdUsuario == parsedUserId);
        }

        var tratamientos = await query
            .OrderByDescending(t => t.IdTratamiento)
            .ToListAsync();

        var resultado = tratamientos.Select(t => MapearARespuestaDto(t)).ToList();
        return Ok(resultado);
    }

    // ========================================================================
    // 2. OBTENER UN TRATAMIENTO POR ID
    // Endpoint: GET /api/tratamientos/{id}
    // ========================================================================
    [HttpGet("{id}")]
    public async Task<IActionResult> ObtenerTratamientoPorId(int id)
    {
        var tratamiento = await _context.Tratamientos
            .Include(t => t.UnidadMedida)
            .Include(t => t.Horarios)
            .FirstOrDefaultAsync(t => t.IdTratamiento == id);

        if (tratamiento == null)
            return NotFound(new { exito = false, mensaje = "Tratamiento no encontrado." });

        return Ok(MapearARespuestaDto(tratamiento));
    }

    // ========================================================================
    // 3. REGISTRAR TRATAMIENTO Y GENERAR HORARIOS AUTOMÁTICOS
    // Endpoint: POST /api/tratamientos
    // ========================================================================
    [HttpPost]
    public async Task<IActionResult> CrearTratamiento(
        [FromBody] CrearTratamientoDto dto,
        [FromHeader(Name = "X-User-Id")] string? userIdHeader)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // 1. Validar que el paciente exista
        var paciente = await _context.Pacientes.FindAsync(dto.IdPaciente);
        if (paciente == null)
        {
            return NotFound(new { exito = false, mensaje = "El paciente especificado no existe." });
        }

        // 2. Resolver el ID del cuidador responsable
        Guid cuidadorId = paciente.IdUsuario;
        if (dto.IdUsuario.HasValue && dto.IdUsuario.Value != Guid.Empty)
        {
            cuidadorId = dto.IdUsuario.Value;
        }
        else if (Guid.TryParse(userIdHeader, out var headerId) && headerId != Guid.Empty)
        {
            cuidadorId = headerId;
        }

        // 3. Resolver la unidad de medida correspondiente
        var idUnidadMedida = await ResolverUnidadMedidaAsync(dto.IdUnidadMedida, dto.UnidadMedida, dto.Presentacion);

        // 4. Resolver frecuencia en TimeSpan
        var frecuencia = ParseFrecuencia(dto.Frecuencia, dto.Horarios.Count);

        // 5. Fechas de vigencia
        var fechaInicio = dto.FechaInicio ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // 6. Instanciar y guardar la cabecera del tratamiento
        var tratamiento = new Tratamiento
        {
            IdPaciente = dto.IdPaciente,
            IdUsuario = cuidadorId,
            Medicamento = dto.Medicamento.Trim(),
            Cantidad = dto.Cantidad,
            IdUnidadMedida = idUnidadMedida,
            Frecuencia = frecuencia,
            FechaInicio = fechaInicio,
            FechaFinal = dto.FechaFinal,
            Indicaciones = string.IsNullOrWhiteSpace(dto.Indicaciones) ? null : dto.Indicaciones.Trim(),
            IdEstadoTratamiento = 1 // 1 = Activo
        };

        _context.Tratamientos.Add(tratamiento);
        await _context.SaveChangesAsync();

        // 7. Generar automáticamente los registros en la tabla 'horario'
        var horariosGenerados = GenerarHorarios(tratamiento, dto.Horarios);
        if (horariosGenerados.Any())
        {
            _context.Horarios.AddRange(horariosGenerados);
            await _context.SaveChangesAsync();
        }

        // 8. Cargar navegación para respuesta enriquecida
        await _context.Entry(tratamiento).Reference(t => t.UnidadMedida).LoadAsync();
        await _context.Entry(tratamiento).Collection(t => t.Horarios).LoadAsync();

        return CreatedAtAction(
            nameof(ObtenerTratamientoPorId),
            new { id = tratamiento.IdTratamiento },
            MapearARespuestaDto(tratamiento, dto.Horarios, dto.Presentacion));
    }

    // ========================================================================
    // 4. ACTUALIZAR TRATAMIENTO EXISTENTE
    // Endpoint: PUT /api/tratamientos/{id}
    // ========================================================================
    [HttpPut("{id}")]
    public async Task<IActionResult> ActualizarTratamiento(int id, [FromBody] ActualizarTratamientoDto dto)
    {
        var tratamiento = await _context.Tratamientos
            .Include(t => t.UnidadMedida)
            .Include(t => t.Horarios)
            .FirstOrDefaultAsync(t => t.IdTratamiento == id);

        if (tratamiento == null)
            return NotFound(new { exito = false, mensaje = "Tratamiento no encontrado." });

        if (!string.IsNullOrWhiteSpace(dto.Medicamento))
            tratamiento.Medicamento = dto.Medicamento.Trim();

        if (dto.Cantidad.HasValue && dto.Cantidad.Value > 0)
            tratamiento.Cantidad = dto.Cantidad.Value;

        if (dto.IdUnidadMedida.HasValue || !string.IsNullOrWhiteSpace(dto.UnidadMedida) || !string.IsNullOrWhiteSpace(dto.Presentacion))
        {
            tratamiento.IdUnidadMedida = await ResolverUnidadMedidaAsync(dto.IdUnidadMedida, dto.UnidadMedida, dto.Presentacion);
        }

        if (!string.IsNullOrWhiteSpace(dto.Frecuencia))
        {
            tratamiento.Frecuencia = ParseFrecuencia(dto.Frecuencia, dto.Horarios?.Count ?? 0);
        }

        if (dto.FechaInicio.HasValue)
            tratamiento.FechaInicio = dto.FechaInicio.Value;

        if (dto.FechaFinal.HasValue)
            tratamiento.FechaFinal = dto.FechaFinal.Value;

        if (dto.Indicaciones != null)
            tratamiento.Indicaciones = string.IsNullOrWhiteSpace(dto.Indicaciones) ? null : dto.Indicaciones.Trim();

        if (dto.IdEstadoTratamiento.HasValue && dto.IdEstadoTratamiento.Value > 0)
            tratamiento.IdEstadoTratamiento = dto.IdEstadoTratamiento.Value;

        // Si se enviaron horarios nuevos, regenerar los horarios futuros
        if (dto.Horarios != null && dto.Horarios.Count > 0)
        {
            var ahoraUtc = DateTime.UtcNow;
            var horariosFuturos = tratamiento.Horarios
                .Where(h => h.Dia >= ahoraUtc.Date)
                .ToList();

            _context.Horarios.RemoveRange(horariosFuturos);

            var nuevosHorarios = GenerarHorarios(tratamiento, dto.Horarios);
            if (nuevosHorarios.Any())
            {
                _context.Horarios.AddRange(nuevosHorarios);
            }
        }

        await _context.SaveChangesAsync();

        await _context.Entry(tratamiento).Reference(t => t.UnidadMedida).LoadAsync();
        await _context.Entry(tratamiento).Collection(t => t.Horarios).LoadAsync();

        return Ok(new
        {
            exito = true,
            mensaje = "Tratamiento actualizado exitosamente.",
            tratamiento = MapearARespuestaDto(tratamiento, dto.Horarios, dto.Presentacion)
        });
    }

    // ========================================================================
    // 5. BORRADO LÓGICO (SOFT DELETE) DE UN TRATAMIENTO
    // Endpoint: DELETE /api/tratamientos/{id}
    // ========================================================================
    [HttpDelete("{id}")]
    public async Task<IActionResult> EliminarTratamiento(int id)
    {
        var tratamiento = await _context.Tratamientos.FindAsync(id);
        if (tratamiento == null)
            return NotFound(new { exito = false, mensaje = "Tratamiento no encontrado." });

        // Borrado lógico: se cambia a Estado 2 (Finalizado)
        // preservando el histórico médico y tomas pasadas
        tratamiento.IdEstadoTratamiento = 2; // Finalizado
        await _context.SaveChangesAsync();

        return Ok(new
        {
            exito = true,
            mensaje = "Tratamiento finalizado correctamente. Su historial y tomas registradas se mantienen seguros."
        });
    }

    // ========================================================================
    // MÉTODOS AUXILIARES
    // ========================================================================

    private async Task<int> ResolverUnidadMedidaAsync(int? idUnidad, string? nombreUnidad, string? presentacion)
    {
        if (idUnidad.HasValue && idUnidad.Value > 0)
        {
            var existe = await _context.UnidadesMedida.AnyAsync(u => u.IdUnidadMedida == idUnidad.Value);
            if (existe) return idUnidad.Value;
        }

        var texto = $"{nombreUnidad} {presentacion}".ToLowerInvariant();
        if (texto.Contains("mg") || texto.Contains("mili")) return 2; // Miligramo
        if (texto.Contains("ml")) return 3; // Mililitro
        if (texto.Contains("cap") || texto.Contains("cápsula")) return 4; // Cápsula
        if (texto.Contains("got")) return 5; // Gota
        if (texto.Contains("tab") || texto.Contains("comp")) return 1; // Tableta

        return 1; // Default: Tableta
    }

    private static TimeSpan ParseFrecuencia(string? frecuenciaTexto, int cantidadHorarios)
    {
        if (!string.IsNullOrWhiteSpace(frecuenciaTexto))
        {
            if (TimeSpan.TryParse(frecuenciaTexto, out var ts))
                return ts;

            var match = Regex.Match(frecuenciaTexto, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var horas) && horas > 0)
            {
                return TimeSpan.FromHours(horas);
            }
        }

        if (cantidadHorarios > 0)
        {
            var horasCalculadas = Math.Max(1, 24 / cantidadHorarios);
            return TimeSpan.FromHours(horasCalculadas);
        }

        return TimeSpan.FromHours(8); // Por defecto cada 8 horas
    }

    private static List<Horario> GenerarHorarios(Tratamiento tratamiento, List<string>? listaHoras)
    {
        var horasParsed = new List<TimeOnly>();

        if (listaHoras != null && listaHoras.Count > 0)
        {
            foreach (var h in listaHoras)
            {
                if (TryParseHora(h, out var time))
                {
                    horasParsed.Add(time);
                }
            }
        }

        if (!horasParsed.Any())
        {
            horasParsed.Add(new TimeOnly(8, 0)); // 08:00 AM por defecto
        }

        // Horizonte de generación de tomas
        // Si no tiene fecha final, generamos un lote inicial de 14 días
        var fechaInicio = tratamiento.FechaInicio;
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var inicio = fechaInicio < hoy ? hoy : fechaInicio;

        DateOnly fechaFin;
        if (tratamiento.FechaFinal.HasValue)
        {
            // Limitar a máximo 60 días para proteger la base de datos
            var diasTotal = tratamiento.FechaFinal.Value.DayNumber - inicio.DayNumber;
            fechaFin = diasTotal > 60 ? inicio.AddDays(60) : tratamiento.FechaFinal.Value;
        }
        else
        {
            fechaFin = inicio.AddDays(14); // 14 días programados
        }

        var resultado = new List<Horario>();

        for (var diaActual = inicio; diaActual <= fechaFin; diaActual = diaActual.AddDays(1))
        {
            foreach (var hora in horasParsed)
            {
                var fechaHora = DateTime.SpecifyKind(diaActual.ToDateTime(hora), DateTimeKind.Utc);
                resultado.Add(new Horario
                {
                    IdTratamiento = tratamiento.IdTratamiento,
                    IdPaciente = tratamiento.IdPaciente,
                    IdUsuario = tratamiento.IdUsuario,
                    Dia = fechaHora
                });
            }
        }

        return resultado;
    }

    private static bool TryParseHora(string texto, out TimeOnly time)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            time = default;
            return false;
        }

        var formatos = new[]
        {
            "h:mm tt", "hh:mm tt", "H:mm", "HH:mm", "HH:mm:ss", "h:mm:ss tt"
        };

        if (TimeOnly.TryParseExact(texto.Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            return true;
        }

        return TimeOnly.TryParse(texto.Trim(), out time);
    }

    private static TratamientoRespuestaDto MapearARespuestaDto(
        Tratamiento t,
        List<string>? horasFallback = null,
        string? presentacionFallback = null)
    {
        // Extraer horas únicas del día de la colección de horarios
        var horasFormateadas = t.Horarios?
            .Select(h => h.Dia.ToString("h:mm tt", CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(h => h)
            .ToList() ?? new List<string>();

        if (!horasFormateadas.Any() && horasFallback != null && horasFallback.Any())
        {
            horasFormateadas = horasFallback;
        }

        var unidadNombre = t.UnidadMedida?.NombreUnidad ?? "Tableta";
        var unidadAbrev = t.UnidadMedida?.Abreviacion ?? unidadNombre;

        var horas = t.Frecuencia.TotalHours;
        var frecuenciaTexto = horas > 0
            ? (horas == 24 ? "Una vez al día" : $"Cada {(int)horas} horas")
            : "Cada 8 horas";

        return new TratamientoRespuestaDto
        {
            IdTratamiento = t.IdTratamiento,
            IdPaciente = t.IdPaciente,
            IdUsuario = t.IdUsuario,
            Medicamento = t.Medicamento,
            Cantidad = t.Cantidad,
            IdUnidadMedida = t.IdUnidadMedida,
            UnidadMedida = unidadAbrev,
            Frecuencia = frecuenciaTexto,
            FechaInicio = t.FechaInicio,
            FechaFinal = t.FechaFinal,
            Indicaciones = t.Indicaciones,
            IdEstadoTratamiento = t.IdEstadoTratamiento,
            Activo = t.IdEstadoTratamiento == 1,
            Horarios = horasFormateadas,
            Presentacion = presentacionFallback ?? unidadNombre
        };
    }
}

