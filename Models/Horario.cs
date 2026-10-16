using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("horario")]
public class Horario
{
    [Key]
    [Column("id_horario")]
    public int IdHorario { get; set; }

    [Required]
    [Column("id_tratamiento")]
    public int IdTratamiento { get; set; }

    [Required]
    [Column("id_paciente")]
    public Guid IdPaciente { get; set; }

    [Required]
    [Column("id_usuario")]
    public Guid IdUsuario { get; set; }

    [Required]
    [Column("dia")]
    public DateTime Dia { get; set; }

    // Propiedades de navegación
    [ForeignKey(nameof(IdTratamiento))]
    public Tratamiento? Tratamiento { get; set; }

    [ForeignKey(nameof(IdPaciente))]
    public Paciente? Paciente { get; set; }

    [ForeignKey(nameof(IdUsuario))]
    public Usuario? Usuario { get; set; }

    public Toma? Toma { get; set; }
}

