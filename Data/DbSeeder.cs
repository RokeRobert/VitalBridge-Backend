using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        // 1. Estados de Usuario
        if (!context.EstadosUsuario.Any())
        {
            context.EstadosUsuario.AddRange(
                new EstadoUsuario { IdEstadoUsuario = 1, NombreEstado = "Activo" },
                new EstadoUsuario { IdEstadoUsuario = 2, NombreEstado = "Inactivo" }
            );
            await context.SaveChangesAsync();
        }

        // 2. Tipos de Paciente
        if (!context.TiposPaciente.Any())
        {
            context.TiposPaciente.AddRange(
                new TipoPaciente { IdTipoPaciente = 1, NombreTipo = "Adulto Mayor" },
                new TipoPaciente { IdTipoPaciente = 2, NombreTipo = "Paciente Crónico" },
                new TipoPaciente { IdTipoPaciente = 3, NombreTipo = "General" }
            );
            await context.SaveChangesAsync();
        }

        // 3. Estados de Alerta
        if (!context.EstadosAlerta.Any())
        {
            context.EstadosAlerta.AddRange(
                new EstadoAlerta { IdEstadoAlerta = 1, NombreEstado = "Activa" },
                new EstadoAlerta { IdEstadoAlerta = 2, NombreEstado = "Atendida" },
                new EstadoAlerta { IdEstadoAlerta = 3, NombreEstado = "Cancelada" }
            );
            await context.SaveChangesAsync();
        }

        // 4. Estados de Tratamiento
        if (!context.EstadosTratamiento.Any())
        {
            context.EstadosTratamiento.AddRange(
                new EstadoTratamiento { IdEstadoTratamiento = 1, NombreEstado = "Activo" },
                new EstadoTratamiento { IdEstadoTratamiento = 2, NombreEstado = "Finalizado" }
            );
            await context.SaveChangesAsync();
        }

        // 5. Estados de Toma
        if (!context.EstadosToma.Any())
        {
            context.EstadosToma.AddRange(
                new EstadoToma { IdEstadoToma = 1, NombreEstado = "Tomada" },
                new EstadoToma { IdEstadoToma = 2, NombreEstado = "Pendiente" },
                new EstadoToma { IdEstadoToma = 3, NombreEstado = "Omitida" }
            );
            await context.SaveChangesAsync();
        }

        // 6. Unidades de Medida
        if (!context.UnidadesMedida.Any())
        {
            context.UnidadesMedida.AddRange(
                new UnidadMedida { IdUnidadMedida = 1, NombreUnidad = "Tableta", Abreviacion = "tab" },
                new UnidadMedida { IdUnidadMedida = 2, NombreUnidad = "Miligramo", Abreviacion = "mg" },
                new UnidadMedida { IdUnidadMedida = 3, NombreUnidad = "Mililitro", Abreviacion = "ml" },
                new UnidadMedida { IdUnidadMedida = 4, NombreUnidad = "Cápsula", Abreviacion = "cap" },
                new UnidadMedida { IdUnidadMedida = 5, NombreUnidad = "Gota", Abreviacion = "gotas" }
            );
            await context.SaveChangesAsync();
        }

        // 7. Asegurar que el bucket 'perfiles' exista en Supabase Storage
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO storage.buckets (id, name, public) VALUES ('perfiles', 'perfiles', true) ON CONFLICT (id) DO NOTHING;");
        }
        catch { }
    }
}

