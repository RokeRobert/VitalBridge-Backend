using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("estado_notificacion")]
public class EstadoNotificacion
{
    [Key]
    [Column("id_estado_notificacion")]
    public int IdEstadoNotificacion { get; set; }

    [Required]
    [Column("nombre_estado")]
    [MaxLength(50)]
    public string NombreEstado { get; set; } = string.Empty;
}

