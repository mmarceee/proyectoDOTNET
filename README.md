# Logística — Plataforma multioperador de última milla

Laboratorio .NET 2026 · Equipo 1. La documentación de análisis y diseño está en [`docs/analisis-diseno`](docs/analisis-diseno).

## Requisitos

- .NET 10 SDK (versión fijada en `global.json`)
- Docker Desktop con Docker Compose
- En Windows, Git Bash para ejecutar los scripts de `scripts/`

## Puesta en marcha local

1. Copiar `.env.example` como `.env` y completar las claves. Si un puerto ya está en uso en tu máquina (por ejemplo el 5432), cambialo en `.env`.
2. Levantar el entorno completo: servicios de soporte (PostgreSQL, Valkey, RabbitMQ, Aspire Dashboard y Mailpit), Api y Worker:
   ```bash
   docker compose up -d --build --wait
   ```
   La aplicación queda en http://localhost:8080 (ver [Rutas](#rutas)).

### Desarrollo desde Visual Studio

Para depurar la Api o el Worker, levantar sólo los servicios de soporte y ejecutar la aplicación desde Visual Studio o con `dotnet run`:

1. Levantar los servicios de soporte:
   ```bash
   docker compose up -d --wait postgres valkey rabbitmq aspire-dashboard mailpit
   ```
2. Cargar las cadenas de conexión en `dotnet user-secrets` a partir de `.env` (compartidas por la Api y el Worker):
   ```bash
   bash scripts/configurar-secretos.sh
   ```
3. Ejecutar `Logistica.Api` desde Visual Studio o con `dotnet run --project src/Logistica.Api`. La Api sirve también el Backoffice y las tres aplicaciones Blazor WebAssembly; no hace falta ejecutarlas por separado.

### Compilar y probar

```bash
dotnet build Logistica.slnx
dotnet test Logistica.slnx
```

Si Windows (Smart App Control) bloquea los DLL compilados y las pruebas no cargan, ejecutarlas en un contenedor Linux, igual que el CI:

```bash
bash scripts/test-en-docker.sh
```

## Rutas

| Ruta | Contenido |
| --- | --- |
| `/api/...` | Minimal APIs de los módulos |
| `/backoffice` | Backoffice (Razor Pages) |
| `/portal/` | Portal del comercio (Blazor WebAssembly) |
| `/seguimiento/{token}` | Seguimiento público (Blazor WebAssembly) |
| `/repartidor/` | Aplicación del repartidor (Blazor WebAssembly PWA) |
| `/health` | Health check |

| Servicio de soporte | URL local |
| --- | --- |
| RabbitMQ Management | http://localhost:15672 |
| Aspire Dashboard | http://localhost:18888 |
| Mailpit (correos de desarrollo) | http://localhost:8025 |

## Estructura

```
src/
  Logistica.Api/                 Host único: API, Backoffice y aplicaciones WebAssembly
  Logistica.Backoffice/          Biblioteca Razor: layout y páginas comunes del Backoffice
  Logistica.PortalComercio/      Blazor WebAssembly (/portal)
  Logistica.SeguimientoPublico/  Blazor WebAssembly (/seguimiento)
  Logistica.Repartidor.Pwa/      Blazor WebAssembly PWA (/repartidor)
  Logistica.Http.Contracts/      DTOs de request y response de la API, compartidos con las apps WebAssembly
  Logistica.Worker/              Worker independiente (consume RabbitMQ)
  BuildingBlocks/
    Logistica.SharedKernel/                   Tipos base y contratos, sin frameworks
    Logistica.BuildingBlocks.Infrastructure/  Multitenancy (filtros, interceptor) y Outbox con EF Core
  Modules/<Modulo>/
    Logistica.Modules.<Modulo>/            Domain, Application/Features, Infrastructure, Presentation/Features
    Logistica.Modules.<Modulo>.Contracts/  Lo único visible para otros módulos: I<Modulo>ModuleApi, Results/, Events/
tests/
  Logistica.UnitTests/
  Logistica.IntegrationTests/
  Logistica.ArchitectureTests/   Reglas de dependencia del ADR-0001 y sus addenda
scripts/                         configurar-secretos.sh, test-en-docker.sh
infra/terraform/
```
