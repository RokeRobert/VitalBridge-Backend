using System.ComponentModel.DataAnnotations;

namespace VitalBridge_Backend.DTOs;

public class TomaRespuestaDto
{
    public int IdHorario { get; set; }
    public int IdTratamiento { get; set; }
    public string Medicamento { get; set; } = string.Empty;
    public string Dosis { get; set; } = string.Empty;
    public string HoraProgramada { get; set; } = string.Empty;
    public DateTime? HoraTomada { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public string? Observaciones { get; set; }
    public string? Indicaciones { get; set; }
}

public class ConfirmarTomaDto
{
    [Required(ErrorMessage = "El identificador del horario es obligatorio.")]
    public int IdHorario { get; set; }

    public string? Observaciones { get; set; }

    public DateTime? HoraTomada { get; set; }
}

