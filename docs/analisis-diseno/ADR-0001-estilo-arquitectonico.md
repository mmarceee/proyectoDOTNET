# **ADR-0001 · Estilo arquitectónico interno y organización del código**

| Estado | Aceptado (versión 1.0). La versión 2.0 incorpora los addenda 1 a 5, pendientes de revisión |
| :---- | :---- |
| **Versión** | 2.0 |
| **Fecha** | 24 de septiembre de 2026 (versión 1.0) · 1 de octubre de 2026 (versión 2.0) |
| **Responsable** | Lucas Ottonello |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Organización interna del monolito modular |

**Este registro propone organizar la solución como un monolito modular por capacidades de negocio, con casos de uso estructurados verticalmente y dependencias orientadas hacia el dominio. La decisión busca reducir el acoplamiento y facilitar la incorporación del cambio de requerimientos previsto para el 29 de octubre.**

La versión 2.0 integra en el texto las decisiones de los addenda 1 a 5: código compartido entre módulos, ubicación de los endpoints y de los casos de uso, comunicación entre módulos y biblioteca de pruebas de arquitectura, ejecución del Backoffice y aplicaciones Blazor WebAssembly en el mismo host. Cada sección modificada indica el addendum del que proviene.

# **1\. Contexto y restricciones**

El sistema a construir es una plataforma multioperador de distribución de última milla. Debe atender a varios operadores logísticos en un único despliegue, manteniendo aislados sus datos, usuarios, configuraciones y reglas de negocio.

La letra del laboratorio establece las siguientes restricciones arquitectónicas:

* La solución se desarrollará en .NET 10\.

* La API y las aplicaciones web formarán un monolito modular.

* Existirá un servicio de procesamiento en segundo plano (worker) desplegado de forma independiente.

* El worker se comunicará con el resto de la solución de forma asíncrona mediante una cola de mensajes.

* La lógica de dominio no podrá depender de la infraestructura, del acceso a datos ni de los frameworks de presentación.

* La regla de dependencias deberá verificarse mediante una prueba automatizada de arquitectura ejecutada por el pipeline.

* El entorno completo deberá poder ejecutarse con Docker Compose.

* El 29 de octubre el equipo recibirá un cambio de requerimientos no anunciado que deberá incorporar obligatoriamente.

El dominio incluye áreas con responsabilidades diferentes, entre ellas tenencia y usuarios, configuración del operador, envíos, planificación de rutas, ejecución de entregas, notificaciones y liquidaciones. Si estas áreas se implementan sin límites claros, un cambio funcional puede afectar archivos y reglas distribuidos por toda la solución.

Por tanto, se necesita una organización que:

* Mantenga la lógica de negocio independiente de ASP.NET Core, Entity Framework Core, la base de datos, la cola y otros servicios externos.

* Permita ubicar rápidamente el código correspondiente a una capacidad o caso de uso.

* Reduzca el impacto de cambios en una parte del negocio sobre las demás.

* Evite que clases de servicio generales concentren demasiadas responsabilidades.

* Sea comprensible y aplicable por todos los integrantes dentro del plazo del laboratorio.

# **2\. Decisión**

Se adoptará un **monolito modular organizado por capacidades de negocio**. Dentro de cada módulo, los casos de uso se organizarán como **vertical slices**, manteniendo una regla de dependencias inspirada en Clean Architecture y en arquitectura hexagonal.

Esta decisión complementa, pero no reemplaza, la arquitectura de despliegue exigida por la letra. El monolito modular y el worker independiente son restricciones del laboratorio; la organización por capacidades, vertical slices y dependencias hacia el dominio constituye la decisión interna del equipo.

## **2.1 Módulos de negocio**

De acuerdo con el modelo de dominio elaborado por el equipo, la aplicación se dividirá en los siguientes módulos:

1. Administración y configuración.

2. Envíos y entregas.

3. Planificación de rutas.

4. Ejecución de rutas.

5. Seguimiento y notificaciones.

6. Depósito y liquidaciones.

La propiedad principal de los conceptos se distribuirá de la siguiente manera:

* **Administración y configuración:** Operador, Comercio, Usuario, Repartidor, Vehículo, Zona, versiones de reglas y tarifas.

* **Envíos y entregas:** Envío, Bulto, Destinatario, EventoEnvio, IntentoEntrega, Incidencia y Devolución.

* **Planificación de rutas:** Ruta, Parada, criterios de ordenamiento y estados de planificación.

* **Ejecución de rutas:** EscaneoCarga, PosicionVehiculo, Rendición y LineaRendicion.

* **Seguimiento y notificaciones:** SuscripcionAviso, Notificacion y EntregaAviso.

* **Depósito y liquidaciones:** RecepcionDeposito, Liquidación y LineaLiquidacion.

Cada concepto de dominio tendrá un único módulo propietario. Cuando un concepto aparezca en otro módulo, se representará mediante su identificador o mediante un contrato público; no se duplicará la entidad ni se modificará directamente su estado interno.

## 

## **2.2 Organización interna**

Cada módulo distinguirá las siguientes responsabilidades:

* **Domain:** entidades, value objects, agregados, invariantes, eventos de dominio y servicios de dominio.

* **Application:** casos de uso, comandos, consultas, validación de entrada y coordinación de operaciones.

* **Infrastructure:** persistencia con Entity Framework Core, implementación de repositorios, mensajería, caché y adaptadores de servicios externos.

* **Presentation:** endpoints de la API y páginas del Backoffice que invocan los casos de uso, **ubicados dentro del módulo** (addenda 2 y 4).

Para equilibrar el aislamiento con el tamaño del equipo, cada módulo se implementará mediante dos proyectos de biblioteca:

Logistica.Modules.Envios/  
  Domain/  
  Application/  
    Features/  
  Infrastructure/  
  Presentation/  
    Features/  
  EnviosModule.cs  
   
Logistica.Modules.Envios.Contracts/  
  IEnviosModuleApi.cs  
  Results/  
  Events/

El primer proyecto contiene la implementación interna del módulo, separada mediante carpetas y namespaces. Sus commands, queries, handlers y entidades se declaran internal: lo único público es la **clase de entrada** \<Modulo\>Module, con los métodos Add\<Modulo\>Module(), que registra sus servicios, y Map\<Modulo\>Endpoints(), que registra sus endpoints bajo una ruta propia (/api/envios, /api/planificacion, etc.). Para que las pruebas unitarias alcancen los tipos internos, cada módulo declara InternalsVisibleTo hacia Logistica.UnitTests (addendum 2).

El proyecto **Contracts** es la única parte del módulo que pueden usar los demás módulos (addendum 2):

* I\<Modulo\>ModuleApi: interfaz con las consultas y acciones que otros módulos invocan de forma síncrona y en memoria. La implementa una clase internal de Application.

* Results/: tipos de datos, preferentemente record inmutables, que la interfaz recibe o devuelve. Nunca se exponen entidades del dominio.

* Events/: eventos de integración que el módulo publica para que otros reaccionen.

No se crean carpetas Commands/ ni Queries/ en Contracts: los módulos no se envían commands entre sí, sino que invocan los métodos de I\<Modulo\>ModuleApi. Las carpetas se crean cuando aparece el primer archivo que las necesita.

**Proyectos compartidos (addendum 1).** El código técnico que todos los módulos necesitan de forma idéntica, en particular el aislamiento entre inquilinos del ADR-0002, se ubica en dos proyectos bajo src/BuildingBlocks/:

* **Logistica.SharedKernel:** C\# puro, sin paquetes NuGet. Clases base (Entity, AggregateRoot, IDomainEvent, DomainException), marcadores de inquilino (IOperadorOwned, IComercioOwned), la interfaz ICurrentTenant y el tipo base de los eventos de integración.

* **Logistica.BuildingBlocks.Infrastructure:** depende de Entity Framework Core. Contiene el TenantSaveChangesInterceptor, la extensión ApplyTenantQueryFilters(), la tabla Outbox y la clase base ModuleDbContext.

Sólo se incorpora un tipo a estos proyectos si se usa en dos o más módulos, es puramente técnico y debe comportarse igual en todos. Si una clase del código compartido menciona envíos, rutas, comercios, tarifas u otro concepto del dominio, está mal ubicada. Las implementaciones de ICurrentTenant se ubican en los procesos ejecutables: la API las toma de la sesión o del token de seguimiento, y el worker, de los metadatos del mensaje.

**DTOs HTTP compartidos (addendum 5).** Los tipos de request y response de los endpoints se ubican en Logistica.Http.Contracts, un proyecto sin dependencias que referencian los módulos y las aplicaciones Blazor WebAssembly. Es distinto de los proyectos Contracts de cada módulo, que nunca llegan al navegador.

**Proyectos ejecutables.** Logistica.Api es el único host web: compone los módulos, sirve la API, el Backoffice y las aplicaciones WebAssembly (sección 2.6), y configura lo transversal (autenticación, resolución del inquilino, logging, telemetría y health checks). Logistica.Worker se despliega por separado. Las bibliotecas de los módulos se compilan y despliegan junto con ellos.

## **2.3 Vertical slices**

Dentro de Application y Presentation, el código se organiza por caso de uso, bajo una carpeta Features/ (addendum 2). Por ejemplo:

Logistica.Modules.Envios/  
  Domain/  
    Envios/  
      Envio.cs  
      Bulto.cs  
      EventoEnvio.cs  
  Application/  
    Features/  
      CrearEnvio/  
        CrearEnvioCommand.cs  
        CrearEnvioHandler.cs  
  Infrastructure/  
    Persistence/  
    Messaging/  
  Presentation/  
    Features/  
      CrearEnvio/  
        CrearEnvioEndpoint.cs  
   
Logistica.Http.Contracts/  
  Envios/  
    CrearEnvioRequest.cs

Un caso de uso queda completo dentro de su módulo: handler, endpoint y, si corresponde, página del Backoffice. El endpoint traduce la petición HTTP a un command, invoca al handler y convierte el resultado en una respuesta HTTP; no contiene reglas de negocio ni acceso a datos. La validación de formato usa la validación incorporada de .NET 10\.

Las reglas propias del negocio no se duplicarán dentro de los handlers. Por ejemplo, el handler de transición coordinará el caso de uso, pero la entidad Envío será responsable de determinar si la transición solicitada es válida.

## 

## 

## **2.4 Regla de dependencias**

**Entre capas de un módulo** (addendum 2):

* Domain no depende de Application, Infrastructure, Presentation, Entity Framework Core ni ASP.NET Core.

* Application no depende de Infrastructure, Presentation, Entity Framework Core ni ASP.NET Core.

* Infrastructure puede depender de Application y Domain para implementar sus contratos, pero no de Presentation ni de ASP.NET Core.

* Presentation puede depender de Application y de los Contracts, pero no de Infrastructure.

* Sólo Presentation y la clase de entrada del módulo usan ASP.NET Core. El proyecto del módulo referencia el framework para su capa Presentation; las pruebas impiden que llegue a las demás capas.

* Las páginas del Backoffice que Razor compila en el namespace AspNetCoreGeneratedDocument no dependen de la capa Infrastructure de su módulo ni del interior de otros módulos (addendum 4).

**Entre módulos:**

* Un módulo no podrá acceder directamente al DbContext, las tablas, los repositorios internos ni las entidades de otro módulo.

* Un módulo sólo podrá referenciar el proyecto Contracts de otro módulo; no podrá depender de sus namespaces Domain, Application, Infrastructure ni Presentation.

* No se utilizarán llamadas HTTP entre los módulos del monolito.

* Las integraciones con base de datos, caché, mensajería, almacenamiento, correo y webhooks se implementarán como adaptadores externos a la lógica de dominio.

**Proyectos compartidos y aplicaciones cliente** (addenda 1, 4 y 5):

| Proyecto | Puede referenciar |
| :---- | :---- |
| Logistica.SharedKernel | Nada |
| Logistica.BuildingBlocks.Infrastructure | SharedKernel y Entity Framework Core |
| Logistica.Modules.X | Su Contracts, SharedKernel, BuildingBlocks.Infrastructure y Http.Contracts |
| Logistica.Modules.X.Contracts | SharedKernel |
| Logistica.Http.Contracts | Nada (se compila también para el navegador) |
| Logistica.Backoffice | Ningún módulo: sólo layout, menú y páginas comunes |
| Aplicaciones Blazor WebAssembly | Http.Contracts; nunca módulos, BuildingBlocks ni Backoffice |
| Logistica.Api y Logistica.Worker | Los módulos y BuildingBlocks.Infrastructure |

Ningún proyecto de BuildingBlocks puede depender de un módulo.

Estas reglas se controlan mediante un proyecto de pruebas con **xUnit y ArchUnitNET** (TngTech.ArchUnitNET.xUnit), que reemplaza a NetArchTest.Rules (addendum 3). El aislamiento entre módulos se verifica con una regla explícita por módulo; los proyectos Contracts no se cargan en la arquitectura analizada, de modo que usarlos está permitido y usar cualquier otra parte de otro módulo hace fallar la prueba. Las pruebas se ejecutan mediante dotnet test en el pipeline de integración continua.

## **2.5 Comunicación entre módulos**

Sección incorporada por el addendum 3\. Reemplaza las viñetas de comunicación de la regla de dependencias original.

Los eventos de dominio internos se despacharán en memoria. La comunicación síncrona entre módulos se realizará mediante contratos públicos. Los eventos de integración que requieran procesamiento durable, ejecución en el Worker o comunicación con sistemas externos se registrarán mediante el patrón Outbox y se publicarán en RabbitMQ. RabbitMQ no se utilizará para todas las interacciones internas del monolito.

Regla práctica: **si perder la reacción deja datos inconsistentes, la reacción se publica mediante Outbox**. Si puede perderse sin consecuencias, puede despacharse en memoria.

| Necesidad | Mecanismo | Ejemplo |
| :---- | :---- | :---- |
| Consulta o respuesta inmediata | Contrato síncrono en memoria, publicado en Contracts | Planificación consulta si un envío puede asignarse a una ruta. |
| Reacción que puede perderse sin dejar datos inconsistentes | Evento de dominio en memoria | Al cambiar el estado de un envío se invalida la caché del seguimiento público; si falla, la caché expira sola. |
| Reacción que no puede perderse | Outbox y RabbitMQ | Cuando Envíos registra una entrega, Depósito y liquidaciones incorpora el envío a la liquidación del comercio. |
| Trabajo en el Worker o con sistemas externos | Outbox, RabbitMQ y Worker | Notificaciones al destinatario y webhooks a los comercios. |

**Transacción única en las llamadas síncronas.** Cuando un caso de uso invoca a otro módulo mediante su contrato síncrono y ambos modifican datos, los dos cambios se guardan en una sola transacción: los DbContext de los módulos comparten la conexión a la misma base PostgreSQL. Si cualquiera de los dos falla, se revierten ambos.

## **2.6 Presentación y aplicaciones web**

Sección incorporada por los addenda 4 y 5\.

Logistica.Api es el único host web y sirve todo desde un mismo origen:

| Ruta | Contenido |
| :---- | :---- |
| /api/... | Minimal APIs de los módulos |
| /backoffice/... | Backoffice (Razor Pages) |
| /portal/... | Portal del comercio (Blazor WebAssembly) |
| /seguimiento/... | Seguimiento público (Blazor WebAssembly); el enlace de un envío es /seguimiento/{token} |
| /repartidor/... | Aplicación del repartidor (Blazor WebAssembly PWA) |
| /health | Health check |

**Backoffice (addendum 4).** Logistica.Backoffice es una biblioteca Razor cargada por la API, no una aplicación independiente. Contiene el layout, el menú, las páginas comunes y sus archivos estáticos. Las páginas de cada caso de uso viven en la capa Presentation de su módulo, junto a su endpoint, con un PageModel que puede ser internal. Para descubrirlas fuera de la carpeta Pages/, la API configura la raíz de Razor Pages en /, y **toda página declara su ruta absoluta** bajo /backoffice (por ejemplo, @page "/backoffice/zonas/alta").

**Aplicaciones Blazor WebAssembly (addendum 5).** El portal, el seguimiento y la aplicación del repartidor se sirven bajo su propia ruta, declarada con StaticWebAssetBasePath y con \<base href\>. Cada ruta sin extensión de archivo devuelve el index.html de su aplicación. Durante el desarrollo se ejecuta sólo la API. El service worker de la PWA se limita a /repartidor/. Al compartir origen, no se configura CORS y la cookie de Identity puede emitirse con SameSite=Strict.

## **2.7 Preparación para cambios**

No se intentará anticipar el requerimiento desconocido del 29 de octubre. En su lugar, se buscará que:

* Las reglas de negocio tengan un único lugar de definición.

* Las reglas configurables no se escriban directamente en controladores, vistas o adaptadores.

* Los casos de uso estén localizados y tengan dependencias explícitas.

* Los módulos expongan contratos pequeños y estables.

* La infraestructura pueda cambiar sin modificar las entidades del dominio.

Esta estructura no garantiza que todo cambio afecte un solo archivo o módulo, pero reduce el acoplamiento accidental y permite identificar con mayor claridad los componentes involucrados.

# **3\. Alternativas consideradas**

## **3.1 Arquitectura tradicional por capas**

Se consideró organizar la solución en capas globales de presentación, servicios, repositorios y entidades.

**Motivo de descarte:** aunque es sencilla al inicio, distribuye cada funcionalidad entre carpetas técnicas globales. Un cambio de negocio puede requerir modificaciones en distintos sectores de la solución y favorecer la aparición de servicios generales con demasiadas responsabilidades. Tampoco garantiza por sí sola el aislamiento entre los módulos del negocio.

## **3.2 Clean Architecture global**

Se consideró crear capas globales de Domain, Application, Infrastructure y Presentation para toda la solución.

**Motivo de descarte:** cumple correctamente la regla de dependencias, pero una implementación exclusivamente global puede debilitar los límites entre capacidades de negocio. También puede producir demasiadas abstracciones compartidas y hacer que un cambio funcional atraviese varias carpetas generales.

Se conservará su regla de dependencias hacia el dominio dentro de la decisión adoptada.

## 

## 

## **3.3 Vertical Slice Architecture sin límites de dominio**

Se consideró organizar toda la solución únicamente por casos de uso.

**Motivo de descarte:** facilita localizar funcionalidades, pero no determina dónde deben residir las invariantes compartidas ni cómo impedir que los handlers dependan directamente de Entity Framework Core u otras implementaciones. Sin límites adicionales, la lógica de negocio podría quedar duplicada o dispersa.

Se conservará la organización por casos de uso dentro de cada módulo.

## **3.4 Arquitectura hexagonal aplicada a toda clase**

Se consideró modelar todos los componentes mediante puertos y adaptadores.

**Motivo de descarte:** es valiosa para las dependencias externas, pero aplicada indiscriminadamente puede generar interfaces y adaptadores sin una necesidad real, aumentando el código ceremonial.

Se utilizará en los límites que interactúan con persistencia, mensajería, caché, almacenamiento y servicios externos.

## **3.5 Microservicios**

Se consideró separar las capacidades en servicios desplegables independientes.

**Motivo de descarte:** contradice la arquitectura de monolito modular establecida por la letra y agrega costos innecesarios de despliegue, comunicación por red, consistencia distribuida, observabilidad y operación. El tamaño del equipo y el plazo tampoco justifican esa complejidad.

## **3.6 Código compartido en un único proyecto, dentro de un módulo o copiado en cada uno (addendum 1\)**

**Motivo de descarte:** un único proyecto arrastraría Entity Framework Core hasta los Contracts y el dominio; ubicarlo en el módulo de Administración haría que todos los módulos dependan de su interior; copiarlo en cada módulo dejaría seis copias del mecanismo que impide la filtración de datos entre operadores.

## **3.7 Endpoints en el proyecto anfitrión (texto de la versión 1.0, addendum 2\)**

Los endpoints se organizarían en Logistica.Api/Endpoints/\<Modulo\>/\<CasoDeUso\>/.

**Motivo de descarte:** cada caso de uso quedaría repartido en dos proyectos y los handlers deberían ser públicos para que la API los use. Es una alternativa válida; se descarta por menor cohesión y menor encapsulamiento.

## **3.8 Backoffice como proceso separado (addendum 4\)**

**Motivo de descarte:** llamando a la API por HTTP obligaría a reenviar la identidad del usuario, a resolver el inquilino dos veces y a mantener DTOs duplicados; referenciando los módulos directamente, dos procesos tendrían sus propios DbContext, Outbox y caché sobre la misma base. Su única ventaja, desplegarlo y escalarlo por separado, no es necesaria.

## **3.9 RabbitMQ para toda la comunicación entre módulos, o todos los eventos en memoria (addendum 3\)**

**Motivo de descarte:** la cola en todas las interacciones agrega serialización, reintentos, duplicados y consistencia eventual dentro del mismo proceso; todos los eventos en memoria pierden las reacciones imprescindibles si el módulo que reacciona falla.

## **3.10 NetArchTest.Rules (texto de la versión 1.0, addendum 3\)**

**Motivo de descarte:** cubre las reglas básicas, pero no publica versiones desde mayo de 2021\. Con un costo de cambio bajo en este momento, se prefiere ArchUnitNET, mantenida activamente.

## **3.11 Aplicaciones WebAssembly en orígenes separados (addendum 5\)**

**Motivo de descarte:** exige configurar CORS, emitir la cookie con SameSite=None y protegerla adicionalmente contra CSRF, y desplegar tres servicios más.

# **4\. Consecuencias**

## **4.1 Consecuencias positivas**

* Los cambios funcionales tenderán a quedar localizados en un módulo y en un conjunto acotado de casos de uso.

* Un caso de uso queda completo en su módulo (handler, endpoint y página), y el interior del módulo puede ser internal.

* La lógica de dominio podrá probarse sin iniciar la base de datos, la cola ni las aplicaciones web.

* El aislamiento entre inquilinos se implementa y se prueba una sola vez, en BuildingBlocks.

* Las dependencias externas podrán sustituirse o simularse mediante adaptadores.

* La estructura del código reflejará las capacidades del negocio representadas en el modelo de dominio.

* Un único host y un único origen: sin CORS, con una sola autenticación y un solo servicio web para desplegar.

* Cada interacción entre módulos tiene un criterio explícito para elegir su mecanismo de comunicación.

* La prueba de arquitectura detectará dependencias prohibidas durante el pipeline.

* La separación facilitará que distintos integrantes trabajen en capacidades diferentes con menor interferencia.

## **4.2 Consecuencias negativas y dificultades**

* La solución tendrá más carpetas, contratos y proyectos que una arquitectura tradicional por capas.

* El equipo deberá aprender y respetar las reglas de dependencia y propiedad de cada módulo.

* Algunos casos de uso atravesarán varios módulos y requerirán coordinación explícita.

* El proyecto de cada módulo tiene disponible ASP.NET Core: la separación entre Presentation y las demás capas depende de las pruebas de arquitectura y no de las referencias entre proyectos.

* Un cambio en BuildingBlocks impacta a todos los módulos a la vez, y el código compartido puede crecer de forma desordenada si no se respeta su criterio de admisión.

* Las reacciones por Outbox son eventualmente consistentes, y todo consumidor debe ser idempotente.

* Con la raíz de Razor Pages en /, toda página debe declarar su ruta absoluta bajo /backoffice; se controla en la revisión de código.

* Se desactiva OverrideHtmlAssetPlaceholders en las aplicaciones WebAssembly, porque en .NET 10 impide publicarlas bajo rutas distintas en el mismo host.

* El Backoffice y las aplicaciones WebAssembly se despliegan junto con la API: un cambio en cualquiera requiere volver a desplegarla.

* Los reportes que consulten información de varios módulos requieren modelos de lectura específicos.

* Una aplicación excesivamente estricta de interfaces, comandos y adaptadores puede introducir complejidad innecesaria; el equipo deberá evitar abstracciones que no respondan a un problema real.

* Los límites iniciales de los módulos pueden necesitar ajustes a medida que el equipo comprenda mejor el dominio.

# **5\. Verificación de la decisión**

La adopción de esta decisión se considerará verificable cuando:

1. La solución tenga módulos identificables por capacidad de negocio, cada uno con su clase de entrada y sus carpetas Application/Features/ y Presentation/Features/.

2. Sea posible ejecutar una prueba unitaria del dominio sin infraestructura.

3. Existan pruebas automatizadas que fallen si Domain, Application o Infrastructure dependen de capas o frameworks no permitidos, si un módulo usa el interior de otro, o si los proyectos compartidos, el Backoffice o las aplicaciones WebAssembly dependen de los módulos.

4. Ningún módulo acceda directamente a la persistencia interna de otro módulo.

5. Al menos un flujo de extremo a extremo, inicialmente la creación de un envío, atraviese Presentation, Application, Domain e Infrastructure respetando las dependencias definidas.

6. La API sirva el Backoffice y las aplicaciones WebAssembly bajo sus rutas, verificado por pruebas de integración.

7. Las pruebas de arquitectura y de integración se ejecuten en el pipeline de integración continua.

Los puntos 1, 3, 6 y 7 ya están implementados en el esqueleto de la solución (tests/Logistica.ArchitectureTests y tests/Logistica.IntegrationTests), y se verificaron con pruebas de mutación que introducen dependencias prohibidas.

# **6\. Compromisos de implementación**

* Crear un proyecto interno y un proyecto Contracts por cada módulo, con su clase de entrada.

* Definir primero los contratos necesarios para los flujos entre Administración y configuración, Envíos y entregas, y Planificación de rutas.

* Mantener el proyecto de pruebas arquitectónicas con xUnit y ArchUnitNET, ejecutado con dotnet test en el pipeline de integración continua.

* Implementar el interceptor, los filtros por inquilino y el Outbox en BuildingBlocks.Infrastructure antes del hito del 15 de octubre.

* Revisar los límites de los módulos si la implementación demuestra que una responsabilidad fue asignada al propietario incorrecto; cualquier cambio relevante se documentará mediante una nueva versión o un ADR sucesor.

# **7\. Referencias**

* Letra del laboratorio: sección 6.1, Plataforma y arquitectura; sección 6.2, Capa de presentación web; sección 6.8, Mensajería asíncrona; sección 6.15, Calidad; sección 8.1, Entrega de análisis y diseño; sección 8.2, Registros de decisión de arquitectura; sección 8.3, cambio obligatorio de requerimientos del 29 de octubre.

* ADR-0001 · Addendum 1 · Código compartido entre módulos (BuildingBlocks).

* ADR-0001 · Addendum 2 · Ubicación de los endpoints, los casos de uso y los contratos.

* ADR-0001 · Addendum 3 · Comunicación entre módulos y biblioteca de pruebas de arquitectura.

* ADR-0001 · Addendum 4 · Ejecución del Backoffice y ubicación de sus páginas.

* ADR-0001 · Addendum 5 · Aplicaciones Blazor WebAssembly en el mismo host y DTOs HTTP compartidos.

* ADR-0002 · Estrategia de multitenancy.

# **8\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---- | :---- | :---- | :---- |
| 1.0 | 24/09/2026 | Versión inicial aceptada. | Lucas Ottonello |
| 2.0 | 01/10/2026 | Incorpora en el texto los addenda 1 a 5: código compartido, ubicación de endpoints y contratos, comunicación entre módulos, ArchUnitNET, Backoffice y aplicaciones WebAssembly en el mismo host. | Ezequiel Marcenal |

