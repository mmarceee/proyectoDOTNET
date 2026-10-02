# **ADR-0001 · Addendum 4 · Ejecución del Backoffice y ubicación de sus páginas**

| Estado | Aceptado |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Responsable del ADR-0001** | Lucas Ottonello |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Modifica** | ADR-0001, sección 2.2 (Organización interna) |
| **Relacionado con** | ADR-0001 · Addendum 2 · Ubicación de los endpoints, los casos de uso y los contratos; ADR-0002 · Estrategia de multitenancy; Guía de decisiones tecnológicas, sección 4.1 |

**Este addendum resuelve la decisión abierta del addendum 2: el Backoffice se ejecuta en el mismo proceso que la API, como una biblioteca Razor cargada por `Logistica.Api`, y las páginas de cada caso de uso viven en la capa `Presentation` del módulo correspondiente, junto a sus endpoints.**

# **1\. Contexto**

El Backoffice se implementa con Razor Pages, por exigencia de la letra. A diferencia del portal del comercio, el seguimiento público y la aplicación del repartidor, que son aplicaciones Blazor WebAssembly ejecutadas en el navegador y llaman a la API por HTTP, las páginas del Backoffice se generan en el servidor. Cada página necesita ejecutar casos de uso de los módulos.

El addendum 2 estableció que los commands, queries y handlers de cada módulo son `internal`, y que un caso de uso queda completo dentro de su módulo. Con esa regla, una página ubicada en el proyecto `Logistica.Backoffice` no puede invocar los handlers. Hay que decidir:

1\.  En qué proceso se ejecuta el Backoffice.

2\.  Cómo llegan sus páginas a los casos de uso.

# **2\. Decisión**

## **2.1 El Backoffice se ejecuta en el mismo proceso que la API**

`Logistica.Backoffice` deja de ser una aplicación web independiente y pasa a ser una **biblioteca Razor** (Razor Class Library) referenciada y servida por `Logistica.Api`. La API y el Backoffice forman un único host web:

•  Una única autenticación con cookie de Identity y un único middleware de resolución del inquilino (ADR-0002).  
•  El hub de SignalR del tablero operativo vive en el mismo proceso que las páginas que lo usan.  
•  Un servicio menos para desplegar en App Platform.

Esto es coherente con la restricción de la letra, recogida en el ADR-0001, según la cual la API y las aplicaciones web forman un monolito modular.

## **2.2 Las páginas viven en la capa Presentation de cada módulo**

Las páginas del Backoffice de un caso de uso se ubican junto a su endpoint, en `Presentation/Features/<CasoDeUso>/`:

Logistica.Modules.Administracion/  
  Application/  
    Features/  
      AltaZona/  
        AltaZonaCommand.cs  
        AltaZonaHandler.cs  
  Presentation/  
    _ViewImports.cshtml  
    Features/  
      AltaZona/  
        AltaZonaEndpoint.cs  
        AltaZona.cshtml  
        AltaZona.cshtml.cs

•  El `PageModel` puede ser `internal` e invocar directamente los handlers `internal` del módulo.  
•  Cada proyecto de módulo usa el SDK de Razor (`Microsoft.NET.Sdk.Razor` con `AddRazorSupportForMvc`).  
•  El `_ViewImports.cshtml` de `Presentation/` registra los tag helpers para las páginas del módulo.

## **2.3 Contenido de Logistica.Backoffice**

La biblioteca `Logistica.Backoffice` contiene sólo lo común a todas las pantallas:

•  El layout (`Pages/Shared/_Layout.cshtml`), el menú y los parciales compartidos.  
•  Un `_ViewStart.cshtml` en la raíz de la biblioteca, que asigna el layout a todas las páginas, incluidas las de los módulos.  
•  Las páginas que no pertenecen a ningún módulo, como la página de inicio (`/backoffice`).  
•  Los archivos estáticos (Bootstrap, estilos y scripts), servidos bajo `/_content/Logistica.Backoffice/`.

El Backoffice no referencia a ningún módulo.

## **2.4 Descubrimiento y rutas de las páginas**

Por defecto, ASP.NET Core sólo descubre páginas dentro de la carpeta `Pages/`. Para descubrir las páginas ubicadas en `Presentation/Features/`, `Logistica.Api` configura la raíz de Razor Pages en `/`:

builder.Services.AddRazorPages(options \=\> options.RootDirectory \= "/");

Como consecuencia, **toda página declara su ruta absoluta** en la directiva `@page`, siempre bajo el prefijo `/backoffice`:

@page "/backoffice/zonas/alta"

Así las rutas quedan explícitas en cada página, del mismo modo que los endpoints declaran las suyas.

## **2.5 Regla de dependencias (agrega a la sección 2.4 del ADR-0001)**

Razor compila cada archivo `.cshtml` en una clase del namespace `AspNetCoreGeneratedDocument`, fuera del namespace del módulo, por lo que las reglas por namespace no alcanzan a las vistas. Se agregan dos reglas:

•  Las vistas compiladas de un módulo no pueden depender de la capa Infrastructure de ese módulo ni de los namespaces internos de otros módulos.  
•  `Logistica.Backoffice` no puede depender de ningún módulo.

# **3\. Alternativas consideradas**

## **3.1 Backoffice como proceso separado que llama a la API por HTTP**

**Motivo de descarte:** obliga a reenviar la identidad del usuario en cada llamada, a resolver el inquilino en dos procesos, a mantener un cliente HTTP y DTOs duplicados por caso de uso, y a desplegar un servicio más. Su única ventaja, desplegar y escalar el Backoffice de forma independiente, no es necesaria: el equipo está exento del requisito de escalado horizontal.

## **3.2 Backoffice como proceso separado que referencia directamente los módulos**

**Motivo de descarte:** dos procesos tendrían sus propios `DbContext`, Outbox y caché sobre la misma base de datos, con riesgos de consistencia y el doble de configuración.

## **3.3 Todas las páginas en Logistica.Backoffice, con acceso a los tipos internos de los módulos**

Las páginas se ubicarían en el proyecto del Backoffice, y cada módulo declararía `InternalsVisibleTo` hacia él.

**Motivo de descarte:** abre los tipos internos de los seis módulos a otro proyecto de producción, y cada caso de uso vuelve a quedar repartido en dos proyectos, que es lo que el addendum 2 buscó evitar. Es más simple de entender, pero debilita el encapsulamiento.

## **3.4 Páginas en un Área de Razor Pages por módulo**

Cada módulo ubicaría sus páginas en `Areas/<Modulo>/Pages/`, el mecanismo estándar de ASP.NET Core para separar páginas por sección.

**Motivo de descarte:** no requiere cambiar la raíz de Razor Pages, pero separa la página de su endpoint y de su handler dentro del módulo. Se prefiere mantener todo el caso de uso en `Presentation/Features/<CasoDeUso>/`, al costo de declarar rutas absolutas.

# **4\. Consecuencias**

## **4.1 Positivas**

•  Un caso de uso queda completo en su módulo: handler, endpoint y página.  
•  Los handlers siguen siendo `internal`.  
•  Una sola autenticación, un solo middleware de inquilino y un solo despliegue.  
•  Las rutas del Backoffice quedan explícitas en cada página.

## **4.2 Negativas y riesgos**

•  Los módulos usan el SDK de Razor, y las páginas del Backoffice quedan repartidas en seis proyectos.  
•  Con la raíz de Razor Pages en `/`, cualquier `.cshtml` con `@page` de cualquier proyecto referenciado por la API se convierte en una página. Toda página debe declarar su ruta absoluta bajo `/backoffice`; se controla en la revisión de código.  
•  El Backoffice no puede desplegarse ni escalarse por separado de la API.

# **5\. Verificación**

1\.  `Logistica.Backoffice` es una biblioteca Razor sin `Program.cs`, referenciada por `Logistica.Api`.

2\.  `GET /backoffice` responde con el layout del Backoffice (`tests/Logistica.IntegrationTests/BackofficeTests.cs`).

3\.  Existen pruebas de arquitectura para las vistas compiladas de los módulos y para el Backoffice (`tests/Logistica.ArchitectureTests/BackofficeDependencyTests.cs`).

Los puntos 1 a 3 ya están implementados. Además se verificó, en una copia temporal de la solución, que:

•  Una página en `Administracion/Presentation/Features/AltaZona/`, con un `PageModel` `internal` que invoca un handler `internal`, se sirve en `/backoffice/zonas/alta` con el layout del Backoffice y el token antiforgery.  
•  Si esa página inyecta un tipo de `Infrastructure`, la prueba de arquitectura falla.

# **6\. Decisiones relacionadas pendientes**

| Tema | Por qué importa |
| :---- | :---- |
| ~~Servir las aplicaciones Blazor WebAssembly desde el mismo host~~ | Resuelta en el addendum 5: `Logistica.Api` sirve las tres aplicaciones bajo `/portal`, `/seguimiento` y `/repartidor`. |

# **7\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Propuesta inicial del addendum. | Ezequiel Marcenal |
| 0.2 | 30/09/2026 | Se da por resuelta la decisión pendiente sobre las aplicaciones WebAssembly (addendum 5). | Ezequiel Marcenal |
| 0.3 | 02/10/2026 | Revisado y aceptado por el responsable del ADR-0001. | Lucas Ottonello |
