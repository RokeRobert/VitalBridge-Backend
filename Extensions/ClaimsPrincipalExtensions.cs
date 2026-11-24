using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace VitalBridge_Backend.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extrae el ID del usuario directamente de los claims del token JWT (ClaimTypes.NameIdentifier o sub),
    /// con respaldo al encabezado X-User-Id para peticiones en desarrollo o clientes legados.
    /// </summary>
    public static Guid? ObtenerUsuarioId(this ClaimsPrincipal user, HttpRequest? request = null)
    {
        var claimId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? user.FindFirst("sub")?.Value
                   ?? user.FindFirst("id")?.Value;

        if (!string.IsNullOrEmpty(claimId) && Guid.TryParse(claimId, out var parsedGuid) && parsedGuid != Guid.Empty)
        {
            return parsedGuid;
        }

        if (request != null && request.Headers.TryGetValue("X-User-Id", out var headerVal) && Guid.TryParse(headerVal, out var headerGuid) && headerGuid != Guid.Empty)
        {
            return headerGuid;
        }

        return null;
    }
}
