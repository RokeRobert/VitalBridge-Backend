// ============================================================================
// CONTROLADOR DE GESTIÓN DE PACIENTES (PacientesController.cs)
// ============================================================================
// Este controlador administra el ciclo de vida de los pacientes asignados a un cuidador:
// 1. Consulta de pacientes activos (GET /api/pacientes).
// 2. Registro de nuevos pacientes con generación de token único seguro (POST /api/pacientes).
// 3. Edición de datos del paciente (PUT /api/pacientes/{id}).
// 4. Baja lógica / Soft Delete de pacientes (DELETE /api/pacientes/{id}).
// ============================================================================

using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.DTOs;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Controllers;

[ApiController]
[Route("api/[controller]")] // Ruta base: /api/pacientes
public class PacientesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    // Alfabeto Base32 humanamente legible:
    // Excluye intencionalmente números y letras ambiguas (como el 0 y la O, o el 1 y la I)
    // para que adultos mayores o cuidadores no tengan problemas al teclear el código.
    private const string AlfabetoSeguro = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private static readonly Guid DummyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public PacientesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // ========================================================================
    // 1. OBTENER PACIENTES DEL CUIDADOR
    // Endpoint: GET /api/pacientes
    // ========================================================================
    // Soporta recibir el ID del cuidador ya sea por cabecera HTTP (X-User-Id) o Query Parameter (?idUsuario=...)
    [HttpGet]
    public async Task<IActionResult> ObtenerPacientes([FromHeader(Name = "X-User-Id")] string? userIdHeader, [FromQuery] Guid? idUsuario)
    {
        Guid? cuidadorId = idUsuario;
        if (!cuidadorId.HasValue && Guid.TryParse(userIdHeader, out var parsedId))
        {
            cuidadorId = parsedId;
        }

        // Construcción de la consulta con Entity Framework Core:
        // - Include: Trae la información relacionada del catálogo 'TipoPaciente' y los 'Tokens'.
        // - Where: Filtra solo pacientes activos (IdEstadoPaciente == 1), ignorando los dados de baja.
        var query = _context.Pacientes
            .Include(p => p.TipoPaciente)
            .Include(p => p.Tokens)
            .Where(p => p.IdEstadoPaciente == 1);

        if (cuidadorId.HasValue && cuidadorId.Value != Guid.Empty && cuidadorId.Value != DummyId)
        {
            var existe = await _context.Usuarios.AnyAsync(u => u.IdUsuario == cuidadorId.Value);
            if (existe)
            {
                query = query.Where(p => p.IdUsuario == cuidadorId.Value);
            }
        }

        var pacientes = await query
            .OrderByDescending(p => p.FechaRegistro)
            .ToListAsync();

        // Mapeo manual a DTO para enviar a Flutter solo la información necesaria
        var resultado = pacientes.Select(p => new PacienteRespuestaDto
        {
            IdPaciente = p.IdPaciente,
            NombrePaciente = p.NombrePaciente,
            ApellidosPacientes = p.ApellidosPacientes,
            Telefono = p.Telefono,
            FotoPaciente = p.FotoPaciente,
            IdTipoPaciente = p.IdTipoPaciente,
            TipoPaciente = p.TipoPaciente?.NombreTipo ?? "Adulto Mayor",
            IdUsuario = p.IdUsuario,
            Token = p.Tokens.OrderByDescending(t => t.FechaCreacion).FirstOrDefault()?.Token ?? string.Empty,
            Parentesco = p.Parentesco ?? "Familiar"
        }).ToList();

        return Ok(resultado);
    }

    // ========================================================================
    // 2. REGISTRAR NUEVO PACIENTE Y GENERAR TOKEN DE VINCULACIÓN
    // Endpoint: POST /api/pacientes
    // ========================================================================
    [HttpPost]
    public async Task<IActionResult> RegistrarPaciente(
        [FromBody] CrearPacienteDto dto,
        [FromHeader(Name = "X-User-Id")] string? userIdHeader)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        Guid idUsuario = dto.IdUsuario ?? Guid.Empty;
        if (idUsuario == Guid.Empty && Guid.TryParse(userIdHeader, out var parsedId))
        {
            idUsuario = parsedId;
        }

        if (idUsuario == Guid.Empty || idUsuario == DummyId)
        {
            var primerUsuario = await _context.Usuarios.FirstOrDefaultAsync();
            if (primerUsuario != null)
                idUsuario = primerUsuario.IdUsuario;
            else
                return BadRequest(new { exito = false, mensaje = "Debe especificar un cuidador válido (IdUsuario)." });
        }

        // PASO 1: Generación de Token con Garantía Anti-Colisión (Garantiza unicidad en la base de datos)
        string tokenSeguro;
        do
        {
            tokenSeguro = GenerarTokenSeguro();
        }
        while (await _context.TokensPaciente.AnyAsync(t => t.Token == tokenSeguro));

        // PASO 2: Instanciar la entidad Paciente
        var paciente = new Paciente
        {
            IdPaciente = Guid.NewGuid(),
            NombrePaciente = dto.NombrePaciente.Trim(),
            ApellidosPacientes = dto.ApellidosPacientes.Trim(),
            Telefono = dto.Telefono.Trim(),
            IdTipoPaciente = dto.IdTipoPaciente > 0 ? dto.IdTipoPaciente : 1,
            IdUsuario = idUsuario,
            IdEstadoPaciente = 1, // 1 = Activo
            Parentesco = string.IsNullOrWhiteSpace(dto.Parentesco) ? "Familiar" : dto.Parentesco.Trim(),
            FechaRegistro = DateTime.UtcNow
        };

        // PASO 3: Vincular el token criptográfico al paciente
        var tokenRegistro = new TokenPaciente
        {
            Token = tokenSeguro,
            IdPaciente = paciente.IdPaciente,
            FechaCreacion = DateTime.UtcNow
        };

        _context.Pacientes.Add(paciente);
        _context.TokensPaciente.Add(tokenRegistro);

        await _context.SaveChangesAsync();

        // Retorna HTTP 201 Created con el recurso recién creado
        return CreatedAtAction(nameof(ObtenerPacientes), new { id = paciente.IdPaciente }, new
        {
            exito = true,
            mensaje = "Paciente y token de vinculación creados exitosamente.",
            paciente = new PacienteRespuestaDto
            {
                IdPaciente = paciente.IdPaciente,
                NombrePaciente = paciente.NombrePaciente,
                ApellidosPacientes = paciente.ApellidosPacientes,
                Telefono = paciente.Telefono,
                FotoPaciente = paciente.FotoPaciente,
                IdTipoPaciente = paciente.IdTipoPaciente,
                IdUsuario = paciente.IdUsuario,
                Token = tokenSeguro,
                Parentesco = paciente.Parentesco
            },
            token = tokenSeguro
        });
    }

    // ========================================================================
    // 3. BORRADO LÓGICO (Soft Delete) DE PACIENTES
    // Endpoint: DELETE /api/pacientes/{id}
    // ========================================================================
    // En sistemas médicos no se debe borrar físicamente una fila (Hard Delete)
    // para no destruir el historial clínico ni los tratamientos pasados.
    // En su lugar, se cambia el estado a Inactivo (IdEstadoPaciente = 2).
    [HttpDelete("{id}")]
    public async Task<IActionResult> EliminarPaciente(Guid id)
    {
        var paciente = await _context.Pacientes.FindAsync(id);
        if (paciente == null)
            return NotFound(new { exito = false, mensaje = "Paciente no encontrado." });

        paciente.IdEstadoPaciente = 2; // Estado Inactivo
        await _context.SaveChangesAsync();

        return Ok(new
        {
            exito = true,
            mensaje = "Paciente archivado correctamente. Su historial médico se mantiene seguro."
        });
    }

    // ========================================================================
    // 4. ACTUALIZAR DATOS DEL PACIENTE
    // Endpoint: PUT /api/pacientes/{id}
    // ========================================================================
    [HttpPut("{id}")]
    public async Task<IActionResult> ActualizarPaciente(Guid id, [FromBody] ActualizarPacienteDto dto)
    {
        var paciente = await _context.Pacientes.FindAsync(id);
        if (paciente == null)
            return NotFound(new { exito = false, mensaje = "Paciente no encontrado." });

        if (!string.IsNullOrWhiteSpace(dto.NombrePaciente)) paciente.NombrePaciente = dto.NombrePaciente.Trim();
        if (!string.IsNullOrWhiteSpace(dto.ApellidosPacientes)) paciente.ApellidosPacientes = dto.ApellidosPacientes.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Telefono)) paciente.Telefono = dto.Telefono.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Parentesco)) paciente.Parentesco = dto.Parentesco.Trim();
        if (!string.IsNullOrWhiteSpace(dto.FotoPaciente)) paciente.FotoPaciente = dto.FotoPaciente.Trim();
        if (dto.IdTipoPaciente.HasValue && dto.IdTipoPaciente.Value > 0) paciente.IdTipoPaciente = dto.IdTipoPaciente.Value;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            exito = true,
            mensaje = "Datos del paciente actualizados exitosamente.",
            paciente = new PacienteRespuestaDto
            {
                IdPaciente = paciente.IdPaciente,
                NombrePaciente = paciente.NombrePaciente,
                ApellidosPacientes = paciente.ApellidosPacientes,
                Telefono = paciente.Telefono,
                FotoPaciente = paciente.FotoPaciente,
                IdTipoPaciente = paciente.IdTipoPaciente,
                IdUsuario = paciente.IdUsuario,
                Parentesco = paciente.Parentesco ?? "Familiar"
            }
        });
    }

    // ========================================================================
    // FUNCIÓN AUXILIAR: GENERADOR CRIPTOGRÁFICO DE TOKENS
    // Formato: VB-XXXX-XXXX (ejemplo: VB-A7K9-W3MZ)
    // ========================================================================
    // Usa RandomNumberGenerator (generador criptográficamente fuerte) en lugar
    // de new Random() para garantizar que los códigos no sean predecibles.
    // Espacio de combinaciones: 32^8 = 1,099,511,627,776 combinaciones posibles.
    private static string GenerarTokenSeguro()
    {
        var bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);

        var parte1 = new char[4];
        var parte2 = new char[4];

        for (int i = 0; i < 4; i++)
        {
            parte1[i] = AlfabetoSeguro[bytes[i] % AlfabetoSeguro.Length];
            parte2[i] = AlfabetoSeguro[bytes[i + 4] % AlfabetoSeguro.Length];
        }

        return $"VB-{new string(parte1)}-{new string(parte2)}";
    }
}
