// ============================================================================
// CONTEXTO DE BASE DE DATOS PRINCIPAL (ApplicationDbContext.cs)
// ============================================================================
// Esta clase hereda de DbContext de Entity Framework Core.
// Actúa como la sesión y puente entre nuestras clases de C# y las tablas
// de PostgreSQL alojadas en Supabase.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Data;

public class ApplicationDbContext : DbContext
{
    // Constructor: Recibe las opciones de configuración (como la cadena de conexión) desde Program.cs
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // ========================================================================
    // TABLAS REPRESENTADAS COMO DBSETS
    // Cada DbSet<T> equivale a una tabla física en PostgreSQL.
    // Con esto podemos hacer: await _context.Usuarios.ToListAsync()
    // ========================================================================
    public DbSet<EstadoUsuario> EstadosUsuario => Set<EstadoUsuario>();         // Catálogo: Activo, Inactivo, Bloqueado
    public DbSet<Usuario> Usuarios => Set<Usuario>();                           // Cuidadores registrados
    public DbSet<TokenUsuario> TokensUsuario => Set<TokenUsuario>();             // Tokens FCM para notificaciones push
    public DbSet<TipoPaciente> TiposPaciente => Set<TipoPaciente>();             // Catálogo: Adulto Mayor, Discapacidad, etc.
    public DbSet<Paciente> Pacientes => Set<Paciente>();                         // Pacientes a cargo del cuidador
    public DbSet<TokenPaciente> TokensPaciente => Set<TokenPaciente>();         // Códigos alfanuméricos de vinculación (QR)
    public DbSet<EstadoAlerta> EstadosAlerta => Set<EstadoAlerta>();             // Catálogo: Pendiente, Atendida, Cancelada
    public DbSet<Alerta> Alertas => Set<Alerta>();                               // Alertas de SOS y emergencias
    public DbSet<EstadoTratamiento> EstadosTratamiento => Set<EstadoTratamiento>(); // Catálogo: En curso, Pausado, Finalizado
    public DbSet<UnidadMedida> UnidadesMedida => Set<UnidadMedida>();           // Catálogo: mg, ml, pastilla, gotas
    public DbSet<Tratamiento> Tratamientos => Set<Tratamiento>();               // Medicamentos y dosis recetadas
    public DbSet<Horario> Horarios => Set<Horario>();                           // Horas programadas para cada toma
    public DbSet<EstadoToma> EstadosToma => Set<EstadoToma>();                   // Catálogo: Pendiente, Tomada, Omitida
    public DbSet<Toma> Tomas => Set<Toma>();                                     // Registro real de si se tomó la medicina
    public DbSet<EstadoNotificacion> EstadosNotificacion => Set<EstadoNotificacion>(); // Historial de avisos enviados

    // ========================================================================
    // CONFIGURACIÓN DE RELACIONES Y REGLAS (Fluent API)
    // ========================================================================
    // Aquí definimos relaciones complejas, claves foráneas, borrado en cascada
    // y restricciones que van más allá de lo que permiten los atributos simples.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Relación 1 a 1 entre Horario y Toma
        // A cada horario de toma le corresponde un registro de confirmación de toma.
        modelBuilder.Entity<Toma>()
            .HasOne(t => t.Horario)
            .WithOne(h => h.Toma)
            .HasForeignKey<Toma>(t => t.IdHorario)
            .OnDelete(DeleteBehavior.Cascade); // Si se elimina el horario, se elimina su registro de toma asociado

        // 2. Precisión decimal para la dosis del medicamento (ej. 1.5 pastillas o 250.00 mg)
        modelBuilder.Entity<Tratamiento>()
            .Property(t => t.Cantidad)
            .HasPrecision(18, 2);

        // 3. Relación Paciente -> Tratamientos
        // Si se borra un paciente, sus tratamientos se eliminan en cascada.
        modelBuilder.Entity<Tratamiento>()
            .HasOne(t => t.Paciente)
            .WithMany(p => p.Tratamientos)
            .HasForeignKey(t => t.IdPaciente)
            .OnDelete(DeleteBehavior.Cascade);

        // 4. Relación Cuidador (Usuario) -> Tratamientos
        // Se usa DeleteBehavior.Restrict para evitar ciclos de eliminación múltiple (Multiple Cascade Paths).
        modelBuilder.Entity<Tratamiento>()
            .HasOne(t => t.Usuario)
            .WithMany(u => u.Tratamientos)
            .HasForeignKey(t => t.IdUsuario)
            .OnDelete(DeleteBehavior.Restrict);

        // 5. Configuración de claves foráneas en la tabla Horario
        modelBuilder.Entity<Horario>()
            .HasOne(h => h.Tratamiento)
            .WithMany(t => t.Horarios)
            .HasForeignKey(h => h.IdTratamiento)
            .OnDelete(DeleteBehavior.Cascade); // Si se borra el tratamiento, se borran sus horarios

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

        // 6. Índice único para el correo electrónico del cuidador
        // Garantiza a nivel de base de datos que dos usuarios nunca tengan el mismo correo.
        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.Correo)
            .IsUnique();
    }
}