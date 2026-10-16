using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("unidad_medida")]
public class UnidadMedida
{
    [Key]
    [Column("id_unidad_medida")]
    public int IdUnidadMedida { get; set; }

    [Column("nombre_unidad")]
    [MaxLength(50)]
    public string? NombreUnidad { get; set; }

    [Column("abreviacion")]
    [MaxLength(20)]
    public string? Abreviacion { get; set; }

    // Relaciones
    public ICollection<Tratamiento> Tratamientos { get; set; } = new List<Tratamiento>();
}

