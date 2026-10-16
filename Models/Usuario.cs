using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("usuario")]
public class Usuario
{
    [Key]
    [Column("id_usuario")]
    public Guid IdUsuario { get; set; }

    [Column("foto")]
    [MaxLength(255)]
    public string? Foto { get; set; }

    [Required]
    [Column("nombre")]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [Column("apellidos")]
    [MaxLength(100)]
    public string Apellidos { get; set; } = string.Empty;

    [Required]
    [Column("correo")]
    [MaxLength(150)]
    [EmailAddress]
    public string Correo { get; set; } = string.Empty;

    [Required]
    [Column("telefono")]
    [MaxLength(20)]
    public string Telefono { get; set; } = string.Empty;

    [Required]
    [Column("contrasena")]
    [MaxLength(255)]
    public string Contrasena { get; set; } = string.Empty;

    [Required]
    [Column("id_estado_usuario")]
    public int IdEstadoUsuario { get; set; }

    [Required]
    [Column("fecha_registro")]
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    [ForeignKey(nameof(IdEstadoUsuario))]
    public EstadoUsuario? EstadoUsuario { get; set; }

    public ICollection<Paciente> Pacientes { get; set; } = new List<Paciente>();
    public ICollection<TokenUsuario> Tokens { get; set; } = new List<TokenUsuario>();
    public ICollection<Tratamiento> Tratamientos { get; set; } = new List<Tratamiento>();
    public ICollection<Horario> Horarios { get; set; } = new List<Horario>();
}

