# Guía: la ruta de un caso de uso

Esta guía explica, con un ejemplo completo, qué archivos hay que crear para implementar un caso de uso, dónde va cada uno y por qué va ahí. El ejemplo es **CU-10 · Crear un envío individual** ([casos-de-uso.md](analisis-diseno/casos-de-uso.md), sección 2.2), que es además el que exige el monitoreo del 8/10.

Las reglas que se aplican salen del ADR-0001 y sus addenda. No hace falta memorizarlas: las pruebas de arquitectura fallan si se rompen.

> La versión mínima de CU-10 del 8/10 ya está implementada. Los fragmentos de código de esta guía son versiones **abreviadas** de los archivos reales; cada paso indica dónde está el archivo completo. Lo que todavía no existe se marca como *pendiente*.

---

## 1. El recorrido completo de una petición

Cuando el comercio aprieta **Confirmar** en el Portal, la petición recorre estas piezas:

```
 Portal del comercio (Blazor WebAssembly, en el navegador)
   │  POST /api/envios   con un CrearEnvioRequest en JSON
   ▼
 Logistica.Api  (el host: arranca todo, inquilino, manejo de errores)
   │  la ruta /api/envios la registró EnviosModule.MapEnviosEndpoints()
   ▼
 Envios · Presentation · CrearEnvioEndpoint
   │  traduce HTTP → CrearEnvioCommand
   ▼
 Envios · Application · CrearEnvioHandler        ← coordina el caso de uso
   │  1. toma operador y comercio ──────────────►  ICurrentTenant (SharedKernel)
   │  2. pide la zona y la tarifa ──────────────►  IAdministracionModuleApi      (pendiente: 15/10)
   │  3. pide el número del envío ──────────────►  IEnvioRepository  (Domain)
   │  4. crea el envío ─────────────────────────►  Envios · Domain · Envio
   │                                               (las reglas de negocio)
   │  5. lo guarda ─────────────────────────────►  IEnvioRepository
   ▼                                                  └─ implementado por EnvioRepository
                                                         → EnviosDbContext → PostgreSQL
 respuesta HTTP 201 con un CrearEnvioResponse
 (o 400 con ProblemDetails si se violó una regla de negocio)
```

Cada pieza tiene **una sola responsabilidad**:

| Capa | Responsabilidad | No hace |
| :---- | :---- | :---- |
| **Presentation** (endpoint) | Traducir HTTP a un command y el resultado a una respuesta HTTP. | Reglas de negocio ni acceso a datos. |
| **Application** (handler) | Coordinar el caso de uso a través de interfaces: pedir datos, llamar al dominio, guardar. | Decidir reglas de negocio ni usar EF Core directamente. |
| **Domain** (entidades e interfaces de repositorio) | Las reglas de negocio: qué es un envío válido, qué transiciones se permiten. | Saber que existen HTTP, EF Core o PostgreSQL. |
| **Infrastructure** | Hablar con el mundo exterior: base de datos, cola, caché. Implementa las interfaces del dominio. | Reglas de negocio. |

La razón de esta separación es la que da la letra (sección 8.3): el 29/10 llega un cambio de requerimientos no anunciado. Si las reglas están en un solo lugar (Domain), el cambio se hace en un solo lugar.

---

## 2. Dónde van las entidades

Cada entidad del [modelo de dominio](analisis-diseno/modelo-de-dominio.md) tiene **un único módulo dueño** (ADR-0001, sección 2.1). Va en la carpeta `Domain/` de ese módulo, agrupada por agregado: una subcarpeta por raíz de agregado, con las entidades y value objects que le pertenecen.

### 2.1 Administración y configuración — `Logistica.Modules.Administracion/Domain/`

```
Domain/
  Operadores/      Operador.cs, IdentidadVisual.cs
  Comercios/       Comercio.cs, RelacionComercial.cs, EstadoRelacion.cs
  ClavesApi/       ClaveApi.cs, TipoClaveApi.cs
  Zonas/           Zona.cs, FranjaHoraria.cs
  Tarifas/         VersionTarifario.cs, ReglaTarifa.cs, AjusteTarifa.cs, TipoAjuste.cs
  Reglas/          VersionReglas.cs, MotivoNoEntrega.cs, PruebaExigida.cs,
                   PoliticaDevolucion.cs, PlazoModalidad.cs
  Flota/           Repartidor.cs, Vehiculo.cs
  EstadoVersion.cs
```

`Usuario` no es una entidad de dominio propia: los usuarios los maneja **ASP.NET Core Identity**, en `Infrastructure/Identity/` de este módulo.

### 2.2 Envíos y entregas — `Logistica.Modules.Envios/Domain/`

```
Domain/
  Envios/          Envio.cs, Bulto.cs, DatosBulto.cs, Destinatario.cs, Direccion.cs,
                   EventoEnvio.cs, IntentoEntrega.cs, PruebaEntrega.cs,
                   EstadoEnvio.cs, OrigenEvento.cs, ResultadoIntento.cs,
                   TablaTransiciones.cs, IEnvioRepository.cs
  Devoluciones/    Devolucion.cs, EstadoDevolucion.cs
  Incidencias/     Incidencia.cs, TipoIncidencia.cs, EstadoIncidencia.cs
```

### 2.3 Depósito y liquidaciones — `Logistica.Modules.Deposito/Domain/`

```
Domain/
  Recepciones/     RecepcionDeposito.cs, ResultadoRecepcion.cs
  Liquidaciones/   Liquidacion.cs, LineaLiquidacion.cs, EstadoLiquidacion.cs
```

### 2.4 Planificación de rutas — `Logistica.Modules.Planificacion/Domain/`

```
Domain/
  Rutas/           Ruta.cs, Parada.cs, EstadoRuta.cs, EstadoParada.cs
```

### 2.5 Ejecución de rutas — `Logistica.Modules.Ejecucion/Domain/`

```
Domain/
  Carga/           EscaneoCarga.cs, ResultadoEscaneo.cs
  Posiciones/      PosicionVehiculo.cs
  Rendiciones/     Rendicion.cs, LineaRendicion.cs, EstadoLineaRendicion.cs
```

### 2.6 Seguimiento y notificaciones — `Logistica.Modules.Seguimiento/Domain/`

```
Domain/
  Avisos/          SuscripcionAviso.cs, EntregaAviso.cs, TipoEventoAviso.cs, EstadoEntregaAviso.cs
  Notificaciones/  Notificacion.cs, CanalNotificacion.cs, TipoNotificacion.cs, EstadoNotificacion.cs
```

### 2.7 Lo que no es de ningún módulo — `BuildingBlocks/`

Sólo piezas técnicas, sin conceptos de logística (addendum 1):

```
Logistica.SharedKernel/                  C# puro, sin paquetes
  Entity.cs                    clase base: Id (Guid v7) y eventos de dominio
  IDomainEvent.cs
  DomainException.cs           error de regla de negocio (la API responde 400)
  IOperadorOwned.cs            "esta entidad pertenece a un operador"
  IComercioOwned.cs            "esta entidad pertenece a un comercio"
  ICurrentTenant.cs            "¿quién está haciendo este request?"
  TenantMismatchException.cs   intento de guardar una entidad de otro inquilino

Logistica.BuildingBlocks.Infrastructure/  depende de EF Core y Npgsql
  Persistence/ModuleDbContext.cs          base de los DbContext: schema propio, Id generado por el dominio
  Persistence/PersistenceExtensions.cs    AddModuleDbContext y MigrateModuleDatabasesAsync
```

### 2.8 Cuatro reglas para las entidades

1. **Una entidad no referencia entidades de otro módulo, sólo su `Id`.** `Envio` va a guardar `ZonaId` (un `Guid`), no un objeto `Zona`. La zona es de Administración, y Envíos no puede ver su Domain.
2. **Los enums de negocio compartidos van en los `Contracts` del módulo dueño.** `Modalidad` (Estándar o Urgente) la define Administración, porque es parte de su configuración, y Envíos la usa desde `Logistica.Modules.Administracion.Contracts`.
3. **Las entidades protegen sus reglas.** Las propiedades tienen `private set` y el estado sólo cambia mediante métodos con nombre de negocio (`Envio.Crear`, `Envio.Transicionar`, `Ruta.Despachar`). Así ninguna otra parte del código puede dejar una entidad en un estado inválido.
4. **El Id lo genera el dominio.** `Entity` asigna un `Guid.CreateVersion7()` al crear el objeto, así las entidades hijas pueden guardar el `Id` del padre antes de ir a la base. `ModuleDbContext` le avisa a EF Core con `ValueGeneratedNever()`.

---

## 3. Los archivos de CU-10, uno por uno

Este es el orden recomendado para construirlos: **de adentro hacia afuera**. Primero el dominio, que no depende de nada y se prueba solo; al final la pantalla.

```
src/
  BuildingBlocks/Logistica.SharedKernel/
    Entity.cs, IDomainEvent.cs, DomainException.cs, IOperadorOwned.cs,
    IComercioOwned.cs, ICurrentTenant.cs, TenantMismatchException.cs                 ← paso 1 ✔
  Modules/Envios/Logistica.Modules.Envios/
    Domain/Envios/
      Envio.cs, Bulto.cs, DatosBulto.cs, Destinatario.cs, Direccion.cs,
      EventoEnvio.cs, EstadoEnvio.cs, OrigenEvento.cs                                ← paso 2 ✔
      TablaTransiciones.cs                                                           ← paso 2 (pendiente: 15/10)
  Modules/Administracion/Logistica.Modules.Administracion.Contracts/
    IAdministracionModuleApi.cs, Modalidad.cs, Results/...                           ← paso 4 (pendiente: 15/10)
  Modules/Envios/Logistica.Modules.Envios/
    Application/Features/CrearEnvio/
      CrearEnvioCommand.cs, CrearEnvioHandler.cs                                     ← paso 5 ✔
  BuildingBlocks/Logistica.BuildingBlocks.Infrastructure/Persistence/
    ModuleDbContext.cs, PersistenceExtensions.cs                                     ← paso 6 ✔
  Modules/Envios/Logistica.Modules.Envios/
    Domain/Envios/IEnvioRepository.cs                                                ← paso 6 ✔
    Infrastructure/Persistence/
      EnviosDbContext.cs, EnvioRepository.cs, Configurations/, Migrations/           ← paso 6 ✔
    Presentation/Features/CrearEnvio/
      CrearEnvioEndpoint.cs                                                          ← paso 7 ✔
    EnviosModule.cs  (se completa)                                                   ← paso 7 ✔
  Logistica.Http.Contracts/Envios/
    CrearEnvioRequest.cs, CrearEnvioResponse.cs                                      ← paso 7 ✔
  Logistica.Api/
    Program.cs, Errores/DomainExceptionHandler.cs, Tenancy/TenantProvisorio.cs      ← paso 7 ✔
  Logistica.PortalComercio/Pages/Envios/
    NuevoEnvio.razor                                                                 ← paso 8 (Lucas)
tests/
  Logistica.UnitTests/Envios/EnvioTests.cs                                           ← paso 3 ✔
  Logistica.IntegrationTests/PostgresApiFactory.cs                                   ← paso 9 ✔
  Logistica.IntegrationTests/Envios/CrearEnvioTests.cs                               ← paso 9 ✔
```

### Paso 1 · Las piezas base, en `SharedKernel`

**Por qué ahí:** las usan todos los módulos y no tienen reglas de negocio. Si estuvieran en un módulo, los demás tendrían que depender de él.

```csharp
// SharedKernel/ICurrentTenant.cs
public interface ICurrentTenant
{
    Guid? OperadorId { get; }   // null: no hay inquilino, y el filtro no devuelve nada (falla cerrado)
    Guid? ComercioId { get; }   // null: es personal del operador
}
```

`OperadorId` es `Guid?` y no `Guid` a propósito: "sin inquilino" es `null`, explícito, y no un `Guid.Empty` fácil de pasar por alto (ADR-0002, sección 2.2).

### Paso 2 · La entidad `Envio`, en `Envios/Domain/Envios/`

**Por qué ahí:** `Envio` es la raíz del agregado central y su dueño es Envíos. Acá viven sus reglas: un envío necesita al menos un bulto, nace en estado *Admitido* con su evento inicial (T1), y sólo cambia de estado según la tabla de transiciones (RF 11).

```csharp
// Envios/Domain/Envios/Envio.cs (abreviado)
internal sealed class Envio : Entity, IOperadorOwned, IComercioOwned
{
    private readonly List<Bulto> _bultos = [];
    private readonly List<EventoEnvio> _eventos = [];

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Numero { get; private set; } = "";
    public Destinatario Destinatario { get; private set; } = null!;
    public Direccion Direccion { get; private set; } = null!;
    public EstadoEnvio Estado { get; private set; }
    public decimal MontoTarifa { get; private set; }
    public IReadOnlyList<Bulto> Bultos => _bultos;
    public IReadOnlyList<EventoEnvio> Eventos => _eventos;

    private Envio() { } // para EF Core

    public static Envio Crear(Guid operadorId, Guid comercioId, string numero, Destinatario destinatario,
        Direccion direccion, IReadOnlyList<DatosBulto> bultos, OrigenEvento origen, Guid? responsableId,
        DateTimeOffset ahora)
    {
        // valida: número presente y al menos un bulto; si no, throw new DomainException("...")
        // crea los bultos (copian el inquilino del envío) y suma sus tarifas
        // registra el evento inicial: estado anterior nulo → Admitido (T1)
    }
}
```

Decisiones de la versión del 8/10:

- `OperadorId` y `ComercioId` se pasan a `Crear`: cuando el envío lo crea personal del operador, la sesión no trae `ComercioId`.
- El `Numero` lo genera el handler con una secuencia de PostgreSQL, porque sólo la base garantiza que sea único.
- La fecha (`ahora`) entra por parámetro: el dominio no lee el reloj y las pruebas pueden fijarla.
- `Destinatario` y `Direccion` son `record` (value objects) que se validan en su constructor.
- **Pendiente para el 15/10:** `Transicionar` y `TablaTransiciones`, con la tabla de transiciones de Cristian; y `ZonaId`, `Modalidad`, versiones de tarifario y reglas, y `TokenSeguimiento`.

`Envio` no sabe que existe una base de datos ni HTTP: por eso no tiene ningún `using` de EF Core ni de ASP.NET Core. La prueba de arquitectura lo verifica.

### Paso 3 · Pruebas unitarias del dominio, en `tests/Logistica.UnitTests/Envios/`

**Por qué ahora:** el dominio no depende de nada, así que se prueba sin base de datos ni servidor, en milisegundos. Es lo que exige la sección 6.15 (*pruebas unitarias sobre la lógica de dominio*), y te asegura que las reglas funcionan antes de conectarlas a nada.

```csharp
// tests/Logistica.UnitTests/Envios/EnvioTests.cs (una de las seis pruebas)
[Fact]
public void Un_envio_sin_bultos_no_se_puede_crear()
{
    Assert.Throws<DomainException>(() => CrearEnvio());
}
```

Cuando exista `Transicionar`, se agregan las pruebas de la tabla de transiciones, por ejemplo que un envío entregado no puede volver a tránsito.

### Paso 4 · El contrato de Administración, en `Administracion.Contracts` *(pendiente: 15/10)*

**Por qué ahí:** Envíos necesita la zona y la tarifa, pero no puede ver el Domain de Administración (ADR-0001: un módulo sólo usa los `Contracts` de otro). Administración publica una interfaz con lo que ofrece, y la implementa adentro, en su capa Application, con una clase `internal`.

```csharp
// Administracion.Contracts/IAdministracionModuleApi.cs
public interface IAdministracionModuleApi
{
    Task<ZonaResumen?> ObtenerZona(string codigoPostal, CancellationToken ct);
    Task<TarifaBulto?> CalcularTarifa(Guid zonaId, Modalidad modalidad,
                                      decimal pesoKg, decimal volumenM3, CancellationToken ct);
}
```

En la versión del 8/10 este paso no existe: el envío no tiene zona y la tarifa es cero (sección 4).

### Paso 5 · El command y el handler, en `Envios/Application/Features/CrearEnvio/`

**Por qué ahí:** es un *vertical slice* (ADR-0001, sección 2.3): todo lo del caso de uso queda en una carpeta con su nombre. El handler **coordina** pero no decide: las reglas siguen en `Envio`.

```csharp
// CrearEnvioCommand.cs: sin OperadorId ni ComercioId, que salen de la sesión
internal sealed record CrearEnvioCommand(
    Destinatario Destinatario, Direccion Direccion, IReadOnlyList<BultoACrear> Bultos, OrigenEvento Origen);

// CrearEnvioHandler.cs (abreviado)
internal sealed class CrearEnvioHandler(IEnvioRepository envios, ICurrentTenant tenant, TimeProvider reloj)
{
    public async Task<Envio> HandleAsync(CrearEnvioCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercioId = tenant.ComercioId ?? throw new InvalidOperationException("La sesión no tiene un comercio.");

        // 15/10: pedir la zona (CU-10, A2) y la tarifa de cada bulto (CU-10, paso 7) a Administración
        var bultos = command.Bultos.Select(b => new DatosBulto(/* medidas */, TarifaProvisoria)).ToList();

        var numero = await envios.SiguienteNumeroAsync(ct);
        var envio = Envio.Crear(operadorId, comercioId, numero, /* ... */, reloj.GetUtcNow());

        envios.Agregar(envio);
        await envios.GuardarCambiosAsync(ct);
        return envio;
    }
}
```

Tres decisiones para defender:

- **El handler usa `IEnvioRepository`, no `EnviosDbContext`.** Application no puede depender de Infrastructure ni de EF Core (ADR-0001, sección 2.4), y la prueba de arquitectura falla si lo hace. La interfaz está en el dominio y la implementa Infrastructure (paso 6).
- **El operador y el comercio salen de `ICurrentTenant`**, nunca del pedido: así nadie puede crear envíos a nombre de otro inquilino.
- **La fecha sale de `TimeProvider`**, que se inyecta: en UTC, como exige Npgsql, y reemplazable en las pruebas.

Es `internal`: nadie fuera del módulo puede usarlo (addendum 2).

### Paso 6 · La persistencia, en `BuildingBlocks` y `Envios/Infrastructure/Persistence/`

**Por qué ahí:** EF Core es un detalle de infraestructura. Si mañana cambiara la base de datos, se cambian estas carpetas y el dominio no se toca.

La parte común a todos los módulos está en `BuildingBlocks.Infrastructure/Persistence/`:

```csharp
// ModuleDbContext.cs (abreviado)
public abstract class ModuleDbContext(DbContextOptions options, string schema) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(schema);                                 // un schema por módulo (ADR-0003)
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        // para cada Entity: Id con ValueGeneratedNever() e Ignore(DomainEvents)
    }
}
```

`PersistenceExtensions.AddModuleDbContext<T>()` registra el `DbContext` del módulo sobre PostgreSQL, con su propia tabla de historial de migraciones, y `MigrateModuleDatabasesAsync()` aplica las migraciones de todos los módulos.

La parte de Envíos:

```csharp
// Domain/Envios/IEnvioRepository.cs: el contrato, sin EF Core
internal interface IEnvioRepository
{
    Task<string> SiguienteNumeroAsync(CancellationToken ct);
    void Agregar(Envio envio);
    Task GuardarCambiosAsync(CancellationToken ct);
}

// Infrastructure/Persistence/EnviosDbContext.cs
internal sealed class EnviosDbContext(DbContextOptions<EnviosDbContext> options)
    : ModuleDbContext(options, Schema)
{
    public const string Schema = "envios";
    public DbSet<Envio> Envios => Set<Envio>();
    // OnModelCreating agrega la secuencia numero_envio
}
```

`EnvioRepository` implementa la interfaz con el `EnviosDbContext`: el número sale de `nextval('envios.numero_envio')` con formato `ENV-000001`. `Agregar` y `GuardarCambiosAsync` van separados para que, cuando llegue el Outbox (29/10), el envío y el mensaje se guarden en una sola transacción.

En `Configurations/` va el mapeo: `Destinatario` y `Direccion` como *complex types* (columnas de la tabla `Envios`, sin tabla propia), los bultos y los eventos como tablas hijas, los enums como texto, y los índices únicos (`OperadorId`, `Numero`) y (`OperadorId`, `Codigo`).

**Migraciones.** Se generan en un contenedor, porque Smart App Control bloquea `dotnet ef` en Windows:

```bash
bash scripts/migracion-en-docker.sh Envios NombreDelCambio
```

Quedan en `Infrastructure/Persistence/Migrations/`. La API las aplica al iniciar sólo si `Database:MigrateOnStartup` vale `true`, cosa que hace `docker-compose.yml`.

### Paso 7 · El endpoint, los DTOs y el registro

**Por qué ahí:** el endpoint es la capa Presentation del módulo (addendum 2). Los DTOs `CrearEnvioRequest` y `CrearEnvioResponse` van en `Logistica.Http.Contracts`, porque el Portal (que corre en el navegador) también los necesita (addendum 5). El request no trae tarifa: la calcula el sistema.

```csharp
// Envios/Presentation/Features/CrearEnvio/CrearEnvioEndpoint.cs (abreviado)
internal static class CrearEnvioEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", CrearAsync);
    }

    private static async Task<Created<CrearEnvioResponse>> CrearAsync(
        CrearEnvioRequest request, CrearEnvioHandler handler, CancellationToken ct)
    {
        var command = new CrearEnvioCommand(/* request → objetos de dominio */, OrigenEvento.PortalComercio);
        var envio = await handler.HandleAsync(command, ct);
        return TypedResults.Created($"/api/envios/{envio.Id}", new CrearEnvioResponse(envio.Id, envio.Numero));
    }
}
```

En `EnviosModule.cs` se registra todo:

```csharp
services.AddModuleDbContext<EnviosDbContext>(configuration.GetConnectionString("Postgres"), EnviosDbContext.Schema);
services.AddScoped<IEnvioRepository, EnvioRepository>();   // el único lugar donde se conocen interfaz e implementación
services.AddScoped<CrearEnvioHandler>();
// ...
CrearEnvioEndpoint.Map(group);
```

`Logistica.Api` ya llamaba a `AddEnviosModule()` y `MapEnviosEndpoints()`. Para CU-10 se le agregó lo que sirve a todos los módulos:

| Pieza | Para qué |
| :---- | :---- |
| `Errores/DomainExceptionHandler.cs` | Una `DomainException` responde 400 con ProblemDetails, sin `try/catch` en cada endpoint |
| `UseExceptionHandler` con `StatusCodeSelector` | Un body inválido responde 400 también en Development, en lugar de 500 |
| `RespectNullableAnnotations` y `RespectRequiredConstructorParameters` | Un JSON sin un campo obligatorio responde 400 antes de llegar al endpoint |
| `AddSingleton(TimeProvider.System)` | El reloj que reciben los handlers |
| `Tenancy/TenantProvisorio.cs` | El `ICurrentTenant` del 8/10 (sección 4) |
| `MigrateModuleDatabasesAsync()` | Crea las tablas al iniciar, si la configuración lo pide |

### Paso 8 · La pantalla, en `Logistica.PortalComercio/Pages/Envios/` *(pendiente: Lucas)*

**Por qué ahí:** es la interfaz del comercio. Usa `HttpClient` para llamar a `/api/envios` con el mismo `CrearEnvioRequest`, y nunca referencia los módulos (la prueba de arquitectura lo verifica).

### Paso 9 · La prueba de integración, en `tests/Logistica.IntegrationTests/`

**Por qué:** prueba el recorrido completo de la sección 1 contra un PostgreSQL real, y es lo que pide la sección 6.15 (*pruebas de integración sobre al menos un flujo crítico*).

- `PostgresApiFactory` levanta la API en memoria (`WebApplicationFactory`) conectada a un PostgreSQL 17.6 que Testcontainers crea en Docker y borra al terminar. Con `UseSetting` le pasa la cadena de conexión y activa las migraciones al iniciar.
- `Envios/CrearEnvioTests` hace el `POST` y verifica la respuesta (201, número y `Location`) y, con SQL directo, lo que quedó en la base: estado *Admitido*, el operador de la sesión, los bultos y el evento inicial. También prueba los 400 por regla de negocio y por JSON incompleto.
- Cuando exista el listado del Backoffice (CU-13), se agrega el `GET` para verificar que el envío creado aparece, que es lo que se demuestra el 8/10.

`scripts/test-en-docker.sh` le pasa al contenedor de pruebas el socket de Docker, para que Testcontainers pueda levantar PostgreSQL. En el CI no hace falta: GitHub Actions ya tiene Docker.

---

## 4. Lo provisorio para el 8/10

Para llegar al 8/10 alcanza con una versión mínima (catálogo, sección 3). Hay piezas que después se reemplazan:

| Pieza | Versión del 8/10 | Versión definitiva |
| :---- | :---- | :---- |
| `ICurrentTenant` | `TenantProvisorio`: operador `11111111-…` y comercio `22222222-…`, fijos en `appsettings.json` de la API | El claim de la cookie de Identity (15/10, Cristian) |
| Datos iniciales | Un operador y un comercio con los mismos GUID de `TenantProvisorio` | Los mismos, más un segundo operador con configuración distinta, zonas y tarifarios (15/10) |
| Zona y tarifa | El envío no tiene zona y la tarifa de cada bulto es cero | Calculadas por Administración con `IAdministracionModuleApi` (15/10) |
| Origen del envío | Siempre `PortalComercio` | El que corresponda: Portal, Backoffice o API pública |
| Token de seguimiento | No se genera todavía | Token firmado (RF 25) |

---

## 5. Resumen: dónde va cada cosa

| Qué | Dónde | Por qué |
| :---- | :---- | :---- |
| Entidad, value object, enum de negocio | `Modulo/Domain/<Agregado>/` | Las reglas viven en un solo lugar, sin dependencias técnicas. |
| Interfaz de repositorio | `Modulo/Domain/<Agregado>/I<Agregado>Repository.cs` | Application la usa sin conocer EF Core. |
| Enum de negocio que usa otro módulo | `Modulo.Contracts/` del dueño | Es lo único que otro módulo puede ver. |
| Command y handler | `Modulo/Application/Features/<CasoDeUso>/` | Un caso de uso por carpeta (vertical slice). |
| Interfaz para otros módulos | `Modulo.Contracts/I<Modulo>ModuleApi.cs` | Comunicación síncrona entre módulos (addendum 3). |
| Su implementación | `Modulo/Application/` (`internal`) | El interior del módulo queda oculto. |
| `DbContext`, repositorio, mapeos, migraciones | `Modulo/Infrastructure/Persistence/` | EF Core es un detalle técnico. |
| Endpoint | `Modulo/Presentation/Features/<CasoDeUso>/` | El caso de uso completo queda en el módulo. |
| Página del Backoffice | `Modulo/Presentation/Features/<CasoDeUso>/` | Ídem (addendum 4). |
| DTO de la API HTTP | `Logistica.Http.Contracts/<Modulo>/` | Lo comparten el servidor y las apps del navegador. |
| Pieza técnica común a todos | `SharedKernel` o `BuildingBlocks.Infrastructure` | Sin lógica de negocio (addendum 1). |
| Manejo de errores, implementaciones de `ICurrentTenant` | `Logistica.Api` | Dependen de ASP.NET Core y valen para todos los módulos. |
| Prueba de reglas | `tests/Logistica.UnitTests/<Modulo>/` | Rápida, sin infraestructura. |
| Prueba del recorrido completo | `tests/Logistica.IntegrationTests/<Modulo>/` | Contra una base real (Testcontainers). |

---

## Historial de versiones

| Versión | Fecha | Descripción |
| :---: | :---: | :---- |
| 1.0 | 30/09/2026 | Versión inicial, con bocetos de código. |
| 1.1 | 05/10/2026 | Se actualiza con la implementación de la versión mínima de CU-10: el handler usa `IEnvioRepository` en lugar de `EnviosDbContext` (Application no puede depender de Infrastructure), `ModuleDbContext` y las migraciones en BuildingBlocks, `ICurrentTenant` con `Guid?`, complex types en lugar de owned types, manejo de errores e inquilino provisorio en la API, y la prueba de integración con Testcontainers. Se marcan los pasos pendientes. |
