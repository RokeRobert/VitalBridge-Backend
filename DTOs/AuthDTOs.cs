using System.ComponentModel.DataAnnotations;

namespace VitalBridge_Backend.DTOs;

public class LoginCuidadorDto
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El formato de correo es inválido.")]
    public string Correo { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    public string Contrasena { get; set; } = string.Empty;
}

public class RegistroCuidadorDto
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "Los apellidos son obligatorios.")]
    [MaxLength(100)]
    public string Apellidos { get; set; } = string.Empty;

    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El formato de correo es inválido.")]
    [MaxLength(150)]
    public string Correo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El teléfono es obligatorio.")]
    [MaxLength(20)]
    public string Telefono { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
    public string Contrasena { get; set; } = string.Empty;
}

public class LoginPacienteTokenDto
{
    [Required(ErrorMessage = "El token de vinculación es obligatorio.")]
    public string Token { get; set; } = string.Empty;
}

public class UsuarioRespuestaDto
{
    public Guid IdUsuario { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string? Foto { get; set; }
    public string? Token { get; set; }
}

public class PacienteRespuestaDto
{
    public Guid IdPaciente { get; set; }
    public string NombrePaciente { get; set; } = string.Empty;
    public string ApellidosPacientes { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string? FotoPaciente { get; set; }
    public int IdTipoPaciente { get; set; }
    public string? TipoPaciente { get; set; }
    public Guid IdUsuario { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? Parentesco { get; set; }
}

public class CrearPacienteDto
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [MaxLength(100)]
    public string NombrePaciente { get; set; } = string.Empty;

    [Required(ErrorMessage = "Los apellidos son obligatorios.")]
    [MaxLength(100)]
    public string ApellidosPacientes { get; set; } = string.Empty;

    [Required(ErrorMessage = "El teléfono es obligatorio.")]
    [MaxLength(20)]
    public string Telefono { get; set; } = string.Empty;

    public int IdTipoPaciente { get; set; } = 1;

    public Guid? IdUsuario { get; set; }

    public string? Parentesco { get; set; }
}

public class ActualizarPacienteDto
{
    public string? NombrePaciente { get; set; }
    public string? ApellidosPacientes { get; set; }
    public string? Telefono { get; set; }
    public int? IdTipoPaciente { get; set; }
    public string? Parentesco { get; set; }
    public string? FotoPaciente { get; set; }
}

