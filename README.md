# Logística — Plataforma multioperador de última milla

Laboratorio .NET 2026 · Equipo 1. La documentación de análisis y diseño está en [`docs/analisis-diseno`](docs/analisis-diseno).

## Requisitos

- .NET 10 SDK (versión fijada en `global.json`)
- Docker Desktop con Docker Compose

## Puesta en marcha local

1. Copiar `.env.example` como `.env` y completar las claves.
2. Levantar los servicios de soporte (PostgreSQL, Valkey, RabbitMQ y Aspire Dashboard):
   ```bash
   docker compose up -d --wait
   ```
3. Compilar y probar:
   ```bash
   dotnet build Logistica.slnx
   dotnet test Logistica.slnx
   ```

| Servicio | URL local |
| --- | --- |
| RabbitMQ Management | http://localhost:15672 |
| Aspire Dashboard | http://localhost:18888 |

## Estructura

```
src/
  Logistica.Api/                 API (Minimal APIs) que compone los módulos
  Logistica.Backoffice/          Razor Pages
  Logistica.PortalComercio/      Blazor WebAssembly
  Logistica.SeguimientoPublico/  Blazor WebAssembly
  Logistica.Repartidor.Pwa/      Blazor WebAssembly PWA
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
  Logistica.ArchitectureTests/   Reglas de dependencia del ADR-0001
infra/terraform/
```
