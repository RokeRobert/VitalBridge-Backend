# =============================================================================
# DOCKERFILE PARA DESPLIEGUE EN LA NUBE (Render, Railway, Fly.io, Azure)
# =============================================================================

# Etapa 1: Compilación con el SDK de .NET 9
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restaurar dependencias de NuGet de forma optimizada en caché
COPY ["VitalBridge-Backend.csproj", "./"]
RUN dotnet restore "./VitalBridge-Backend.csproj"

# Copiar el código fuente completo y compilar en modo Release
COPY . .
RUN dotnet publish "VitalBridge-Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etapa 2: Imagen de ejecución ligera (Runtime de ASP.NET Core 9)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copiar los binarios compilados desde la etapa anterior
COPY --from=build /app/publish .

# Puerto estándar para contenedores en .NET 8 y 9
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Comando de arranque del servidor
ENTRYPOINT ["dotnet", "VitalBridge-Backend.dll"]
