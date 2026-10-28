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

Para configurar suscripciones de avisos (CU-63), completar también `AVISOS_CLAVE_CIFRADO` en `.env` con
una clave exclusiva de exactamente 32 bytes en Base64 (`openssl rand -base64 32`). API y Worker comparten
el valor; debe conservarse entre reinicios y despliegues para poder descifrar los secretos guardados.
El [avance de CU-63](docs/analisis-diseno/cu-63/README.md) detalla la configuración y el alcance implementado.

### Desarrollo desde Visual 

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
4. Ejecutar **también** `Logistica.Worker`, en otro proceso de Visual Studio o en otra terminal:
   ```bash
   dotnet run --project src/Logistica.Worker
   ```
   Ejecutar sólo la API no procesa los avisos. Si se depuran ambos proyectos localmente, mantener detenidos los
   contenedores `api` y `worker` para evitar instancias adicionales consumiendo o publicando mensajes.

La [guía de prueba del worker independiente](docs/prueba-worker-independiente.md) permite demostrar la separación
de procesos, la recuperación de mensajes pendientes, los reintentos y la cola de fallidos.

### Compilar y probar

```bash
dotnet build Logistica.slnx
dotnet test Logistica.slnx
```

Si Windows (Smart App Control) bloquea los DLL compilados y las pruebas no cargan, ejecutarlas en un contenedor Linux, igual que el CI:

```bash
bash scripts/test-en-docker.sh
```

El script también genera el reporte de cobertura de las capas de dominio y aplicación en `coverage/index.html` (configuración en `coverage.runsettings`). En el CI, el resumen aparece en la página de cada ejecución y el reporte completo se descarga como el artefacto `reporte-cobertura`.

Las clases de integración se ejecutan de a una para limitar los contenedores y hosts de API
simultáneos. Con `coverage.runsettings`, cada proceso de pruebas tiene un límite de heap administrado de 4 GiB para
que el recolector libere memoria antes de agotar el runner; no es un límite de memoria de la aplicación desplegada.
El límite usa [`DOTNET_GCHeapHardLimit`](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#heap-hard-limit)
en el entorno del testhost configurado por VSTest.
`PostgresApiFactory` usa un catálogo estático vacío para no reconstruir miles de endpoints Blazor en cada host de
negocio. `AplicacionesWebAssemblyTests` mantiene el catálogo generado real y verifica el arranque, runtime y service
worker de las aplicaciones. Sin `StaticAssets:ManifestPath`, la API sigue usando su catálogo generado habitual.
Se cierran los servidores de compilación antes de las pruebas. CI conserva los resultados TRX y los diagnósticos de bloqueos en el artefacto `resultados-pruebas`, incluso
si una prueba falla. Tres minutos sin actividad de pruebas abortan el proceso y registran la secuencia; el paso completo
tiene un límite de veinte minutos. El script de Docker usa los mismos diagnósticos y guarda cada ejecución por separado
en `TestResults/docker-*`.

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
