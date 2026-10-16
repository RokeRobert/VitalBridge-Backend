using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("alerta")]
public class Alerta
{
    [Key]
    [Column("id_alerta")]
    public int IdAlerta { get; set; }

    [Required]
    [Column("id_paciente")]
    public Guid IdPaciente { get; set; }

    [Required]
    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    [Required]
    [Column("id_estado_alerta")]
    public int IdEstadoAlerta { get; set; }

    // Propiedades de navegación
    [ForeignKey(nameof(IdPaciente))]
    public Paciente? Paciente { get; set; }

    [ForeignKey(nameof(IdEstadoAlerta))]
    public EstadoAlerta? EstadoAlerta { get; set; }
}

