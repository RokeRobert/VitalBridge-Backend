using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("estado_tratamiento")]
public class EstadoTratamiento
{
    [Key]
    [Column("id_estado_tratamiento")]
    public int IdEstadoTratamiento { get; set; }

    [Required]
    [Column("nombre_estado")]
    [MaxLength(50)]
    public string NombreEstado { get; set; } = string.Empty;

    // Relaciones
    public ICollection<Tratamiento> Tratamientos { get; set; } = new List<Tratamiento>();
}