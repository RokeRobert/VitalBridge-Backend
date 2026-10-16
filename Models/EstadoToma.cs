using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("estado_toma")]
public class EstadoToma
{
    [Key]
    [Column("id_estado_toma")]
    public int IdEstadoToma { get; set; }

    [Required]
    [Column("nombre_estado")]
    [MaxLength(50)]
    public string NombreEstado { get; set; } = string.Empty;

    // Relaciones
    public ICollection<Toma> Tomas { get; set; } = new List<Toma>();
}

