// ============================================================================
// PUNTO DE ENTRADA PRINCIPAL DEL BACKEND (ASP.NET Core 9 / C#)
// ============================================================================
// Este archivo inicializa el servidor web, registra los servicios en el contenedor
// de Inyección de Dependencias (DI) y define el pipeline de procesamiento HTTP (Middlewares).
// ============================================================================

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VitalBridge_Backend.Data;

// 1. Inicialización del Constructor (Builder)
// Carga variables de entorno, argumentos de consola y archivos appsettings.json
var builder = WebApplication.CreateBuilder(args);

// 2. Obtención de la Cadena de Conexión a la Base de Datos (PostgreSQL en Supabase)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                    ?? builder.Configuration.GetConnectionString("SupabaseConnection");

// 3. Registro de Entity Framework Core (ORM)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// 4. Configuración de Autenticación con Token JWT (Supabase Auth)
var supabaseUrl = builder.Configuration["Supabase:Url"]?.TrimEnd('/');
var jwtSecret = builder.Configuration["Supabase:JwtSecret"]
             ?? builder.Configuration["Jwt:Secret"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;

    if (!string.IsNullOrEmpty(jwtSecret))
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            NameClaimType = ClaimTypes.NameIdentifier
        };
    }
    else
    {
        // En Render / producción sin JwtSecret explícito, decodificamos el JWT de Supabase Auth
        // extrayendo los claims del usuario sin fallar por discrepancia de firma con el AnonKey.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = false,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            SignatureValidator = (token, parameters) =>
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                return handler.ReadJwtToken(token);
            },
            NameClaimType = ClaimTypes.NameIdentifier
        };
    }

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Token = authHeader.Substring("Bearer ".Length).Trim();
            }
            else if (context.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader) &&
                     Guid.TryParse(userIdHeader, out var parsedUserId) &&
                     parsedUserId != Guid.Empty)
            {
                // Respaldo para clientes que envían identificación vía X-User-Id
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, parsedUserId.ToString()),
                    new Claim("sub", parsedUserId.ToString())
                };
                var identity = new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme);
                context.Principal = new ClaimsPrincipal(identity);
                context.Success();
            }
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            // Si el token JWT tiene formato inválido pero viene el encabezado X-User-Id, no rechazar
            if (context.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader) &&
                Guid.TryParse(userIdHeader, out var parsedUserId) &&
                parsedUserId != Guid.Empty)
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, parsedUserId.ToString()),
                    new Claim("sub", parsedUserId.ToString())
                };
                var identity = new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme);
                context.Principal = new ClaimsPrincipal(identity);
                context.Success();
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// 5. Registro de Controladores de la API (Controllers)
builder.Services.AddControllers();

// 6. Registro de IHttpClientFactory
builder.Services.AddHttpClient();

// 6.1 Registro de Servicio de Notificaciones Push con Firebase Cloud Messaging
builder.Services.AddSingleton<VitalBridge_Backend.Services.FirebaseNotificationService>();

// 6.2 Monitor en segundo plano para tomas sin confirmar tras 15 minutos
builder.Services.AddHostedService<VitalBridge_Backend.Services.MonitorTomasOmitidasService>();

// 7. Configuración de Documentación OpenAPI (Swagger en .NET 9)
builder.Services.AddOpenApi();

// ============================================================================
// CONSTRUCCIÓN DE LA APLICACIÓN (Cierre de registro de servicios)
// ============================================================================
var app = builder.Build();

// 8. Configuración del Pipeline de Peticiones HTTP (Middlewares)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Middlewares de seguridad: autenticación primero, luego autorización
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ============================================================================
// 9. SEMBRADO DE DATOS INICIALES (Data Seeding)
// ============================================================================
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await context.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS token_fcm_paciente (
            token TEXT PRIMARY KEY,
            id_paciente UUID NOT NULL REFERENCES paciente(id_paciente) ON DELETE CASCADE,
            fecha_creacion TIMESTAMPTZ DEFAULT NOW()
        );
        CREATE TABLE IF NOT EXISTS alerta_toma_omitida (
            id_horario INTEGER PRIMARY KEY REFERENCES horario(id_horario) ON DELETE CASCADE,
            fecha_notificacion TIMESTAMPTZ DEFAULT NOW()
        );
    ");
    await DbSeeder.SeedAsync(context);
}

// 10. Puesta en Marcha del Servidor
app.Run();
