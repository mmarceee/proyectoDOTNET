# **ADR 001 · Addendum 1 · Código compartido entre módulos (BuildingBlocks)**

| Estado | Propuesto — pendiente de revisión por el responsable del ADR-001 |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Responsable del ADR-001** | Lucas Ottonello |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Modifica** | ADR-001, sección 2.2 (Organización interna) y sección 2.4 (Regla de dependencias) |
| **Relacionado con** | ADR-0002 · Estrategia de multitenancy |

**Este addendum incorpora a la estructura del ADR-001 dos proyectos de código compartido, `Logistica.SharedKernel` y `Logistica.BuildingBlocks.Infrastructure`, para alojar los mecanismos técnicos que todos los módulos necesitan de forma idéntica, en particular el aislamiento entre inquilinos exigido por el ADR-0002.**

# **1\. Contexto**

El ADR-001 define que cada módulo se implementa con dos proyectos (el módulo y su `Contracts`) y que un módulo sólo puede referenciar el proyecto `Contracts` de otro. No define dónde ubicar el código técnico que **todos** los módulos necesitan por igual.

El ADR-0002 introduce justamente ese tipo de código:

•  Un servicio `ICurrentTenant` que expone el operador (y, cuando corresponde, el comercio) del request actual.  
•  Global Query Filters de EF Core por `OperadorId` y, condicionalmente, por `ComercioId`.  
•  Un `SaveChangesInterceptor` que asigna el `OperadorId` a las entidades nuevas y rechaza escrituras con un `OperadorId` ajeno o modificado.  
•  Una prueba estructural que verifique que toda entidad con `OperadorId` tiene su filtro configurado.

A esto se suma el patrón Outbox definido en la Guía de decisiones tecnológicas (sección 6.2), que también utilizan todos los módulos para publicar eventos.

Como cada módulo tiene su propio `DbContext`, sin un lugar común estos mecanismos deberían reescribirse en los seis módulos. Tratándose del mecanismo que impide la filtración de datos entre operadores, seis copias independientes son un riesgo inaceptable: basta con que una de ellas quede desactualizada o tenga un error para que exista una fuga real.

# **2\. Decisión**

Se agregan dos proyectos de biblioteca bajo `src/BuildingBlocks/`:

src/  
  BuildingBlocks/  
    Logistica.SharedKernel/  
    Logistica.BuildingBlocks.Infrastructure/  
  Modules/  
    ...

## **2.1 Logistica.SharedKernel**

Proyecto de C# puro, **sin paquetes NuGet ni dependencias de frameworks**. Contiene contratos y tipos base:

•  Clases base del dominio: `Entity`, `AggregateRoot`, `IDomainEvent`, `DomainException`.  
•  Marcadores de pertenencia a un inquilino: `IOperadorOwned` (expone `OperadorId`) e `IComercioOwned` (expone `ComercioId`).  
•  La **interfaz** `ICurrentTenant` y las excepciones de aislamiento (por ejemplo `TenantMismatchException`).  
•  El tipo base de los eventos de integración publicados en los proyectos `Contracts`.

## **2.2 Logistica.BuildingBlocks.Infrastructure**

Proyecto que depende de `Logistica.SharedKernel` y de Entity Framework Core. Contiene la implementación técnica compartida:

•  `TenantSaveChangesInterceptor`: implementación del interceptor definido en el ADR-0002, sección 2\.  
•  `ApplyTenantQueryFilters()`: extensión de `ModelBuilder` que recorre las entidades que implementan `IOperadorOwned` / `IComercioOwned` y les aplica el `HasQueryFilter` correspondiente, incluida la condición sobre `ComercioId`.  
•  Entidad y configuración de la tabla Outbox.  
•  Una clase base `ModuleDbContext` que registra el interceptor, los filtros y el Outbox, de la cual heredan los `DbContext` de los módulos.

## **2.3 Implementaciones de ICurrentTenant**

Las implementaciones concretas de `ICurrentTenant` **no** se ubican en el código compartido, porque dependen de cómo llega la identidad del inquilino a cada proceso:

•  **Logistica.Api**: middleware que toma el `OperadorId` y el `ComercioId` de los claims del usuario autenticado, o del token firmado en el seguimiento público (ADR-0002, sección 2).  
•  **Logistica.Worker**: toma el inquilino de los metadatos del mensaje recibido desde RabbitMQ.

De este modo el código compartido no depende de ASP.NET Core.

## **2.4 Regla de dependencias (agrega a la sección 2.4 del ADR-001)**

| Proyecto | Puede referenciar |
| :---- | :---- |
| Logistica.SharedKernel | Nada |
| Logistica.BuildingBlocks.Infrastructure | Logistica.SharedKernel y Entity Framework Core |
| Logistica.Modules.X | Su propio `Contracts`, `SharedKernel` y `BuildingBlocks.Infrastructure` |
| Logistica.Modules.X.Contracts | `SharedKernel` |
| Logistica.Api y Logistica.Worker | Los proyectos que ya definía el ADR-001 y `BuildingBlocks.Infrastructure` |

Además:

•  Ningún proyecto de `BuildingBlocks` puede referenciar a un módulo.  
•  El Domain de cada módulo puede usar `SharedKernel`, pero no `BuildingBlocks.Infrastructure` (ya cubierto por la regla existente: Domain no depende de Entity Framework Core).

## **2.5 Criterio de admisión**

Para evitar que el código compartido se transforme en un depósito de clases sin dueño, sólo se incorpora un tipo a `BuildingBlocks` si cumple **todas** estas condiciones:

1\.  Es necesario en dos o más módulos.

2\.  Es puramente técnico: no contiene reglas de negocio ni conceptos del dominio logístico.

3\.  Su comportamiento debe ser idéntico en todos los módulos que lo usan.

Regla práctica: si una clase del código compartido menciona envíos, rutas, comercios, operadores como entidad de negocio, tarifas o cualquier otro concepto del modelo de dominio, está mal ubicada. `Operador`, `Comercio` y `RelacionComercial` permanecen en el módulo de Administración y configuración.

# **3\. Alternativas consideradas**

## **3.1 Un único proyecto SharedKernel con todo el código compartido**

**Motivo de descarte:** obligaría a que `SharedKernel` dependa de Entity Framework Core, y esa dependencia llegaría de forma transitiva a los proyectos `Contracts` y quedaría disponible para el código de dominio. Aunque la prueba de arquitectura seguiría detectando el uso indebido, se elimina la barrera física que hoy impide usar EF Core desde el dominio sin agregar una referencia.

## **3.2 Tres proyectos (BuildingBlocks.Domain, .Application e .Infrastructure)**

**Motivo de descarte:** replica las capas de Clean Architecture en el código compartido sin un beneficio concreto para el alcance actual. Agrega ceremonia, contra lo indicado en el ADR-001, sección 4.2. Si en el futuro aparecieran abstracciones de aplicación compartidas con dependencias propias, se podrá separar un tercer proyecto mediante un nuevo addendum.

## **3.3 Ubicar el código compartido dentro del módulo de Administración**

**Motivo de descarte:** todos los módulos pasarían a depender de la implementación interna de Administración, lo que contradice la regla del ADR-001 según la cual un módulo sólo puede referenciar el proyecto `Contracts` de otro.

## **3.4 Implementar los mecanismos en cada módulo**

**Motivo de descarte:** seis copias del mecanismo de aislamiento entre inquilinos. Cualquier divergencia entre ellas es una filtración de datos entre operadores, exactamente lo que el ADR-0002 y la letra (sección 6.5) exigen demostrar que no ocurre.

# **4\. Consecuencias**

## **4.1 Positivas**

•  El aislamiento entre inquilinos se implementa y se prueba una sola vez.  
•  Una entidad queda protegida con sólo implementar `IOperadorOwned`: quien desarrolla un módulo no escribe filtros ni validaciones de inquilino, y no puede olvidarse de hacerlo.  
•  La prueba estructural exigida por el ADR-0002, sección 4, se simplifica: basta con verificar que cada `DbContext` hereda de `ModuleDbContext` y que toda entidad con `OperadorId` implementa `IOperadorOwned`.  
•  El dominio sigue libre de frameworks: `SharedKernel` no tiene dependencias externas.

## **4.2 Negativas y riesgos**

•  Un cambio en `BuildingBlocks` impacta a todos los módulos a la vez. Los cambios en estos proyectos deben revisarse con especial cuidado y estar cubiertos por pruebas.  
•  Riesgo de crecimiento desordenado del código compartido. Se mitiga con el criterio de admisión de la sección 2.5 y con la revisión de código.

# **5\. Verificación**

La decisión se considera verificada cuando:

1\.  Existen los proyectos `Logistica.SharedKernel` y `Logistica.BuildingBlocks.Infrastructure` con las referencias de la sección 2.4.

2\.  Existe una prueba automatizada que falla si `SharedKernel` depende de Entity Framework Core, ASP.NET Core, `Microsoft.Extensions`, Npgsql o `BuildingBlocks.Infrastructure`.

3\.  Existe una prueba automatizada que falla si algún proyecto de `BuildingBlocks` depende de un módulo.

4\.  Ambas pruebas se ejecutan en el pipeline de integración continua.

Los puntos 1 a 4 ya están implementados en el esqueleto de la solución (`tests/Logistica.ArchitectureTests/BuildingBlocksDependencyTests.cs`). La implementación del interceptor, los filtros y el Outbox corresponde al hito del 15 de octubre.

# **6\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Propuesta inicial del addendum. | Ezequiel Marcenal |
