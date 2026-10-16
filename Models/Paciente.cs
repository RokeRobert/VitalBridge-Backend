using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("paciente")]
public class Paciente
{
    [Key]
    [Column("id_paciente")]
    public Guid IdPaciente { get; set; }

    [Column("foto_paciente")]
    [MaxLength(255)]
    public string? FotoPaciente { get; set; }

    [Required]
    [Column("nombre_paciente")]
    [MaxLength(100)]
    public string NombrePaciente { get; set; } = string.Empty;

    [Required]
    [Column("apellidos_pacientes")]
    [MaxLength(100)]
    public string ApellidosPacientes { get; set; } = string.Empty;

    [Required]
    [Column("id_tipo_paciente")]
    public int IdTipoPaciente { get; set; }

    [Required]
    [Column("telefono")]
    [MaxLength(20)]
    public string Telefono { get; set; } = string.Empty;

    [Required]
    [Column("id_usuario")]
    public Guid IdUsuario { get; set; }

    [Required]
    [Column("id_estado_paciente")]
    public int IdEstadoPaciente { get; set; }

    [Required]
    [Column("fecha_registro")]
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    [ForeignKey(nameof(IdTipoPaciente))]
    public TipoPaciente? TipoPaciente { get; set; }

    [ForeignKey(nameof(IdUsuario))]
    public Usuario? Usuario { get; set; }

    public ICollection<TokenPaciente> Tokens { get; set; } = new List<TokenPaciente>();
    public ICollection<Alerta> Alertas { get; set; } = new List<Alerta>();
    public ICollection<Tratamiento> Tratamientos { get; set; } = new List<Tratamiento>();
    public ICollection<Horario> Horarios { get; set; } = new List<Horario>();
}

