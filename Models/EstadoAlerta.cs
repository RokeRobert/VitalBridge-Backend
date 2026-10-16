using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("estado_alerta")]
public class EstadoAlerta
{
    [Key]
    [Column("id_estado_alerta")]
    public int IdEstadoAlerta { get; set; }

    [Required]
    [Column("nombre_estado")]
    [MaxLength(50)]
    public string NombreEstado { get; set; } = string.Empty;

    // Relaciones
    public ICollection<Alerta> Alertas { get; set; } = new List<Alerta>();
}

