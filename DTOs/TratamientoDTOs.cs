using System.ComponentModel.DataAnnotations;

namespace VitalBridge_Backend.DTOs;

/// <summary>
/// DTO para registrar un nuevo tratamiento médico desde el frontend.
/// </summary>
public class CrearTratamientoDto
{
    [Required(ErrorMessage = "El ID del paciente es obligatorio.")]
    public Guid IdPaciente { get; set; }

    public Guid? IdUsuario { get; set; }

    [Required(ErrorMessage = "El nombre del medicamento es obligatorio.")]
    [MaxLength(150, ErrorMessage = "El nombre del medicamento no puede exceder 150 caracteres.")]
    public string Medicamento { get; set; } = string.Empty;

    [Required(ErrorMessage = "La dosis o cantidad es obligatoria.")]
    [Range(0.01, 10000.0, ErrorMessage = "La cantidad debe ser mayor a 0.")]
    public decimal Cantidad { get; set; } = 1;

    public int? IdUnidadMedida { get; set; }

    public string? UnidadMedida { get; set; }

    public string? Frecuencia { get; set; }

    public DateOnly? FechaInicio { get; set; }

    public DateOnly? FechaFinal { get; set; }

    public string? Indicaciones { get; set; }

    /// <summary>
    /// Lista de horas programadas para las tomas (ej. ["08:00 AM", "08:00 PM"] o ["08:00", "20:00"]).
    /// </summary>
    public List<string> Horarios { get; set; } = new();

    public string? Presentacion { get; set; }
}

/// <summary>
/// DTO para actualizar los datos de un tratamiento existente.
/// </summary>
public class ActualizarTratamientoDto
{
    [MaxLength(150, ErrorMessage = "El nombre del medicamento no puede exceder 150 caracteres.")]
    public string? Medicamento { get; set; }

    [Range(0.01, 10000.0, ErrorMessage = "La cantidad debe ser mayor a 0.")]
    public decimal? Cantidad { get; set; }

    public int? IdUnidadMedida { get; set; }

    public string? UnidadMedida { get; set; }

    public string? Frecuencia { get; set; }

    public DateOnly? FechaInicio { get; set; }

    public DateOnly? FechaFinal { get; set; }

    public string? Indicaciones { get; set; }

    public List<string>? Horarios { get; set; }

    public string? Presentacion { get; set; }

    public int? IdEstadoTratamiento { get; set; }
}

/// <summary>
/// DTO de respuesta estructurada para Flutter y clientes de la API.
/// </summary>
public class TratamientoRespuestaDto
{
    public int IdTratamiento { get; set; }
    public Guid IdPaciente { get; set; }
    public Guid IdUsuario { get; set; }
    public string Medicamento { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public int IdUnidadMedida { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
    public string Frecuencia { get; set; } = string.Empty;
    public DateOnly FechaInicio { get; set; }
    public DateOnly? FechaFinal { get; set; }
    public string? Indicaciones { get; set; }
    public int IdEstadoTratamiento { get; set; }
    public bool Activo { get; set; }
    public List<string> Horarios { get; set; } = new();
    public string Presentacion { get; set; } = "Tableta";
}

