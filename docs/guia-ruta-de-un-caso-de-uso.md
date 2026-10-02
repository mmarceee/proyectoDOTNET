# Guía: la ruta de un caso de uso

Esta guía explica, con un ejemplo completo, qué archivos hay que crear para implementar un caso de uso, dónde va cada uno y por qué va ahí. El ejemplo es **CU-10 · Crear un envío individual** ([casos-de-uso.md](analisis-diseno/casos-de-uso.md), sección 2.2), que es además el que exige el monitoreo del 8/10.

Las reglas que se aplican salen del ADR-0001 y sus addenda. No hace falta memorizarlas: las pruebas de arquitectura fallan si se rompen.

> Los fragmentos de código son **bocetos** para entender la estructura, no la implementación final.

---

## 1. El recorrido completo de una petición

Cuando el comercio aprieta **Confirmar** en el Portal, la petición recorre estas piezas:

```
 Portal del comercio (Blazor WebAssembly, en el navegador)
   │  POST /api/envios   con un CrearEnvioRequest en JSON
   ▼
 Logistica.Api  (el host: arranca todo, autenticación, tenant)
   │  la ruta /api/envios la registró EnviosModule.MapEnviosEndpoints()
   ▼
 Envios · Presentation · CrearEnvioEndpoint
   │  traduce HTTP → CrearEnvioCommand
   ▼
 Envios · Application · CrearEnvioHandler        ← coordina el caso de uso
   │  1. pide la zona y la tarifa ──────────────►  IAdministracionModuleApi
   │                                               (Contracts de Administración)
   │  2. crea el envío ─────────────────────────►  Envios · Domain · Envio
   │                                               (las reglas de negocio)
   │  3. lo guarda ─────────────────────────────►  Envios · Infrastructure · EnviosDbContext
   ▼                                               (EF Core → PostgreSQL)
 respuesta HTTP 201 con un CrearEnvioResponse
```

Cada pieza tiene **una sola responsabilidad**:

| Capa | Responsabilidad | No hace |
| :---- | :---- | :---- |
| **Presentation** (endpoint) | Traducir HTTP a un command y el resultado a una respuesta HTTP. | Reglas de negocio ni acceso a datos. |
| **Application** (handler) | Coordinar el caso de uso: pedir datos, llamar al dominio, guardar. | Decidir reglas de negocio. |
| **Domain** (entidades) | Las reglas de negocio: qué es un envío válido, qué transiciones se permiten. | Saber que existen HTTP, EF Core o PostgreSQL. |
| **Infrastructure** | Hablar con el mundo exterior: base de datos, cola, caché. | Reglas de negocio. |

La razón de esta separación es la que da la letra (sección 8.3): el 29/10 llega un cambio de requerimientos no anunciado. Si las reglas están en un solo lugar (Domain), el cambio se hace en un solo lugar.

---

## 2. Dónde van las entidades

Cada entidad del [modelo de dominio](analisis-diseno/modelo-de-dominio.md) tiene **un único módulo dueño** (ADR-0001, sección 2.1). Va en la carpeta `Domain/` de ese módulo, agrupada por agregado: una subcarpeta por raíz de agregado, con las entidades y value objects que le pertenecen.

### 2.1 Administración y configuración — `Logistica.Modules.Administracion/Domain/`

```
Domain/
  Operadores/      Operador.cs, IdentidadVisual.cs
  Comercios/       Comercio.cs, RelacionComercial.cs, EstadoRelacion.cs
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
  Envios/          Envio.cs, Bulto.cs, Destinatario.cs, Direccion.cs,
                   EventoEnvio.cs, IntentoEntrega.cs, PruebaEntrega.cs,
                   EstadoEnvio.cs, OrigenEvento.cs, ResultadoIntento.cs,
                   TablaTransiciones.cs
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
  Rutas/           Ruta.cs, Parada.cs, EstadoRuta.cs, EstadoParada.cs, CriterioOrden.cs
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

### 2.7 Lo que no es de ningún módulo — `Logistica.SharedKernel/`

Sólo piezas técnicas, sin conceptos de logística (addendum 1):

```
Logistica.SharedKernel/
  Entity.cs              clase base: Id y eventos de dominio
  IDomainEvent.cs
  DomainException.cs     error de regla de negocio
  IOperadorOwned.cs      "esta entidad pertenece a un operador"
  IComercioOwned.cs      "esta entidad pertenece a un comercio"
  ICurrentTenant.cs      "¿quién está haciendo este request?"
```

### 2.8 Tres reglas para las entidades

1. **Una entidad no referencia entidades de otro módulo, sólo su `Id`.** `Envio` guarda `ZonaId` (un `Guid`), no un objeto `Zona`. La zona es de Administración, y Envíos no puede ver su Domain.
2. **Los enums de negocio compartidos van en los `Contracts` del módulo dueño.** `Modalidad` (Estándar o Urgente) la define Administración, porque es parte de su configuración, y Envíos la usa desde `Logistica.Modules.Administracion.Contracts`.
3. **Las entidades protegen sus reglas.** Las propiedades tienen `private set` y el estado sólo cambia mediante métodos con nombre de negocio (`Envio.Transicionar`, `Ruta.Despachar`). Así ninguna otra parte del código puede dejar una entidad en un estado inválido.

---

## 3. Los archivos de CU-10, uno por uno

Este es el orden recomendado para construirlos: **de adentro hacia afuera**. Primero el dominio, que no depende de nada y se prueba solo; al final la pantalla.

```
src/
  BuildingBlocks/Logistica.SharedKernel/
    Entity.cs, DomainException.cs, IOperadorOwned.cs, IComercioOwned.cs, ICurrentTenant.cs   ← paso 1
  Modules/Envios/Logistica.Modules.Envios/
    Domain/Envios/
      Envio.cs, Bulto.cs, Destinatario.cs, Direccion.cs, EventoEnvio.cs,
      EstadoEnvio.cs, TablaTransiciones.cs                                                 ← paso 2
    Application/Features/CrearEnvio/
      CrearEnvioCommand.cs, CrearEnvioHandler.cs                                           ← paso 5
    Infrastructure/Persistence/
      EnviosDbContext.cs, Configurations/EnvioConfiguration.cs, Migrations/               ← paso 6
    Presentation/Features/CrearEnvio/
      CrearEnvioEndpoint.cs                                                                ← paso 7
    EnviosModule.cs  (ya existe: se completa)                                             ← paso 7
  Modules/Administracion/Logistica.Modules.Administracion.Contracts/
    IAdministracionModuleApi.cs, Modalidad.cs, Results/ZonaResumen.cs, Results/TarifaBulto.cs   ← paso 4
  Modules/Administracion/Logistica.Modules.Administracion/
    Application/AdministracionModuleApi.cs                                                ← paso 4
  Logistica.Http.Contracts/Envios/
    CrearEnvioRequest.cs, CrearEnvioResponse.cs                                            ← paso 7
  Logistica.PortalComercio/Pages/Envios/
    NuevoEnvio.razor                                                                       ← paso 8
tests/
  Logistica.UnitTests/Envios/EnvioTests.cs                                                 ← paso 3
  Logistica.IntegrationTests/Envios/CrearEnvioTests.cs                                     ← paso 9
```

### Paso 1 · Las piezas base, en `SharedKernel`

**Por qué ahí:** las usan todos los módulos y no tienen reglas de negocio. Si estuvieran en un módulo, los demás tendrían que depender de él.

```csharp
// SharedKernel/ICurrentTenant.cs
public interface ICurrentTenant
{
    Guid OperadorId { get; }
    Guid? ComercioId { get; }   // null si es personal del operador
}
```

### Paso 2 · La entidad `Envio`, en `Envios/Domain/Envios/`

**Por qué ahí:** `Envio` es la raíz del agregado central y su dueño es Envíos. Acá viven sus reglas: un envío necesita al menos un bulto, nace en estado *Admitido*, y sólo cambia de estado según la tabla de transiciones (RF 11).

```csharp
public sealed class Envio : Entity, IOperadorOwned, IComercioOwned
{
    private readonly List<Bulto> _bultos = [];
    private readonly List<EventoEnvio> _eventos = [];

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Numero { get; private set; } = "";
    public EstadoEnvio Estado { get; private set; }
    public decimal MontoTarifa { get; private set; }
    public IReadOnlyList<Bulto> Bultos => _bultos;

    private Envio() { }   // para EF Core

    public static Envio Crear(/* destinatario, dirección, bultos, zona, tarifa, versiones... */)
    {
        // valida las reglas: al menos un bulto, tarifa positiva, etc.
        // si algo no se cumple: throw new DomainException("...")
        // registra el evento inicial: estado anterior nulo → Admitido (T1)
    }

    public void Transicionar(EstadoEnvio nuevo, OrigenEvento origen, Guid? responsableId)
    {
        if (!TablaTransiciones.Permite(Estado, nuevo))
            throw new DomainException($"Transición inválida: {Estado} → {nuevo}");
        // registra el EventoEnvio y cambia el estado
    }
}
```

`Envio` no sabe que existe una base de datos ni HTTP: por eso no tiene ningún `using` de EF Core ni de ASP.NET Core. La prueba de arquitectura lo verifica.

### Paso 3 · Pruebas unitarias del dominio, en `tests/Logistica.UnitTests/Envios/`

**Por qué ahora:** el dominio no depende de nada, así que se prueba sin base de datos ni servidor, en milisegundos. Es lo que exige la sección 6.15 (*pruebas unitarias sobre la lógica de dominio*), y te asegura que las reglas funcionan antes de conectarlas a nada.

```csharp
[Fact]
public void Un_envio_entregado_no_puede_volver_a_transito()
{
    var envio = /* crear un envío y llevarlo a Entregado */;

    Assert.Throws<DomainException>(() =>
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.Backoffice, null));
}
```

### Paso 4 · El contrato de Administración, en `Administracion.Contracts`

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

### Paso 5 · El command y el handler, en `Envios/Application/Features/CrearEnvio/`

**Por qué ahí:** es un *vertical slice* (ADR-0001, sección 2.3): todo lo del caso de uso queda en una carpeta con su nombre. El handler **coordina** pero no decide: las reglas siguen en `Envio`.

```csharp
internal sealed record CrearEnvioCommand(/* datos del envío */);

internal sealed class CrearEnvioHandler(
    IAdministracionModuleApi administracion,
    ICurrentTenant tenant,
    EnviosDbContext db)
{
    public async Task<Envio> Handle(CrearEnvioCommand command, CancellationToken ct)
    {
        var zona = await administracion.ObtenerZona(command.CodigoPostal, ct)
                   ?? throw new DomainException("Dirección fuera de cobertura");      // CU-10, A2
        // calcular la tarifa de cada bulto (CU-10, paso 7)
        var envio = Envio.Crear(/* ... */);
        db.Envios.Add(envio);
        await db.SaveChangesAsync(ct);
        return envio;
    }
}
```

Es `internal`: nadie fuera del módulo puede usarlo (addendum 2).

### Paso 6 · La persistencia, en `Envios/Infrastructure/Persistence/`

**Por qué ahí:** EF Core es un detalle de infraestructura. Si mañana cambiara la base de datos, se cambia esta carpeta y el dominio no se toca.

```csharp
internal sealed class EnviosDbContext(DbContextOptions<EnviosDbContext> options) : DbContext(options)
{
    public DbSet<Envio> Envios => Set<Envio>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("envios");            // un schema por módulo (ADR-0003)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EnviosDbContext).Assembly);
    }
}
```

En `Configurations/EnvioConfiguration.cs` va el mapeo: `Destinatario` y `Direccion` como *owned types* (no tienen tabla propia), los bultos como tabla hija, y el índice único (`OperadorId`, `Numero`). Las migraciones se generan con `dotnet ef migrations add` y quedan en `Migrations/`.

### Paso 7 · El endpoint y el registro del módulo

**Por qué ahí:** el endpoint es la capa Presentation del módulo (addendum 2). Los DTOs `CrearEnvioRequest` y `CrearEnvioResponse` van en `Logistica.Http.Contracts`, porque el Portal (que corre en el navegador) también los necesita (addendum 5).

```csharp
// Envios/Presentation/Features/CrearEnvio/CrearEnvioEndpoint.cs
internal static class CrearEnvioEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/", async (CrearEnvioRequest request, CrearEnvioHandler handler, CancellationToken ct) =>
        {
            var envio = await handler.Handle(/* request → command */, ct);
            return Results.Created($"/api/envios/{envio.Numero}", /* envio → CrearEnvioResponse */);
        });
}
```

Y en `EnviosModule.cs` (ya existe) se registra todo:

```csharp
services.AddDbContext<EnviosDbContext>(/* cadena de conexión */);
services.AddScoped<CrearEnvioHandler>();
// ...
CrearEnvioEndpoint.Map(group);
```

`Logistica.Api` no cambia: ya llama a `AddEnviosModule()` y `MapEnviosEndpoints()`.

### Paso 8 · La pantalla, en `Logistica.PortalComercio/Pages/Envios/`

**Por qué ahí:** es la interfaz del comercio. Usa `HttpClient` para llamar a `/api/envios` con el mismo `CrearEnvioRequest`, y nunca referencia los módulos (la prueba de arquitectura lo verifica).

### Paso 9 · La prueba de integración, en `tests/Logistica.IntegrationTests/Envios/`

**Por qué:** prueba el recorrido completo de la sección 1 contra un PostgreSQL real (Testcontainers): hace el `POST`, después el `GET` del listado, y verifica que el envío aparece. Es exactamente lo que se demuestra el 8/10, y lo que pide la sección 6.15 (*pruebas de integración sobre al menos un flujo crítico*).

---

## 4. Lo provisorio para el 8/10

Para llegar al 8/10 alcanza con una versión mínima (catálogo, sección 3). Hay tres piezas que después se reemplazan:

| Pieza | Versión del 8/10 | Versión definitiva |
| :---- | :---- | :---- |
| `ICurrentTenant` | Un operador y un comercio fijos, leídos de la configuración | El claim de la cookie de Identity (15/10, Cristian) |
| Datos iniciales | Un operador, un comercio, una zona y un tarifario cargados al arrancar | Los mismos, más un segundo operador con configuración distinta (15/10) |
| Token de seguimiento | No se genera todavía | Token firmado (RF 25) |

---

## 5. Resumen: dónde va cada cosa

| Qué | Dónde | Por qué |
| :---- | :---- | :---- |
| Entidad, value object, enum de negocio | `Modulo/Domain/<Agregado>/` | Las reglas viven en un solo lugar, sin dependencias técnicas. |
| Enum de negocio que usa otro módulo | `Modulo.Contracts/` del dueño | Es lo único que otro módulo puede ver. |
| Command y handler | `Modulo/Application/Features/<CasoDeUso>/` | Un caso de uso por carpeta (vertical slice). |
| Interfaz para otros módulos | `Modulo.Contracts/I<Modulo>ModuleApi.cs` | Comunicación síncrona entre módulos (addendum 3). |
| Su implementación | `Modulo/Application/` (`internal`) | El interior del módulo queda oculto. |
| `DbContext`, mapeos, migraciones | `Modulo/Infrastructure/Persistence/` | EF Core es un detalle técnico. |
| Endpoint | `Modulo/Presentation/Features/<CasoDeUso>/` | El caso de uso completo queda en el módulo. |
| Página del Backoffice | `Modulo/Presentation/Features/<CasoDeUso>/` | Ídem (addendum 4). |
| DTO de la API HTTP | `Logistica.Http.Contracts/<Modulo>/` | Lo comparten el servidor y las apps del navegador. |
| Pieza técnica común a todos | `SharedKernel` o `BuildingBlocks.Infrastructure` | Sin lógica de negocio (addendum 1). |
| Prueba de reglas | `tests/Logistica.UnitTests/<Modulo>/` | Rápida, sin infraestructura. |
| Prueba del recorrido completo | `tests/Logistica.IntegrationTests/<Modulo>/` | Contra una base real. |
