using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VitalBridge_Backend.Models;

[Table("alerta_toma_omitida")]
public class AlertaTomaOmitida
{
    [Key]
    [Column("id_horario")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int IdHorario { get; set; }

    [Column("fecha_notificacion")]
    public DateTime FechaNotificacion { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(IdHorario))]
    public Horario? Horario { get; set; }
}
