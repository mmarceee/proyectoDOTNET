# **Guía de decisiones tecnológicas del equipo**

*Qué vamos a construir, cómo se conectan las piezas y por qué elegimos cada herramienta*

| Fecha | 25 de septiembre de 2026 |
| :---- | :---- |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Propósito** | Alinear al equipo antes de comenzar la implementación |
| **Estado** | Decisiones confirmadas y pendientes identificadas |

Esta guía resume las decisiones tomadas para el laboratorio y explica el papel de cada tecnología. Su objetivo es que los tres integrantes puedan comprender la solución completa, aunque después cada uno profundice en su área. Las reglas de negocio seguirán aisladas de las herramientas para que el cambio de requerimientos previsto pueda incorporarse sin reescribir todo el sistema.

# **1 Vista general**

| Razor Pages / Blazor WebAssembly / PWA                  | HTTPS y SignalR                  vASP.NET Core Minimal APIs \- Monolito modular                  |      \+-----------+-----------+      |           |           | PostgreSQL      Valkey     RabbitMQ \-\> Worker      |                       |      \+------ OpenTelemetry \--+ \-\> Aspire Dashboard |
| :---- |

La API y las aplicaciones web forman el monolito modular. El worker se despliega por separado y recibe trabajo únicamente por RabbitMQ. PostgreSQL conserva la información permanente; Valkey acelera lecturas; SignalR envía actualizaciones en tiempo real; Serilog y OpenTelemetry permiten observar todo el recorrido.

# **2 Cómo se organiza el código**

## **2.1 Monolito modular**

No construiremos seis microservicios. Tendremos una solución desplegable principal dividida internamente en módulos de negocio. Cada módulo es responsable de sus reglas, casos de uso y tablas. Esta organización reduce la complejidad operativa sin convertir el sistema en una aplicación donde todo depende de todo.

| Concepto | Qué significa en el proyecto |
| ----- | ----- |
| Monolito | La API y las aplicaciones web se construyen y despliegan como una solución coordinada. |
| Modular | Administración, Envíos, Planificación, Ejecución, Seguimiento y Depósito mantienen límites explícitos. |
| Worker independiente | Procesa mensajes en segundo plano sin recibir llamadas HTTP desde la API. |

## **2.2 Capas internas y dirección de dependencias**

| API \-\> Application \-\> Domain          ^          | implementa puertos     Infrastructure |
| :---- |

* Domain contiene entidades, estados y reglas de negocio. No conoce frameworks.  
* Application contiene commands, queries y handlers. Coordina cada caso de uso.  
* Infrastructure conecta EF Core, PostgreSQL, Valkey, RabbitMQ y servicios externos.  
* API traduce las solicitudes HTTP y devuelve respuestas; no decide reglas del negocio.

## **2.3 Vertical slices**

Cada caso de uso se mantiene junto. En lugar de tener un servicio enorme con decenas de métodos, habrá un handler específico por operación.

| CrearEnvio/  CrearEnvioEndpoint.cs  CrearEnvioRequest.cs  CrearEnvioCommand.cs  CrearEnvioHandler.cs |
| :---- |

El endpoint recibe HTTP, el command representa la intención y el handler coordina el caso de uso. Las reglas invariantes permanecen en las entidades y servicios de dominio.

## **2.4 Comunicación entre módulos**

Un módulo no puede leer las tablas, entidades o DbContext internos de otro. Si necesita información inmediata, utilizará un contrato público del módulo propietario. Si el efecto puede ocurrir después, publicará o consumirá un evento.

| Necesidad | Mecanismo | Ejemplo |
| ----- | ----- | ----- |
| Respuesta inmediata | **Contrato interno** | Planificación consulta si un envío puede asignarse a una ruta. |
| Reacción posterior | **Evento por RabbitMQ** | Al cambiar el estado de un envío, el worker prepara notificaciones. |
| Datos propios | **Repositorio del módulo** | Envíos consulta solamente sus propias tablas. |

# **3 Presentación y API**

## **3.1 Razor Pages y Blazor WebAssembly**

El backoffice utilizará Razor Pages porque la letra lo exige y se adapta a pantallas administrativas. El portal del comercio y el seguimiento público utilizarán Blazor WebAssembly. La aplicación del repartidor será además una PWA instalable.

WebAssembly descarga la aplicación al navegador y ejecuta allí la interfaz. No coloca las reglas de negocio en el cliente: toda operación sensible vuelve a validarse en la API. Elegimos este modo porque la PWA debe cargar y continuar operando sin conexión y porque evita mantener una conexión permanente como exige Blazor Server.

La PWA no resuelve automáticamente el trabajo offline. Más adelante se implementará almacenamiento local, una cola de operaciones pendientes y una política de sincronización. Esa política se documentará en otro ADR.

## **3.2 Minimal APIs**

Una Minimal API cumple el mismo papel que un método de un controller tradicional: publica una ruta, recibe parámetros y devuelve una respuesta. La diferencia es que la ruta y el método quedan cerca del caso de uso, evitando controllers grandes.

| POST /api/envios  \-\> CrearEnvioEndpoint  \-\> CrearEnvioCommand  \-\> CrearEnvioHandler  \-\> Dominio |
| :---- |

Los endpoints serán delgados. No usarán directamente EF Core, Valkey ni RabbitMQ y no contendrán decisiones del negocio.

## **3.3 Validación incorporada**

Usaremos la validación incluida en .NET 10 para datos obligatorios, formatos y rangos. No agregaremos FluentValidation. Las respuestas inválidas se devolverán con ProblemDetails.

| Tipo de validación | Ubicación | Ejemplo |
| ----- | ----- | ----- |
| Formato de entrada | **Request** | Peso obligatorio y mayor que cero. |
| Autorización | **API y políticas** | El usuario pertenece al operador solicitado. |
| Regla de negocio | **Domain** | Un envío entregado no puede volver a tránsito. |

# **4 Identidad y multitenancy**

## **4.1 Identity con cookies**

ASP.NET Core Identity administrará usuarios, hashes de contraseña, roles, claims, bloqueos y recuperación de cuenta. El navegador mantendrá la sesión con una cookie HttpOnly y Secure. No construiremos un emisor JWT propio.

Las aplicaciones se publicarán bajo un mismo dominio lógico para que el navegador envíe la cookie a la API de forma controlada. Cuando la PWA esté offline podrá trabajar con datos previamente descargados, pero solamente sincronizará cuando vuelva la conexión y el servidor valide la sesión.

## **4.2 Aislamiento por inquilino**

Cada usuario autenticado tendrá un contexto de operador y, cuando corresponda, de comercio. Ese contexto se resolverá en el servidor. El cliente no podrá elegir libremente un TenantId para acceder a datos ajenos.

* Todas las lecturas y escrituras incluirán el aislamiento correspondiente.  
* Los filtros y validaciones se aplicarán en la infraestructura.  
* Habrá pruebas automáticas con dos operadores para detectar filtraciones.  
* Las reglas, tarifas e identidad visual podrán diferir por operador.

# **5 Datos y persistencia**

## **5.1 PostgreSQL**

PostgreSQL conservará la información permanente. Todos los módulos compartirán la instancia y la base, pero cada uno será propietario de sus tablas. Compartir la base no autoriza a un módulo a consultar directamente las tablas de otro.

## **5.2 Entity Framework Core y Npgsql**

Entity Framework Core permite consultar y guardar entidades con C\# y administrar migraciones. Npgsql es el proveedor que traduce esas operaciones al lenguaje y protocolo de PostgreSQL. No es un servidor adicional ni genera otro costo.

| Código C\# \-\> Entity Framework Core \-\> Npgsql \-\> PostgreSQL |
| :---- |

Cada módulo tendrá su DbContext. No agregaremos un repositorio genérico para todo el sistema; se crearán repositorios específicos cuando un agregado o caso de uso lo justifique.

# **6 Caché y procesamiento asíncrono**

## **6.1 Valkey**

Valkey se utilizará como caché distribuida, no como base principal. La aplicación accederá mediante IDistributedCache, por lo que los casos de uso no conocerán directamente el proveedor. En desarrollo correrá en Docker Compose. En producción se evaluará DigitalOcean Managed Valkey según el costo disponible.

| Caso | Política inicial |
| ----- | ----- |
| Seguimiento público | Expiración corta porque recibe muchas lecturas y cambia con frecuencia. |
| Tarifas y reglas vigentes | Expiración más larga e invalidación al publicar una nueva versión. |

La aplicación accederá mediante IDistributedCache. Esto permite cambiar el proveedor sin modificar los casos de uso.

## **6.2 RabbitMQ y el worker**

RabbitMQ guardará mensajes para que la API no tenga que esperar trabajos secundarios. El worker se despliega de forma independiente y consume esos mensajes.

| API cambia el estado de un envío  \-\> registra el evento en Outbox  \-\> publica en RabbitMQ  \-\> Worker consume  \-\> envía avisos o recalcula indicadores |
| :---- |

* Outbox evita guardar un cambio sin publicar su evento.  
* La idempotencia evita efectos duplicados al reprocesar un mensaje.  
* Los reintentos usan espera creciente.  
* Los mensajes agotados pasan a una dead-letter queue visible y reprocesable.

La lógica de Application dependerá de una interfaz de mensajería. RabbitMQ se implementará en Infrastructure, de forma que el dominio no conozca el broker.

# **7 Tiempo real y observabilidad**

## **7.1 SignalR**

SignalR permitirá que la API envíe novedades al tablero sin que el navegador consulte repetidamente. Se usará para posiciones de vehículos, cambios relevantes y alertas operativas. Elegir Blazor WebAssembly no elimina SignalR: WebAssembly ejecuta la interfaz y SignalR transporta eventos en tiempo real.

## **7.2 Serilog, OpenTelemetry y Aspire Dashboard**

| Herramienta | Responsabilidad |
| ----- | ----- |
| Serilog | Genera logs estructurados con datos consultables. |
| OpenTelemetry | Instrumenta métricas y trazas distribuidas con correlación. |
| Aspire Dashboard | Visualiza logs, métricas y trazas; no utilizaremos el orquestador completo de Aspire. |

El mismo identificador de correlación acompañará una operación desde la aplicación, pasando por la API y RabbitMQ, hasta el worker. El tablero mostrará latencias, errores, profundidad de cola, sincronizaciones pendientes y aciertos de caché.

# **8 Pruebas y calidad**

xUnit será el framework de pruebas. No intentaremos probar cada línea: cubriremos los riesgos que la letra exige y las reglas que podrían romperse con facilidad.

| Prueba | Qué verifica | Herramienta |
| ----- | ----- | ----- |
| Unitaria | **Reglas del dominio y transiciones de estado.** | xUnit |
| Handler | **Coordinación del caso de uso con fakes sencillos.** | xUnit |
| Integración | **Flujo HTTP crítico con PostgreSQL real.** | WebApplicationFactory y Testcontainers |
| Multitenancy | **Que un operador o comercio no lea datos de otro.** | xUnit e integración |
| Arquitectura | **Que Domain no dependa de Infrastructure y que se respeten los módulos.** | NetArchTest o ArchUnitNET, a confirmar |

No incorporaremos inicialmente pruebas de navegador con Playwright ni una biblioteca de mocks. Son opcionales y aumentarían el trabajo sin ser necesarias para el alcance mínimo acordado.

# **9 Desarrollo, integración y despliegue**

## **9.1 Entorno local**

Visual Studio 2026 será el IDE principal. Docker Compose iniciará PostgreSQL, Valkey, RabbitMQ y Aspire Dashboard. De esta forma los tres integrantes utilizarán un entorno equivalente.

## **9.2 GitHub Actions y App Platform**

| Feature branch \-\> Pull request \-\> GitHub ActionsGitHub Actions \-\> compilar y ejecutar pruebasMerge a main \-\> App Platform despliega automáticamente |
| :---- |

GitHub Actions comprobará el código antes de integrarlo. DigitalOcean App Platform observará la rama main y desplegará la versión aprobada. No pondremos comandos de despliegue duplicados en Actions mientras App Platform pueda resolver esa tarea.

## **9.3 Terraform**

Terraform describirá la infraestructura de DigitalOcean mediante archivos versionados. Permitirá ejecutar plan, apply y destroy, demostrando que el ambiente puede reconstruirse. Los tokens y secretos nunca se guardarán en el repositorio.

App Platform manejará HTTPS y el enrutamiento público. Por eso no necesitaremos configurar Caddy ni administrar directamente una máquina virtual.

# **10 Flujo completo de ejemplo**

1. El comercio completa el formulario de alta en Blazor WebAssembly.  
2. La PWA o el portal envía POST /api/envios con la cookie de Identity.  
3. CrearEnvioEndpoint valida el formato y crea CrearEnvioCommand.  
4. CrearEnvioHandler obtiene el tenant, aplica el dominio y guarda mediante el DbContext del módulo.  
5. La transacción registra también el evento en Outbox.  
6. El evento se publica en RabbitMQ.  
7. El worker consume el mensaje de forma idempotente y genera las notificaciones.  
8. OpenTelemetry relaciona las trazas y Aspire Dashboard permite observar el recorrido.  
9. La caché del seguimiento se invalida para que el destinatario vea el nuevo estado.

# **11 Reglas prácticas para el equipo**

| Hacer | Evitar |
| ----- | ----- |
| Crear un handler por caso de uso. | Agregar lógica de negocio al endpoint. |
| Usar contratos públicos entre módulos. | Referenciar entidades o DbContext de otro módulo. |
| Depender de interfaces en Application. | Usar Valkey o RabbitMQ desde Domain. |
| Obtener TenantId del contexto autenticado. | Confiar en un TenantId enviado por el cliente. |
| Registrar eventos mediante Outbox. | Guardar en la base y publicar por separado sin garantía. |
| Agregar pruebas para reglas críticas. | Buscar cobertura alta sin relación con el riesgo. |

# **12 Decisiones pendientes**

* Política de sincronización y resolución de conflictos de la PWA, que tendrá su propio ADR.  
* Alojamiento definitivo de RabbitMQ y contratación de Valkey en producción según costos.  
* Biblioteca concreta para RabbitMQ y para pruebas de arquitectura.  
* Almacenamiento de fotografías, firmas y documentos de prueba de entrega.  
* Protección y persistencia de la observabilidad en el ambiente remoto.

Estas decisiones se tomarán cuando corresponda implementarlas. No modifican el stack central acordado ni impiden iniciar la solución.

# **13 Resumen de compromisos**

* Mantener el dominio independiente de los frameworks y servicios externos.  
* Trabajar por casos de uso y preservar los límites de los módulos.  
* Automatizar compilación, pruebas, despliegue e infraestructura.  
* Implementar solamente las herramientas necesarias para la letra y el alcance del equipo.  
* Documentar las decisiones futuras mediante ADR antes de comprometer la implementación.