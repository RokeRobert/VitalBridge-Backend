// ============================================================================
// PUNTO DE ENTRADA PRINCIPAL DEL BACKEND (ASP.NET Core 9 / C#)
// ============================================================================
// Este archivo inicializa el servidor web, registra los servicios en el contenedor
// de Inyección de Dependencias (DI) y define el pipeline de procesamiento HTTP (Middlewares).
// ============================================================================

using Microsoft.EntityFrameworkCore;
using VitalBridge_Backend.Data;

// 1. Inicialización del Constructor (Builder)
// Carga variables de entorno, argumentos de consola y archivos appsettings.json
var builder = WebApplication.CreateBuilder(args);

// 2. Obtención de la Cadena de Conexión a la Base de Datos (PostgreSQL en Supabase)
// El operador '??' busca 'DefaultConnection' y si es nula, usa 'SupabaseConnection' como respaldo.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                    ?? builder.Configuration.GetConnectionString("SupabaseConnection");

// 3. Registro de Entity Framework Core (ORM)
// Se inyecta 'ApplicationDbContext' configurado con Npgsql para conectar con PostgreSQL.
// Ciclo de vida: Scoped (una instancia por cada petición HTTP).
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// 4. Registro de Controladores de la API (Controllers)
// Permite que ASP.NET detecte clases como AuthController, PacientesController, etc.
builder.Services.AddControllers();

// 5. Registro de IHttpClientFactory
// Administra de forma eficiente un pool de conexiones HTTP para peticiones salientes
// (por ejemplo, cuando el backend se conecta con Supabase Storage para subir fotos).
builder.Services.AddHttpClient();

// 6. Configuración de Documentación OpenAPI (Swagger en .NET 9)
builder.Services.AddOpenApi();

// ============================================================================
// CONSTRUCCIÓN DE LA APLICACIÓN (Cierre de registro de servicios)
// ============================================================================
var app = builder.Build();

// 7. Configuración del Pipeline de Peticiones HTTP (Middlewares)
// Si estamos en entorno de desarrollo, exponemos la documentación interactiva OpenAPI.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Redirecciona automáticamente peticiones HTTP inseguras hacia HTTPS.
app.UseHttpsRedirection();

// Middleware de autorización para validar permisos y roles en endpoints protegidos.
app.UseAuthorization();

// Mapea las rutas URL entrantes a los métodos correspondientes en los controladores.
app.MapControllers();

// ============================================================================
// 8. SEMBRADO DE DATOS INICIALES (Data Seeding)
// ============================================================================
// Como ApplicationDbContext es Scoped, creamos un alcance temporal (Scope) para
// obtener el contexto y ejecutar DbSeeder antes de abrir el puerto a los clientes.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DbSeeder.SeedAsync(context);
}

// 9. Puesta en Marcha del Servidor
// Pone al servidor Kestrel a escuchar peticiones en el puerto configurado (ej. 5113).
app.Run();
