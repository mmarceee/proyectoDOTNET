# Guía: el aislamiento por inquilino

Esta guía explica cómo está implementado el aislamiento entre operadores (ADR-0002) y cómo usarlo al escribir código nuevo. Cubre lo hecho para el hito del 15/10: el filtro global de lecturas, el interceptor de escrituras y las pruebas de aislamiento.

> Las reglas salen del [ADR-0002](analisis-diseno/ADR-0002-estrategia-de-multitenancy.md). Los fragmentos de código son versiones abreviadas; cada sección indica dónde está el archivo completo.

---

## 1. Qué es un inquilino

La plataforma es multioperador: varios operadores logísticos usan el mismo despliegue y la misma base de datos. El **inquilino** es el operador dueño de los datos, y ningún operador puede ver ni tocar los datos de otro.

Hay dos niveles:

| Quién entra | `OperadorId` | `ComercioId` | Qué ve |
| :---- | :---- | :---- | :---- |
| Personal del operador (Backoffice) | el suyo | `null` | Todo lo de su operador, de todos sus comercios |
| Usuario de un comercio (Portal, API pública) | el elegido al iniciar sesión | el suyo | Sólo lo de su comercio con ese operador |
| Nadie (sin inquilino resuelto) | `null` | — | **Nada** (falla cerrado) |

En el código:

- **Cada fila sabe de quién es.** La entidad lleva `OperadorId` e implementa `IOperadorOwned`; si también la consulta el comercio, lleva `ComercioId` e implementa `IComercioOwned`.
- **Cada request sabe quién es el inquilino.** Lo expone `ICurrentTenant` (scoped). Hoy lo implementa `TenantProvisorio`; cuando esté Identity, saldrá de los claims de la cookie.
- `Operador` y `Comercio` son **globales**: no implementan los marcadores. `Usuario` (Identity) tampoco tiene filtro (ADR-0002, sección 2.4).

---

## 2. Las piezas

```
 ICurrentTenant (scoped: uno por request)
   │  lo recibe cada DbContext de módulo en el constructor
   ▼
 ModuleDbContext  (BuildingBlocks)
   ├─ Lecturas:   filtro global "Tenant" en toda entidad IOperadorOwned
   └─ Escrituras: TenantSaveChangesInterceptor en cada SaveChanges
```

Un módulo no escribe filtros ni validaciones de inquilino: le alcanza con heredar de `ModuleDbContext` y marcar sus entidades.

### 2.1 Lecturas: el filtro "Tenant"

Archivo: [`ModuleDbContext.cs`](../src/BuildingBlocks/Logistica.BuildingBlocks.Infrastructure/Persistence/ModuleDbContext.cs)

```csharp
public const string FiltroTenant = "Tenant";

private Guid? OperadorIdActual => tenant.OperadorId;
private Guid? ComercioIdActual => tenant.ComercioId;

// En OnModelCreating, para cada entidad del modelo:
if (typeof(IComercioOwned).IsAssignableFrom(entityType.ClrType))
    AplicarFiltro(nameof(AplicarFiltroComercio), entityType.ClrType, modelBuilder);
else if (typeof(IOperadorOwned).IsAssignableFrom(entityType.ClrType))
    AplicarFiltro(nameof(AplicarFiltroOperador), entityType.ClrType, modelBuilder);

private void AplicarFiltroComercio<TEntity>(ModelBuilder modelBuilder)
    where TEntity : class, IOperadorOwned, IComercioOwned
{
    modelBuilder.Entity<TEntity>().HasQueryFilter(FiltroTenant,
        e => OperadorIdActual != null && e.OperadorId == OperadorIdActual &&
            (ComercioIdActual == null || e.ComercioId == ComercioIdActual));
}
```

Las decisiones que hay que poder explicar:

1. **El filtro lee propiedades del contexto, nunca un valor capturado.** EF construye el modelo una sola vez y lo reutiliza. Si el filtro usara `var id = tenant.OperadorId;` leído en `OnModelCreating`, quedaría fijo con el operador del primer request. Al referenciar `OperadorIdActual` (un miembro del DbContext), EF lo trata como parámetro y lo vuelve a leer del contexto que ejecuta cada consulta.
2. **Método genérico más `MakeGenericMethod`.** `HasQueryFilter` necesita una lambda tipada (`Expression<Func<TEntity, bool>>`), pero el recorrido del modelo da un `Type`. El método genérico se llama por reflexión con el tipo de cada entidad. Es **de instancia** (no `static`) para que la lambda capture `this`.
3. **Primero `IComercioOwned`, después `IOperadorOwned`.** Toda entidad del comercio también es del operador. Si se preguntara primero por `IOperadorOwned`, las entidades del comercio recibirían sólo el filtro de operador y el comercio vería lo de los demás comercios.
4. **Un solo filtro con nombre.** EF Core 10 admite varios filtros por entidad, identificados por nombre. Con el nombre `"Tenant"`, un borrado lógico futuro no lo reemplaza, e `IgnoreQueryFilters([FiltroTenant])` lo saca entero. Por eso operador y comercio van en la misma lambda y no en dos filtros.
5. **Paréntesis en la condición del comercio.** `&&` tiene más precedencia que `||`. Sin paréntesis, la condición se leería `(… && ComercioIdActual == null) || e.ComercioId == …`, y un `ComercioId` conocido saltearía el filtro del operador.
6. **Falla cerrado.** `OperadorIdActual != null` hace que, sin operador, no coincida ninguna fila.

### 2.2 Escrituras: `TenantSaveChangesInterceptor`

Archivo: [`TenantSaveChangesInterceptor.cs`](../src/BuildingBlocks/Logistica.BuildingBlocks.Infrastructure/Persistence/TenantSaveChangesInterceptor.cs)

Corre **antes** de que EF mande los INSERT, UPDATE y DELETE. Si lanza `TenantMismatchException`, no se escribe nada. Recorre las entidades `IOperadorOwned` que se van a escribir y aplica:

| Situación | Resultado |
| :---- | :---- |
| No hay operador en la sesión | Excepción (falla cerrado) |
| **Regla 1.** Alta con `OperadorId` vacío | Se asigna el de la sesión |
| **Regla 2.** Modificación que cambia `OperadorId` | Excepción: una entidad no cambia de inquilino |
| **Regla 3.** Alta, modificación o baja de una entidad de otro operador | Excepción |
| Las mismas tres reglas con `ComercioId` | Sólo si la entidad es `IComercioOwned` **y** la sesión trae comercio |

Las decisiones:

- **Sobrescribe `SavingChanges` y `SavingChangesAsync`.** EF llama a una o a otra según se use `SaveChanges()` o `SaveChangesAsync()`; si faltara una, ese camino no validaría.
- **`ChangeTracker.Entries()` llama a `DetectChanges()`.** Por eso aparecen como `Added` los bultos agregados con `envio.Bultos`, aunque nadie haya hecho `db.Bultos.Add`.
- **Asigna por el change tracker.** `entry.Property("OperadorId").CurrentValue = …` funciona aunque el setter sea `private`. Para la regla 2 compara `OriginalValue` (lo que vino de la base) con `CurrentValue`.
- **No guarda estado.** Lee el inquilino de `eventData.Context`. Por eso `ModuleDbContext` registra **una sola instancia estática** en `OnConfiguring`: si cada contexto registrara la suya, EF armaría un proveedor de servicios interno por contexto.
- **No valida el comercio del personal del operador.** Si el personal crea un envío para un comercio, que el comercio tenga relación activa con el operador es una regla del caso de uso, no de BuildingBlocks (ADR-0002, sección 2.3).

**Lo que no cubre** (ADR-0002, sección 4): `ExecuteUpdate`, `ExecuteDelete` y el SQL crudo, porque no pasan por el change tracker. El filtro sí agrega el `WHERE` del inquilino a `ExecuteUpdate` y `ExecuteDelete`, pero el interceptor no los valida.

### 2.3 `InquilinoFijo`

Archivo: [`InquilinoFijo.cs`](../src/BuildingBlocks/Logistica.BuildingBlocks.Infrastructure/Tenancy/InquilinoFijo.cs)

```csharp
public sealed record InquilinoFijo(Guid? OperadorId, Guid? ComercioId) : ICurrentTenant;
```

Es un inquilino elegido a mano, para cuando no hay un request del que leerlo: el seed de cada operador, los mensajes del Worker y las pruebas.

---

## 3. Cómo usarlo

### Una entidad nueva de un operador

Implementar los marcadores y nada más:

```csharp
internal sealed class Ruta : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    // ...
}
```

El filtro y el interceptor la alcanzan solos. Si el DbContext del módulo sobrescribe `OnModelCreating`, **tiene que llamar a `base.OnModelCreating(modelBuilder)`**; si no, el filtro no se aplica. La prueba estructural lo detecta.

### Consultas: no filtrar a mano

```csharp
// Bien: el filtro global agrega el operador y el comercio.
db.Envios.Where(e => e.Numero == numero)

// Mal: duplica el filtro y obliga a recibir ICurrentTenant.
db.Envios.Where(e => e.OperadorId == tenant.OperadorId && e.Numero == numero)
```

Sí se dejan las comparaciones **entre filas**, que son reglas de la consulta y no del inquilino. Por ejemplo, en `ArchivoEvidenciaReader`, `e.ComercioId == a.ComercioId` verifica que el archivo sea de ese envío.

`IgnoreQueryFilters()` está **prohibido** fuera de la lista cerrada del ADR-0002, sección 2.4: seed, pruebas, listar los operadores de un comercio al iniciar sesión y buscar la `ClaveApi` por su hash.

### Escribir datos de otro operador (seed, Worker, pruebas)

Se arma un contexto con ese inquilino. El resto de las dependencias (opciones, unidad de trabajo) sale del contenedor:

```csharp
await using var db = ActivatorUtilities.CreateInstance<AdministracionDbContext>(
    serviceProvider, new InquilinoFijo(segundoOperadorId, null));
```

Con el contexto del request, el interceptor rechaza la escritura con `TenantMismatchException`.

### Respuesta HTTP ante otro inquilino

Leer un recurso ajeno da `null`, igual que uno inexistente, y el endpoint responde **404 genérico**. Según el ADR-0002 (sección 2.9), una `TenantMismatchException` en una escritura se registra en el log para auditoría y también responde 404, nunca 403. Esa traducción todavía no está implementada (sección 5).

---

## 4. Las pruebas

Archivo: [`AislamientoTests.cs`](../tests/Logistica.IntegrationTests/Tenancy/AislamientoTests.cs). Usan los DbContext reales de los módulos contra un PostgreSQL real (Testcontainers). Cada prueba crea sus propios operadores.

| Prueba | Qué demuestra |
| :---- | :---- |
| `Cada_operador_ve_solo_sus_envios_en_el_listado_y_en_el_detalle` | A no ve lo de B, ni B lo de A, con contextos distintos: el inquilino no queda congelado en el modelo |
| `El_comercio_ve_solo_sus_envios_y_el_personal_del_operador_ve_los_de_todos_sus_comercios` | Las dos ramas del filtro del comercio |
| `Sin_operador_en_la_sesion_no_se_ve_nada_en_ningun_modulo` | Falla cerrado en Envíos, Depósito y Administración |
| `Cada_operador_ve_solo_sus_recepciones_en_deposito` | El filtro en el DbContext de Depósito |
| `Cada_operador_ve_solo_sus_relaciones_comerciales` | El filtro en el DbContext de Administración |
| `No_se_puede_guardar_un_envio_de_otro_operador` | Regla 3 |
| `Sin_operador_en_la_sesion_no_se_puede_guardar` | Falla cerrado en las escrituras |
| `Un_comercio_no_puede_guardar_un_envio_de_otro_comercio` | Regla 3 con el comercio |
| `No_se_puede_cambiar_el_operador_de_un_envio_existente` | Regla 2 |
| `Un_alta_sin_operador_ni_comercio_los_toma_de_la_sesion` | Regla 1, también en los bultos agregados por navegación |
| `En_cada_DbContext_de_los_modulos_toda_entidad_de_un_operador_tiene_el_filtro_Tenant` | Prueba estructural: el filtro está en el modelo real de cada módulo |

Para escribir una prueba con otro inquilino, el inquilino se le da **al contexto**, no al reader:

```csharp
var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider, new InquilinoFijo(operadorId, null));
var reader = new EnvioDetalleReader(db);
```

Para verlas fallar: comentar el `throw` de la regla 3 en el interceptor y correr `bash scripts/test-en-docker.sh`.

---

## 5. Pendientes

- **Segundo operador en el seed** (Cristian): cargarlo con un contexto con `InquilinoFijo`, como en la sección 3. Si no, el interceptor rechaza el seed.
- **Identity** (Cristian): reemplazar `TenantProvisorio` por una implementación que lea los claims de la cookie. El filtro y el interceptor no cambian, porque sólo dependen de `ICurrentTenant`.
- **Texto de los ADR:** el ADR-0002 (secciones 2.1 y 2.2) y el ADR-0001 hablan de una extensión `ApplyTenantQueryFilters()`. La implementación aplica el filtro dentro de `ModuleDbContext.OnModelCreating`, y las dos reglas que el ADR exige se cumplen igual. Hay que corregir el texto.
- **Prueba estructural, parte (a)** (ADR-0002, sección 4): que toda entidad con una propiedad `OperadorId` implemente `IOperadorOwned`, salvo el Outbox y `Usuario`.
- **Traducir `TenantMismatchException` a 404** en el manejo de errores de la API, con registro en el log.
- **Regla de arquitectura** que impida `IgnoreQueryFilters`, `ExecuteUpdate`, `ExecuteDelete` y el SQL crudo fuera de los lugares autorizados (ADR-0002, sección 4, a confirmar).

---

## 6. Preguntas para la defensa

- ¿Por qué el filtro usa `OperadorIdActual` y no un valor leído en `OnModelCreating`?
- ¿Por qué el `if` pregunta primero por `IComercioOwned`?
- ¿Qué pasa si no hay inquilino, en una lectura y en una escritura?
- ¿Por qué hace falta el interceptor si ya existe el filtro?
- ¿Qué operaciones no protege el interceptor y cómo se controlan?
- ¿Por qué el interceptor es una sola instancia estática?
- ¿Por qué un recurso de otro inquilino responde 404 y no 403?

---

## Historial de versiones

| Versión | Fecha | Descripción |
| :---: | :---: | :---- |
| 1.0 | 09/10/2026 | Versión inicial: filtro global "Tenant", `TenantSaveChangesInterceptor`, `InquilinoFijo`, retiro de los filtros manuales y pruebas de aislamiento. |
