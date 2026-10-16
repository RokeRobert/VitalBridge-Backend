using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("estado_usuario")]
public class EstadoUsuario
{
    [Key]
    [Column("id_estado_usuario")]
    public int IdEstadoUsuario { get; set; }

    [Required]
    [Column("nombre_estado")]
    [MaxLength(50)]
    public string NombreEstado { get; set; } = string.Empty;

    // Relaciones
    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}

