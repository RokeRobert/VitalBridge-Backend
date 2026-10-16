using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("tipo_paciente")]
public class TipoPaciente
{
    [Key]
    [Column("id_tipo_paciente")]
    public int IdTipoPaciente { get; set; }

    [Column("nombre_tipo")]
    [MaxLength(50)]
    public string? NombreTipo { get; set; }

    // Relaciones
    public ICollection<Paciente> Pacientes { get; set; } = new List<Paciente>();
}

