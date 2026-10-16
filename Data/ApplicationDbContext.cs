using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<EstadoUsuario> EstadosUsuario => Set<EstadoUsuario>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<TokenUsuario> TokensUsuario => Set<TokenUsuario>();
    public DbSet<TipoPaciente> TiposPaciente => Set<TipoPaciente>();
    public DbSet<Paciente> Pacientes => Set<Paciente>();
    public DbSet<TokenPaciente> TokensPaciente => Set<TokenPaciente>();
    public DbSet<EstadoAlerta> EstadosAlerta => Set<EstadoAlerta>();
    public DbSet<Alerta> Alertas => Set<Alerta>();
    public DbSet<EstadoTratamiento> EstadosTratamiento => Set<EstadoTratamiento>();
    public DbSet<UnidadMedida> UnidadesMedida => Set<UnidadMedida>();
    public DbSet<Tratamiento> Tratamientos => Set<Tratamiento>();
    public DbSet<Horario> Horarios => Set<Horario>();
    public DbSet<EstadoToma> EstadosToma => Set<EstadoToma>();
    public DbSet<Toma> Tomas => Set<Toma>();
    public DbSet<EstadoNotificacion> EstadosNotificacion => Set<EstadoNotificacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Relación 1 a 1 entre Horario y Toma
        modelBuilder.Entity<Toma>()
            .HasOne(t => t.Horario)
            .WithOne(h => h.Toma)
            .HasForeignKey<Toma>(t => t.IdHorario)
            .OnDelete(DeleteBehavior.Cascade);

        // Precisión numérica para Tratamiento.Cantidad
        modelBuilder.Entity<Tratamiento>()
            .Property(t => t.Cantidad)
            .HasPrecision(18, 2);

        // Evitar ciclos de eliminación en cascada en Tratamiento
        modelBuilder.Entity<Tratamiento>()
            .HasOne(t => t.Paciente)
            .WithMany(p => p.Tratamientos)
            .HasForeignKey(t => t.IdPaciente)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Tratamiento>()
            .HasOne(t => t.Usuario)
            .WithMany(u => u.Tratamientos)
            .HasForeignKey(t => t.IdUsuario)
            .OnDelete(DeleteBehavior.Restrict);

        // Evitar ciclos de eliminación en cascada en Horario
        modelBuilder.Entity<Horario>()
            .HasOne(h => h.Tratamiento)
            .WithMany(t => t.Horarios)
            .HasForeignKey(h => h.IdTratamiento)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Horario>()
            .HasOne(h => h.Paciente)
            .WithMany(p => p.Horarios)
            .HasForeignKey(h => h.IdPaciente)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Horario>()
            .HasOne(h => h.Usuario)
            .WithMany(u => u.Horarios)
            .HasForeignKey(h => h.IdUsuario)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices únicos recomendados
        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.Correo)
            .IsUnique();
    }
}