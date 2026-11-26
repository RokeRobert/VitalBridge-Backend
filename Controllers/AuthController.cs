// ============================================================================
// CONTROLADOR DE AUTENTICACIÓN Y SEGURIDAD (AuthController.cs)
// ============================================================================
// Este controlador gestiona:
// 1. Registro de cuidadores (Crea el usuario en Supabase Auth y en la tabla 'usuario').
// 2. Inicio de sesión de cuidadores (Valida credenciales y genera tokens JWT).
// 3. Inicio de sesión de pacientes (Vinculación simplificada mediante Token/código QR).
// ============================================================================

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;
using VitalBridge_Backend.DTOs;
using VitalBridge_Backend.Models;

namespace VitalBridge_Backend.Controllers;

// [ApiController]: Habilita comportamientos automáticos como validación de modelos HTTP y respuestas 400.
// [Route("api/[controller]")]: Define la ruta base reemplazando '[controller]' por el nombre de la clase sin 'Controller' -> /api/auth
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    // Variables privadas para Inyección de Dependencias
    private readonly ApplicationDbContext _context;          // Conexión a las tablas de PostgreSQL
    private readonly IHttpClientFactory _httpClientFactory;  // Fábrica para hacer llamadas HTTP a Supabase
    private readonly IConfiguration _configuration;          // Acceso a appsettings.json (claves, URLs)

    // Constructor: ASP.NET Core inyecta automáticamente estas dependencias al recibir una petición
    public AuthController(
        ApplicationDbContext context,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    // ========================================================================
    // 1. REGISTRO DE NUEVO CUIDADOR
    // Endpoint: POST /api/auth/registro
    // ========================================================================
    [HttpPost("registro")]
    public async Task<IActionResult> RegistrarCuidador([FromBody] RegistroCuidadorDto dto)
    {
        // Validación del DTO (campos requeridos, formato de correo, etc.)
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // PASO 1: Verificar si el correo ya existe en nuestra base de datos local
        var existeCorreo = await _context.Usuarios
            .AnyAsync(u => u.Correo.ToLower() == dto.Correo.Trim().ToLower());

        if (existeCorreo)
        {
            return BadRequest(new { exito = false, mensaje = "El correo electrónico ya está registrado." });
        }

        // Obtener configuración de Supabase desde appsettings.Development.json
        var supabaseUrl = _configuration["Supabase:Url"]?.TrimEnd('/');
        var supabaseAnonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseAnonKey))
        {
            return StatusCode(500, new { exito = false, mensaje = "La configuración de Supabase no está completa." });
        }

        try
        {
            // PASO 2: Enviar solicitud de registro al módulo de seguridad de Supabase (auth.users)
            var client = _httpClientFactory.CreateClient();
            var signupUrl = $"{supabaseUrl}/auth/v1/signup";

            var signupPayload = new
            {
                email = dto.Correo.Trim(),
                password = dto.Contrasena,
                data = new
                {
                    nombre = dto.Nombre.Trim(),
                    apellidos = dto.Apellidos.Trim(),
                    telefono = dto.Telefono.Trim()
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, signupUrl);
            request.Headers.Add("apikey", supabaseAnonKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(signupPayload),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            // Si Supabase rechaza el registro (ej. contraseña muy débil), devolvemos el error al móvil
            if (!response.IsSuccessStatusCode)
            {
                var errorObj = JsonDocument.Parse(responseBody);
                var errorMsg = errorObj.RootElement.TryGetProperty("msg", out var msgProp)
                    ? msgProp.GetString()
                    : (errorObj.RootElement.TryGetProperty("error_description", out var descProp)
                        ? descProp.GetString()
                        : "Error al registrar en Supabase Auth.");

                return BadRequest(new { exito = false, mensaje = errorMsg });
            }

            // PASO 3: Extraer el UUID único generado por Supabase para este usuario
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string? userIdStr = null;
            if (root.TryGetProperty("id", out var idProp))
            {
                userIdStr = idProp.GetString();
            }
            else if (root.TryGetProperty("user", out var userProp) && userProp.TryGetProperty("id", out var subIdProp))
            {
                userIdStr = subIdProp.GetString();
            }

            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return StatusCode(500, new { exito = false, mensaje = "No se pudo obtener el identificador de usuario de Supabase." });
            }

            // PASO 4: Guardar el perfil en la tabla relacional 'public.usuario' de PostgreSQL
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == userId);
            if (usuario == null)
            {
                usuario = new Usuario
                {
                    IdUsuario = userId,
                    Nombre = dto.Nombre.Trim(),
                    Apellidos = dto.Apellidos.Trim(),
                    Correo = dto.Correo.Trim(),
                    Telefono = dto.Telefono.Trim(),
                    IdEstadoUsuario = 1, // 1 = Estado Activo
                    FechaRegistro = DateTime.UtcNow
                };

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();
            }

            // Extraer el token de acceso si fue generado
            string? token = null;
            if (root.TryGetProperty("access_token", out var tokenProp))
            {
                token = tokenProp.GetString();
            }

            bool requiereConfirmacion = string.IsNullOrEmpty(token);

            // PASO 5: Responder a Flutter con HTTP 200 OK y el DTO con datos del usuario
            return Ok(new
            {
                exito = true,
                mensaje = requiereConfirmacion
                    ? "Usuario registrado. Por favor revisa tu bandeja de entrada o spam para confirmar tu correo antes de iniciar sesión."
                    : "Usuario registrado exitosamente.",
                requiereConfirmacion,
                usuario = new UsuarioRespuestaDto
                {
                    IdUsuario = usuario.IdUsuario,
                    Nombre = usuario.Nombre,
                    Apellidos = usuario.Apellidos,
                    Correo = usuario.Correo,
                    Telefono = usuario.Telefono,
                    Foto = usuario.Foto,
                    Token = token
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Ocurrió un error en el servidor durante el registro.",
                detalle = ex.Message
            });
        }
    }

    // ========================================================================
    // 2. INICIO DE SESIÓN DE CUIDADOR (Email + Contraseña)
    // Endpoint: POST /api/auth/login
    // ========================================================================
    [HttpPost("login")]
    public async Task<IActionResult> LoginCuidador([FromBody] LoginCuidadorDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var supabaseUrl = _configuration["Supabase:Url"]?.TrimEnd('/');
        var supabaseAnonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseAnonKey))
        {
            return StatusCode(500, new { exito = false, mensaje = "La configuración de Supabase no está completa." });
        }

        try
        {
            // PASO 1: Validar credenciales contra el endpoint OAuth de Supabase Auth
            var client = _httpClientFactory.CreateClient();
            var loginUrl = $"{supabaseUrl}/auth/v1/token?grant_type=password";

            var loginPayload = new
            {
                email = dto.Correo.Trim(),
                password = dto.Contrasena
            };

            var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
            request.Headers.Add("apikey", supabaseAnonKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(loginPayload),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            // Si las credenciales son incorrectas, devolvemos HTTP 401 Unauthorized
            if (!response.IsSuccessStatusCode)
            {
                string mensajeError = "Correo o contraseña incorrectos.";
                try
                {
                    using var errDoc = JsonDocument.Parse(responseBody);
                    if (errDoc.RootElement.TryGetProperty("error_description", out var desc))
                    {
                        mensajeError = desc.GetString() ?? mensajeError;
                    }
                    else if (errDoc.RootElement.TryGetProperty("msg", out var msg))
                    {
                        mensajeError = msg.GetString() ?? mensajeError;
                    }
                }
                catch { }

                if (mensajeError.Contains("Email not confirmed", StringComparison.OrdinalIgnoreCase))
                {
                    mensajeError = "Tu correo electrónico no ha sido confirmado. Revisa tu bandeja de entrada o solicita un nuevo enlace de activación.";
                }

                return Unauthorized(new
                {
                    exito = false,
                    mensaje = mensajeError
                });
            }

            // PASO 2: Extraer el Access Token (JWT) y el ID del usuario
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var accessToken = root.GetProperty("access_token").GetString();
            var userIdStr = root.GetProperty("user").GetProperty("id").GetString();

            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return StatusCode(500, new { exito = false, mensaje = "Error al identificar al usuario." });
            }

            // PASO 3: Consultar los datos de perfil del cuidador en PostgreSQL
            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.IdUsuario == userId);

            // Si el registro aún no existiera en la tabla pública, lo creamos
            if (usuario == null)
            {
                usuario = new Usuario
                {
                    IdUsuario = userId,
                    Correo = dto.Correo.Trim(),
                    Nombre = "Cuidador",
                    Apellidos = "",
                    Telefono = "",
                    IdEstadoUsuario = 1,
                    FechaRegistro = DateTime.UtcNow
                };
                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();
            }

            // PASO 4: Devolver datos del cuidador a Flutter para persistirlos en SharedPreferences
            return Ok(new
            {
                exito = true,
                mensaje = "Inicio de sesión correcto.",
                usuario = new UsuarioRespuestaDto
                {
                    IdUsuario = usuario.IdUsuario,
                    Nombre = usuario.Nombre,
                    Apellidos = usuario.Apellidos,
                    Correo = usuario.Correo,
                    Telefono = usuario.Telefono,
                    Foto = usuario.Foto,
                    Token = accessToken
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Error al intentar iniciar sesión.",
                detalle = ex.Message
            });
        }
    }

    // ========================================================================
    // 3. INICIO DE SESIÓN DE PACIENTE (Mediante Código Token / QR)
    // Endpoint: POST /api/auth/paciente/token
    // ========================================================================
    // Diseñado especialmente para adultos mayores o personas con discapacidad:
    // en lugar de memorizar correos y contraseñas complejas, ingresan con un código
    // único de 6 caracteres generado por su cuidador.
    [HttpPost("paciente/token")]
    public async Task<IActionResult> LoginPacientePorToken([FromBody] LoginPacienteTokenDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var tokenBuscado = dto.Token.Trim().ToUpper();

        // Buscamos el token en la tabla 'token_paciente' e incluimos la entidad 'Paciente' (Eager Loading)
        var tokenRegistro = await _context.TokensPaciente
            .Include(t => t.Paciente)
            .FirstOrDefaultAsync(t => t.Token.ToUpper() == tokenBuscado);

        if (tokenRegistro == null || tokenRegistro.Paciente == null)
        {
            return NotFound(new
            {
                exito = false,
                mensaje = "Código de vinculación inválido o no encontrado."
            });
        }

        var paciente = tokenRegistro.Paciente;

        // Devolvemos la información del paciente para configurar la app en modo paciente
        return Ok(new
        {
            exito = true,
            mensaje = "Paciente vinculado exitosamente.",
            paciente = new PacienteRespuestaDto
            {
                IdPaciente = paciente.IdPaciente,
                NombrePaciente = paciente.NombrePaciente,
                ApellidosPacientes = paciente.ApellidosPacientes,
                Telefono = paciente.Telefono,
                FotoPaciente = paciente.FotoPaciente,
                Token = tokenRegistro.Token
            }
        });
    }

    // ========================================================================
    // 4. SOLICITUD DE RECUPERACIÓN DE CONTRASEÑA
    // Endpoint: POST /api/auth/recuperar-password
    // ========================================================================
    [HttpPost("recuperar-password")]
    public async Task<IActionResult> RecuperarPassword([FromBody] RecuperarPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var supabaseUrl = _configuration["Supabase:Url"]?.TrimEnd('/');
        var supabaseAnonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseAnonKey))
        {
            return StatusCode(500, new { exito = false, mensaje = "La configuración de Supabase no está completa." });
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var recoverUrl = $"{supabaseUrl}/auth/v1/recover";

            var payload = new
            {
                email = dto.Correo.Trim().ToLower()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, recoverUrl);
            request.Headers.Add("apikey", supabaseAnonKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string mensajeError = "No fue posible procesar la solicitud de recuperación.";
                try
                {
                    using var errDoc = JsonDocument.Parse(responseBody);
                    if (errDoc.RootElement.TryGetProperty("msg", out var m))
                        mensajeError = m.GetString() ?? mensajeError;
                    else if (errDoc.RootElement.TryGetProperty("error_description", out var ed))
                        mensajeError = ed.GetString() ?? mensajeError;
                }
                catch { }

                return BadRequest(new { exito = false, mensaje = mensajeError });
            }

            return Ok(new
            {
                exito = true,
                mensaje = "Se ha enviado un código de recuperación a tu correo electrónico."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Ocurrió un error al solicitar la recuperación de contraseña.",
                detalle = ex.Message
            });
        }
    }

    // ========================================================================
    // 5. RESTABLECER CONTRASEÑA MEDIANTE CÓDIGO OTP
    // Endpoint: POST /api/auth/restablecer-password
    // ========================================================================
    [HttpPost("restablecer-password")]
    public async Task<IActionResult> RestablecerPassword([FromBody] RestablecerPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var supabaseUrl = _configuration["Supabase:Url"]?.TrimEnd('/');
        var supabaseAnonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseAnonKey))
        {
            return StatusCode(500, new { exito = false, mensaje = "La configuración de Supabase no está completa." });
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            // PASO 1: Validar el código OTP contra Supabase Auth
            var verifyUrl = $"{supabaseUrl}/auth/v1/verify";
            var verifyPayload = new
            {
                type = "recovery",
                email = dto.Correo.Trim().ToLower(),
                token = dto.Codigo.Trim()
            };

            var verifyReq = new HttpRequestMessage(HttpMethod.Post, verifyUrl);
            verifyReq.Headers.Add("apikey", supabaseAnonKey);
            verifyReq.Content = new StringContent(
                JsonSerializer.Serialize(verifyPayload),
                Encoding.UTF8,
                "application/json");

            var verifyResp = await client.SendAsync(verifyReq);
            var verifyBody = await verifyResp.Content.ReadAsStringAsync();

            if (!verifyResp.IsSuccessStatusCode)
            {
                string mensajeError = "El código de verificación es incorrecto o ha expirado.";
                try
                {
                    using var errDoc = JsonDocument.Parse(verifyBody);
                    if (errDoc.RootElement.TryGetProperty("msg", out var m))
                        mensajeError = m.GetString() ?? mensajeError;
                    else if (errDoc.RootElement.TryGetProperty("error_description", out var ed))
                        mensajeError = ed.GetString() ?? mensajeError;
                }
                catch { }

                return BadRequest(new { exito = false, mensaje = mensajeError });
            }

            // PASO 2: Extraer el token de acceso temporal de recuperación
            using var doc = JsonDocument.Parse(verifyBody);
            string? tempToken = null;
            if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
            {
                tempToken = tokenProp.GetString();
            }

            if (string.IsNullOrEmpty(tempToken))
            {
                return BadRequest(new { exito = false, mensaje = "No se pudo validar la autorización de cambio de contraseña." });
            }

            // PASO 3: Actualizar la contraseña del usuario en Supabase Auth
            var updateUrl = $"{supabaseUrl}/auth/v1/user";
            var updatePayload = new
            {
                password = dto.NuevaContrasena
            };

            var updateReq = new HttpRequestMessage(HttpMethod.Put, updateUrl);
            updateReq.Headers.Add("apikey", supabaseAnonKey);
            updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tempToken);
            updateReq.Content = new StringContent(
                JsonSerializer.Serialize(updatePayload),
                Encoding.UTF8,
                "application/json");

            var updateResp = await client.SendAsync(updateReq);
            var updateBody = await updateResp.Content.ReadAsStringAsync();

            if (!updateResp.IsSuccessStatusCode)
            {
                string mensajeError = "No se pudo actualizar la contraseña. Verifica que cumpla con los requisitos mínimos de seguridad.";
                try
                {
                    using var errDoc = JsonDocument.Parse(updateBody);
                    if (errDoc.RootElement.TryGetProperty("msg", out var m))
                        mensajeError = m.GetString() ?? mensajeError;
                    else if (errDoc.RootElement.TryGetProperty("error_description", out var ed))
                        mensajeError = ed.GetString() ?? mensajeError;
                }
                catch { }

                return BadRequest(new { exito = false, mensaje = mensajeError });
            }

            return Ok(new
            {
                exito = true,
                mensaje = "Contraseña restablecida exitosamente. Ya puedes iniciar sesión con tu nueva contraseña."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Ocurrió un error al restablecer la contraseña.",
                detalle = ex.Message
            });
        }
    }

    // ========================================================================
    // 6. REENVIAR CORREO DE CONFIRMACIÓN
    // Endpoint: POST /api/auth/reenviar-confirmacion
    // ========================================================================
    [HttpPost("reenviar-confirmacion")]
    public async Task<IActionResult> ReenviarConfirmacion([FromBody] ReenviarConfirmacionDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var supabaseUrl = _configuration["Supabase:Url"]?.TrimEnd('/');
        var supabaseAnonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseAnonKey))
        {
            return StatusCode(500, new { exito = false, mensaje = "La configuración de Supabase no está completa." });
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var resendUrl = $"{supabaseUrl}/auth/v1/resend";

            var payload = new
            {
                type = "signup",
                email = dto.Correo.Trim().ToLower()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, resendUrl);
            request.Headers.Add("apikey", supabaseAnonKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string mensajeError = "No se pudo reenviar el correo de confirmación.";
                try
                {
                    using var errDoc = JsonDocument.Parse(responseBody);
                    if (errDoc.RootElement.TryGetProperty("msg", out var m))
                        mensajeError = m.GetString() ?? mensajeError;
                    else if (errDoc.RootElement.TryGetProperty("error_description", out var ed))
                        mensajeError = ed.GetString() ?? mensajeError;
                }
                catch { }

                return BadRequest(new { exito = false, mensaje = mensajeError });
            }

            return Ok(new
            {
                exito = true,
                mensaje = "Correo de confirmación reenviado exitosamente. Revisa tu bandeja de entrada o spam."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Ocurrió un error al reenviar el correo de confirmación.",
                detalle = ex.Message
            });
        }
    }
}
