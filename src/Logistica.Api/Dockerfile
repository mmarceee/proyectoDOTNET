# Imagen de Logistica.Api: API, Backoffice y aplicaciones Blazor WebAssembly en un único host.
# Se construye desde la raíz del repositorio: docker build -f src/Logistica.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Logistica.Api/Logistica.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Logistica.Api.dll"]
