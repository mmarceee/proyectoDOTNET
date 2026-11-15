# Logística — Plataforma multioperador de última milla

Laboratorio .NET 2026 · Equipo 1. La documentación de análisis y diseño está en [`docs/analisis-diseno`](docs/analisis-diseno).

El cambio de entrega parcial se documenta en las [decisiones cerradas](docs/analisis-diseno/decisiones-entrega-parcial.md) y el [ADR-0007 de adaptación arquitectónica](docs/analisis-diseno/ADR-0007-adaptacion-a-entrega-parcial.md). La decisión de diseño está aceptada; la implementación y la evaluación retrospectiva se registran por separado.

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
   La aplicación queda en http://localhost:8080 (ver [Rutas](#rutas)). Los usuarios para ingresar están en
   [`docs/usuarios-demostracion.txt`](docs/usuarios-demostracion.txt).

Para configurar suscripciones de avisos (CU-63), completar también `AVISOS_CLAVE_CIFRADO` en `.env` con
una clave exclusiva de exactamente 32 bytes en Base64 (`openssl rand -base64 32`). API y Worker comparten
el valor; debe conservarse entre reinicios y despliegues para poder descifrar los secretos guardados.
El [avance de CU-63](docs/analisis-diseno/cu-63/README.md) detalla la configuración y el alcance implementado.

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
4. Ejecutar **también** `Logistica.Worker`, en otro proceso de Visual Studio o en otra terminal:
   ```bash
   dotnet run --project src/Logistica.Worker
   ```
   Ejecutar sólo la API no procesa los avisos. Si se depuran ambos proyectos localmente, mantener detenidos los
   contenedores `api` y `worker` para evitar instancias adicionales consumiendo o publicando mensajes.

La [guía de prueba del worker independiente](docs/prueba-worker-independiente.md) permite demostrar la separación
de procesos, la recuperación de mensajes pendientes, los reintentos y la cola de fallidos.
Para simular éxito y fallos del comercio, usar el [receptor de demostración](tools/Logistica.ReceptorDemo/README.md),
con panel para alternar entre HTTP 200 y 503 y consultar los avisos recibidos.

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

Para el trabajo diario, el script acepta un filtro y corre sólo las pruebas cuyo nombre completo lo contiene, sin
cobertura (por ejemplo, las de Depósito en unos 2 minutos):

```bash
bash scripts/test-en-docker.sh Deposito
```

Las pruebas de extremo a extremo (opcional 7.2, `tests/Logistica.E2ETests`) abren Chromium y recorren los flujos
críticos sobre la aplicación completa: el ingreso al Backoffice, el alta de un envío en el Portal y su aislamiento entre
operadores, el seguimiento público y una incidencia. Necesitan el navegador instalado, así que no forman parte de
`Logistica.slnx`: corren en el job `e2e` del CI y, en local, en un contenedor que instala Chromium:

```bash
bash scripts/e2e-en-docker.sh
```

Cada ventana del navegador guarda su traza en `TestResults/e2e-*/trazas/`. Se ve paso a paso arrastrando el `.zip` a
https://trace.playwright.dev.

Las pruebas de integración usan un solo PostgreSQL para toda la suite: `PostgresApiFactory` migra y siembra una base
plantilla una vez y cada clase recibe su propia base clonada (`CREATE DATABASE … TEMPLATE`). Las clases corren en
paralelo, hasta cuatro a la vez (`ConfiguracionPruebas.cs`); las que levantan contenedores propios (RabbitMQ, Valkey,
Mailpit o un PostgreSQL aparte) están en la colección `ContenedoresPropios` y corren solas, después de las demás.
Con `coverage.runsettings`, cada proceso de pruebas tiene un límite de heap administrado de 4 GiB para
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

## Despliegue

El entorno productivo (Azure Container Apps con la Api, el Worker, Valkey, RabbitMQ y Aspire Dashboard, y PostgreSQL
Flexible Server, en Chile Central y con el crédito de Azure for Students) se crea y se destruye con Terraform. Requisitos,
costo, secretos y problemas conocidos en [`infra/terraform`](infra/terraform/README.md); la decisión, en el
[ADR-0008](docs/analisis-diseno/ADR-0008-despliegue-e-infraestructura.md).

Con la CLI de Azure con sesión iniciada (`az login`), desde `infra/terraform` y con `produccion.tfvars` copiado de
`terraform.tfvars.example` (en PowerShell, el `-var-file` va entre comillas):

```bash
terraform init
terraform plan "-var-file=produccion.tfvars"
terraform apply "-var-file=produccion.tfvars"
terraform output url
terraform destroy "-var-file=produccion.tfvars"
```

- **Despliegue continuo:** cada push a `main` que pasa las pruebas publica las imágenes de la Api y el Worker en GHCR y
  actualiza las dos Container Apps (job `deploy` de `.github/workflows/ci.yml`). El CI inicia sesión con el secreto
  `AZURE_CREDENTIALS` de la entidad de servicio `logistica-ci`, y Terraform le da permiso sobre el grupo de recursos
  (`ci_principal_id`). Con el ambiente destruido, el job no despliega y deja un aviso.
- **Correo:** en producción no se configura un servidor de correo; los correos quedan en mensajes fallidos.
- **Observabilidad:** `terraform output url_observabilidad` y `terraform output -raw acceso_observabilidad`.

## Rutas

| Ruta | Contenido |
| --- | --- |
| `/api/...` | Minimal APIs de los módulos |
| `/api/docs` | Documentación de la API pública y consola para claves de prueba |
| `/api/openapi/v1.json` | Especificación OpenAPI de la API pública v1 |
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

## Scripts

| Script | Para qué |
| --- | --- |
| `scripts/configurar-secretos.sh` | Carga las cadenas de conexión de `.env` en `dotnet user-secrets` (Api y Worker), para depurar |
| `scripts/test-en-docker.sh [filtro]` | Pruebas en un contenedor Linux, igual que el CI, con cobertura; con filtro, sólo las que coinciden |
| `scripts/e2e-en-docker.sh` | Pruebas de extremo a extremo con Playwright en un contenedor que instala Chromium |
| `scripts/migracion-en-docker.sh <Modulo> <Nombre>` | Genera una migración de EF Core en un contenedor (Smart App Control bloquea `dotnet ef` en Windows) |
| `scripts/cu40-en-docker.sh [migrations\|test]` | Script histórico de CU-40: compila en el SDK de Docker y, con `migrations`, generó las migraciones `PlanificacionCU40`; con `test`, corre las pruebas |
| `scripts/datos-demo-cu14.sql` | Datos ficticios para el detalle ampliado del envío (CU-14) |
| `scripts/preparar-lan.ps1`, `scripts/habilitar-lan-firewall.ps1`, `scripts/servidor-certificado-lan.cjs` | Ambiente de ensayo en la red local, con HTTPS, para probar la PWA en un teléfono (`infra/lan`) |
| `scripts/preparar-escenario-rf32.cjs` | Prepara el escenario de aceptación manual de la entrega por bulto (RF 32) en el ambiente LAN |
| `scripts/tests/` | Pruebas JavaScript de la PWA (cámara, hoja de ruta, posición, service worker y visita) |

## Documentación

| Documento | Ubicación |
| --- | --- |
| Documento técnico (requerimientos no funcionales, decisiones y configuración) | [`docs/documento-tecnico.md`](docs/documento-tecnico.md) |
| Registros de decisión de arquitectura (ADR-0001 a ADR-0008) | [`docs/analisis-diseno`](docs/analisis-diseno) |
| Bitácora de uso de asistentes de IA | [`docs/bitacora-uso-de-ia.md`](docs/bitacora-uso-de-ia.md) |
| Usuarios de demostración | [`docs/usuarios-demostracion.txt`](docs/usuarios-demostracion.txt) |
| Pruebas de resiliencia | [`docs/pruebas-de-resiliencia.md`](docs/pruebas-de-resiliencia.md) |
| Guías de demostración (API pública, avisos, worker independiente) | [`docs`](docs) |

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
  Logistica.E2ETests/            Flujos críticos desde el navegador, con Playwright (fuera de Logistica.slnx)
tools/
  Logistica.ReceptorDemo/        Receptor de avisos de prueba (responde 200 o 503) para demostrar reintentos y fallidos
scripts/                         Ver la sección Scripts
infra/
  terraform/                     Entorno productivo en Azure
  lan/                           Ambiente de ensayo en la red local
docs/                            Documento técnico, ADR, análisis y diseño, guías y bitácora de IA
```
