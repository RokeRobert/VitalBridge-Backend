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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.DTOs;
using VitalBridge_Backend.Extensions;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")] // Ruta base: /api/pacientes
public class PacientesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    // Alfabeto Base32 humanamente legible:
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
    [HttpGet]
    public async Task<IActionResult> ObtenerPacientes(
        [FromHeader(Name = "X-User-Id")] string? userIdHeader,
        [FromQuery] Guid? idUsuario)
    {
        Guid? cuidadorId = User.ObtenerUsuarioId(Request) ?? idUsuario;

        if (!cuidadorId.HasValue || cuidadorId.Value == Guid.Empty || cuidadorId.Value == DummyId)
        {
            return Ok(new List<PacienteRespuestaDto>());
        }

        var query = _context.Pacientes
            .Include(p => p.TipoPaciente)
            .Include(p => p.Tokens)
            .Where(p => p.IdEstadoPaciente == 1 && p.IdUsuario == cuidadorId.Value);

        var pacientes = await query
            .OrderByDescending(p => p.FechaRegistro)
            .ToListAsync();

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

        Guid idUsuario = User.ObtenerUsuarioId(Request) ?? dto.IdUsuario ?? Guid.Empty;

        if (idUsuario == Guid.Empty || idUsuario == DummyId)
        {
            return BadRequest(new { exito = false, mensaje = "Debe especificar un cuidador válido (IdUsuario)." });
        }

        var cuidadorExiste = await _context.Usuarios.AnyAsync(u => u.IdUsuario == idUsuario);
        if (!cuidadorExiste)
        {
            return BadRequest(new { exito = false, mensaje = "El cuidador especificado no existe en el sistema." });
        }

        string tokenSeguro;
        do
        {
            tokenSeguro = GenerarTokenSeguro();
        }
        while (await _context.TokensPaciente.AnyAsync(t => t.Token == tokenSeguro));

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

        var tokenRegistro = new TokenPaciente
        {
            Token = tokenSeguro,
            IdPaciente = paciente.IdPaciente,
            FechaCreacion = DateTime.UtcNow
        };

        _context.Pacientes.Add(paciente);
        _context.TokensPaciente.Add(tokenRegistro);

        await _context.SaveChangesAsync();

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
