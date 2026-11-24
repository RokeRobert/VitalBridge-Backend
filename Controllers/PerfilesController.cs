// ============================================================================
// CONTROLADOR DE PERFILES Y FOTOS DE PERFIL (PerfilesController.cs)
// ============================================================================
// Este controlador gestiona:
// 1. Subida de fotos de perfil hacia Supabase Storage y actualización en PostgreSQL.
// 2. Consulta de datos de perfil del cuidador.
// 3. Edición de datos personales del cuidador (Nombre, Apellidos, Teléfono).
// ============================================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.Extensions;

namespace VitalBridge_Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")] // Ruta base: /api/perfiles
public class PerfilesController : ControllerBase
{
    private readonly ApplicationDbContext _context;          // Acceso a la base de datos PostgreSQL
    private readonly IHttpClientFactory _httpClientFactory;  // Para llamadas HTTP salientes a Supabase Storage
    private readonly IConfiguration _configuration;          // Lectura de URL y Claves de Supabase

    public PerfilesController(
        ApplicationDbContext context,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    // ========================================================================
    // 1. SUBIDA DE FOTO DE PERFIL (Cuidador o Paciente)
    // Endpoint: POST /api/perfiles/foto
    // ========================================================================
    [HttpPost("foto")]
    public async Task<IActionResult> SubirFotoPerfil(
        [FromForm] IFormFile? archivo, // Archivo binario enviado por la app móvil
        [FromForm] string tipo,        // Identificador: "usuario" o "paciente"
        [FromForm] Guid id)            // UUID del usuario o del paciente
    {
        // Validación: Asegurar que el archivo no venga vacío
        if (archivo == null || archivo.Length == 0)
        {
            return BadRequest(new { exito = false, mensaje = "Debe enviar un archivo de imagen válido." });
        }

        var tipoLimpio = tipo.Trim().ToLower();
        if (tipoLimpio != "usuario" && tipoLimpio != "paciente")
        {
            return BadRequest(new { exito = false, mensaje = "El tipo debe ser 'usuario' o 'paciente'." });
        }

        var supabaseUrl = _configuration["Supabase:Url"]?.TrimEnd('/');
        var supabaseAnonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseAnonKey))
        {
            return StatusCode(500, new { exito = false, mensaje = "La configuración de Supabase no está completa." });
        }

        try
        {
            var rutaObjeto = $"{tipoLimpio}s/{id}.jpg";
            var urlSubida = $"{supabaseUrl}/storage/v1/object/perfiles/{rutaObjeto}";

            var client = _httpClientFactory.CreateClient();

            using var stream = archivo.OpenReadStream();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(archivo.ContentType ?? "image/jpeg");

            var request = new HttpRequestMessage(HttpMethod.Post, urlSubida);
            request.Headers.Add("apikey", supabaseAnonKey);
            request.Headers.Add("Authorization", $"Bearer {supabaseAnonKey}");
            request.Headers.Add("x-upsert", "true");
            request.Content = content;

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new
                {
                    exito = false,
                    mensaje = "Error al subir la imagen a Supabase Storage.",
                    detalle = errorBody
                });
            }

            var publicUrl = $"{supabaseUrl}/storage/v1/object/public/perfiles/{rutaObjeto}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            if (tipoLimpio == "usuario")
            {
                var usuario = await _context.Usuarios.FindAsync(id);
                if (usuario != null)
                {
                    usuario.Foto = publicUrl;
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                var paciente = await _context.Pacientes.FindAsync(id);
                if (paciente != null)
                {
                    paciente.FotoPaciente = publicUrl;
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new
            {
                exito = true,
                mensaje = "Foto de perfil actualizada correctamente en Supabase Storage.",
                urlFoto = publicUrl
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Error al procesar la foto de perfil.",
                detalle = ex.Message
            });
        }
    }

    // ========================================================================
    // 2. CONSULTAR PERFIL DEL CUIDADOR
    // Endpoint: GET /api/perfiles/usuario/{id}
    // ========================================================================
    [HttpGet("usuario/{id}")]
    public async Task<IActionResult> ObtenerPerfilUsuario(Guid id)
    {
        var targetId = User.ObtenerUsuarioId(Request) ?? id;
        var usuario = await _context.Usuarios.FindAsync(targetId);
        if (usuario == null)
            return NotFound(new { exito = false, mensaje = "Usuario no encontrado." });

        return Ok(new
        {
            exito = true,
            idUsuario = usuario.IdUsuario,
            nombre = usuario.Nombre,
            apellidos = usuario.Apellidos,
            correo = usuario.Correo,
            telefono = usuario.Telefono,
            foto = usuario.Foto,
            fechaRegistro = usuario.FechaRegistro
        });
    }

    // ========================================================================
    // 3. ACTUALIZAR DATOS PERSONALES DEL CUIDADOR
    // Endpoint: PUT /api/perfiles/usuario/{id}
    // ========================================================================
    [HttpPut("usuario/{id}")]
    public async Task<IActionResult> ActualizarPerfilUsuario(Guid id, [FromBody] ActualizarUsuarioDto dto)
    {
        var targetId = User.ObtenerUsuarioId(Request) ?? id;
        var usuario = await _context.Usuarios.FindAsync(targetId);
        if (usuario == null)
            return NotFound(new { exito = false, mensaje = "Usuario no encontrado." });

        if (!string.IsNullOrWhiteSpace(dto.Nombre)) usuario.Nombre = dto.Nombre.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Apellidos)) usuario.Apellidos = dto.Apellidos.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Telefono)) usuario.Telefono = dto.Telefono.Trim();

        await _context.SaveChangesAsync();

        return Ok(new
        {
            exito = true,
            mensaje = "Datos de perfil actualizados exitosamente."
        });
    }
}

public class ActualizarUsuarioDto
{
    public string? Nombre { get; set; }
    public string? Apellidos { get; set; }
    public string? Telefono { get; set; }
}
