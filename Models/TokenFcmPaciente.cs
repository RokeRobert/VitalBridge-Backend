using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("token_fcm_paciente")]
public class TokenFcmPaciente
{
    [Key]
    [Column("token")]
    public string Token { get; set; } = string.Empty;

    [Required]
    [Column("id_paciente")]
    public Guid IdPaciente { get; set; }

    [Column("fecha_creacion")]
    public DateTime? FechaCreacion { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(IdPaciente))]
    public Paciente? Paciente { get; set; }
}
