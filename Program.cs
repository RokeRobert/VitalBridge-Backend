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
             ?? builder.Configuration["Jwt:Secret"]
             ?? builder.Configuration["Supabase:AnonKey"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = !string.IsNullOrEmpty(jwtSecret),
        IssuerSigningKey = !string.IsNullOrEmpty(jwtSecret)
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
            : null,
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        NameClaimType = ClaimTypes.NameIdentifier
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(token) && token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Token = token.Substring("Bearer ".Length).Trim();
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
    await DbSeeder.SeedAsync(context);
}

// 10. Puesta en Marcha del Servidor
app.Run();
