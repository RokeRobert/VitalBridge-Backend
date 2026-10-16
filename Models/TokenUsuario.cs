using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("token_usuario")]
public class TokenUsuario
{
    [Key]
    [Column("token")]
    public string Token { get; set; } = string.Empty;

    [Required]
    [Column("id_usuario")]
    public Guid IdUsuario { get; set; }

    [Column("fecha_creacion")]
    public DateTime? FechaCreacion { get; set; }

    // Propiedad de navegación
    [ForeignKey(nameof(IdUsuario))]
    public Usuario? Usuario { get; set; }
}

