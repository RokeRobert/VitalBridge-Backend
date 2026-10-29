using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;

namespace VitalBridge_Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public HealthController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("db-check")]
    public async Task<IActionResult> CheckDatabase()
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
            }
            await conn.CloseAsync();

            return Ok(new
            {
                status = "OK",
                databaseConnected = true,
                message = "¡Conexión exitosa con la base de datos de Supabase!",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                status = "Error",
                databaseConnected = false,
                message = ex.Message,
                detail = ex.InnerException?.Message
            });
        }
    }

    [HttpGet("tables-check")]
    public async Task<IActionResult> CheckTables()
    {
        try
        {
            var totalUsuarios = await _context.Usuarios.CountAsync();
            var totalPacientes = await _context.Pacientes.CountAsync();
            var totalTratamientos = await _context.Tratamientos.CountAsync();

            return Ok(new
            {
                status = "OK",
                totalUsuarios,
                totalPacientes,
                totalTratamientos,
                message = "Las tablas existen y responden en Supabase."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                status = "Error al consultar tablas",
                message = ex.Message,
                detail = ex.InnerException?.Message
            });
        }
    }

    [HttpGet("debug")]
    public async Task<IActionResult> DebugInfo()
    {
        var usuarios = await _context.Usuarios
            .Select(u => new { u.IdUsuario, u.Correo, u.Nombre, u.Apellidos })
            .ToListAsync();

        var pacientes = await _context.Pacientes
            .Select(p => new { p.IdPaciente, p.NombrePaciente, p.IdUsuario, p.IdEstadoPaciente })
            .ToListAsync();

        return Ok(new { usuarios, pacientes });
    }
}
