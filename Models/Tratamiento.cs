using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("tratamiento")]
public class Tratamiento
{
    [Key]
    [Column("id_tratamiento")]
    public int IdTratamiento { get; set; }

    [Required]
    [Column("id_paciente")]
    public Guid IdPaciente { get; set; }

    [Required]
    [Column("id_usuario")]
    public Guid IdUsuario { get; set; }

    [Required]
    [Column("medicamento")]
    [MaxLength(150)]
    public string Medicamento { get; set; } = string.Empty;

    [Required]
    [Column("cantidad", TypeName = "decimal(18,2)")]
    public decimal Cantidad { get; set; }

    [Required]
    [Column("id_unidad_medida")]
    public int IdUnidadMedida { get; set; }

    [Required]
    [Column("frecuencia")]
    public TimeSpan Frecuencia { get; set; }

    [Required]
    [Column("fecha_inicio")]
    public DateOnly FechaInicio { get; set; }

    [Column("fecha_final")]
    public DateOnly? FechaFinal { get; set; }

    [Column("indicaciones")]
    public string? Indicaciones { get; set; }

    [Required]
    [Column("id_estado_tratamiento")]
    public int IdEstadoTratamiento { get; set; }

    // Propiedades de navegación
    [ForeignKey(nameof(IdPaciente))]
    public Paciente? Paciente { get; set; }

    [ForeignKey(nameof(IdUsuario))]
    public Usuario? Usuario { get; set; }

    [ForeignKey(nameof(IdUnidadMedida))]
    public UnidadMedida? UnidadMedida { get; set; }

    [ForeignKey(nameof(IdEstadoTratamiento))]
    public EstadoTratamiento? EstadoTratamiento { get; set; }

    public ICollection<Horario> Horarios { get; set; } = new List<Horario>();
}

