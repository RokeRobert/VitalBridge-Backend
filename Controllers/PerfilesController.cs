// ============================================================================
// CONTROLADOR DE PERFILES Y FOTOS DE PERFIL (PerfilesController.cs)
// ============================================================================
// Este controlador gestiona:
// 1. Subida de fotos de perfil hacia Supabase Storage y actualización en PostgreSQL.
// 2. Consulta de datos de perfil del cuidador.
// 3. Edición de datos personales del cuidador (Nombre, Apellidos, Teléfono).
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;

namespace VitalBridge_Backend.Controllers;

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
    // Recibe la imagen mediante 'multipart/form-data'. La transmite a Supabase
    // Storage y guarda la URL pública en el campo 'foto' o 'foto_paciente'.
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
            // PASO 1: Definir la ruta del archivo dentro del bucket 'perfiles'
            // Ejemplo: "usuarios/1e8a6250-...jpg" o "pacientes/3f9b1234-...jpg"
            var rutaObjeto = $"{tipoLimpio}s/{id}.jpg";
            var urlSubida = $"{supabaseUrl}/storage/v1/object/perfiles/{rutaObjeto}";

            var client = _httpClientFactory.CreateClient();

            // PASO 2: Abrir flujo binario (Stream) de la imagen para transmitirla
            using var stream = archivo.OpenReadStream();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(archivo.ContentType ?? "image/jpeg");

            // PASO 3: Construir petición POST a la API de Supabase Storage
            var request = new HttpRequestMessage(HttpMethod.Post, urlSubida);
            request.Headers.Add("apikey", supabaseAnonKey);
            request.Headers.Add("Authorization", $"Bearer {supabaseAnonKey}");
            request.Headers.Add("x-upsert", "true"); // Sobrescribe la foto anterior si ya existía una
            request.Content = content;

            var response = await client.SendAsync(request);

            // Si Supabase Storage rechaza la subida, devolvemos el error exacto
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

            // PASO 4: Construir la URL pública permanente del archivo alojado en la nube
            var publicUrl = $"{supabaseUrl}/storage/v1/object/public/perfiles/{rutaObjeto}";

            // PASO 5: Actualizar la base de datos relacional (PostgreSQL) con la nueva URL
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

            // PASO 6: Responder al móvil con la URL pública para actualizar la vista de inmediato
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
        var usuario = await _context.Usuarios.FindAsync(id);
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
        var usuario = await _context.Usuarios.FindAsync(id);
        if (usuario == null)
            return NotFound(new { exito = false, mensaje = "Usuario no encontrado." });

        // Solo actualizamos los campos que el usuario haya modificado
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

// Objeto de Transferencia de Datos (DTO) para la actualización de perfil
public class ActualizarUsuarioDto
{
    public string? Nombre { get; set; }
    public string? Apellidos { get; set; }
    public string? Telefono { get; set; }
}
