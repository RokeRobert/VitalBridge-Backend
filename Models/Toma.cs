using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("toma")]
public class Toma
{
    [Key]
    [Column("id_horario")]
    public int IdHorario { get; set; }

    [Required]
    [Column("id_estado_toma")]
    public int IdEstadoToma { get; set; }

    [Required]
    [Column("hora_tomada")]
    public DateTime HoraTomada { get; set; }

    [Column("observaciones")]
    public string? Observaciones { get; set; }

    // Propiedades de navegación
    [ForeignKey(nameof(IdHorario))]
    public Horario? Horario { get; set; }

    [ForeignKey(nameof(IdEstadoToma))]
    public EstadoToma? EstadoToma { get; set; }
}

