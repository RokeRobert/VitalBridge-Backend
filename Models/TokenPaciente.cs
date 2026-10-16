using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("token_paciente")]
public class TokenPaciente
{
    [Key]
    [Column("token")]
    public string Token { get; set; } = string.Empty;

    [Required]
    [Column("id_paciente")]
    public Guid IdPaciente { get; set; }

    [Column("fecha_creacion")]
    public DateTime? FechaCreacion { get; set; }

    // Propiedad de navegación
    [ForeignKey(nameof(IdPaciente))]
    public Paciente? Paciente { get; set; }
}

