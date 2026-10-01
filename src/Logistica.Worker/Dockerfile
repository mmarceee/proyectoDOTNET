# Imagen de Logistica.Worker, desplegado de forma independiente de la API.
# Se construye desde la raíz del repositorio: docker build -f src/Logistica.Worker/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Logistica.Worker/Logistica.Worker.csproj -c Release -o /app

# aspnet y no runtime: ejecuta el módulo Seguimiento, que usa ASP.NET Core en su capa Presentation.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Logistica.Worker.dll"]
