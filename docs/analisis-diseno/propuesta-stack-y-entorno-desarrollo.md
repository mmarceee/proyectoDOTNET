# **Propuesta de stack tecnológico y entorno de desarrollo**

| Estado | Aceptado |
| :---- | :---- |
| **Fecha** | 25 de septiembre de 2026 (actualizado el 3 de octubre de 2026) |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Alcance** | Entrega de análisis y diseño del 4 de octubre de 2026 |

La propuesta adopta un stack alineado con los requerimientos obligatorios del laboratorio y prioriza herramientas que el equipo pueda desarrollar, probar y explicar dentro del plazo disponible. La solución se implementará como un monolito modular en .NET 10, acompañado por un worker independiente, aplicaciones web y los servicios de soporte exigidos.

# **1 Criterios de selección**

* Cumplimiento directo de las tecnologías y capacidades exigidas por la letra.  
* Curva de aprendizaje razonable para un equipo de tres integrantes.  
* Entorno local reproducible mediante Docker Compose.  
* Separación entre dominio, casos de uso e infraestructura.  
* Servicios reemplazables mediante abstracciones, sin acoplar la lógica de negocio.  
* Despliegue administrado para reducir trabajo operativo y concentrar el esfuerzo en la aplicación.

# **2 Stack tecnológico acordado**

| Área | Tecnología | Uso y justificación |
| ----- | ----- | ----- |
| Plataforma | **.NET 10 y C\#** | Base común de la API, aplicaciones web y worker. .NET 10 es obligatorio. |
| Entorno de desarrollo | **Visual Studio 2026 Community** | IDE principal para editar, ejecutar, depurar y probar la solución. |
| Backoffice | **ASP.NET Core Razor Pages** | Interfaz administrativa del operador, exigida expresamente por la letra. Biblioteca Razor servida por la API en `/backoffice`. |
| Portal y seguimiento | **Blazor WebAssembly** | Portal del comercio y seguimiento público ejecutados en el navegador, servidos por la API en `/portal` y `/seguimiento`. |
| Aplicación del repartidor | **Blazor WebAssembly PWA** | Alternativa permitida para equipos de tres, instalable y preparada para operación sin conexión. Servida por la API en `/repartidor`. |
| API | **ASP.NET Core Minimal APIs** | Endpoints ubicados en la capa Presentation de cada módulo, organizados por caso de uso, con handlers delgados. |
| Validación | **Validación incorporada de .NET 10** | Data Annotations, validación automática y respuestas ProblemDetails, sin FluentValidation. |
| Persistencia | **PostgreSQL, EF Core y Npgsql** | Base relacional compartida, DbContext por módulo y migraciones controladas. |
| Autenticación | **ASP.NET Core Identity y cookies** | Usuarios, roles y claims con cookies HttpOnly y Secure; no se usarán JWT inicialmente. |
| Caché | **Valkey** | Caché distribuida mediante IDistributedCache para seguimiento público y configuración tarifaria. |
| Mensajería | **RabbitMQ y RabbitMQ.Client** | Comunicación asíncrona entre la API y el worker con Outbox, reintentos y cola de fallidos. Cliente oficial, con implementación propia del Outbox (ADR-0003, propuesto). |
| Tiempo real | **ASP.NET Core SignalR** | Actualización del tablero operativo y posiciones de la flota, exigida por la letra. |
| Observabilidad | **Serilog, OpenTelemetry y Aspire Dashboard** | Logs estructurados, métricas, trazas y visualización centralizada. |
| Correo | **SMTP: Mailpit y Brevo** | Invitaciones a usuarios y notificaciones al destinatario. Mailpit captura los correos en desarrollo; Brevo (plan gratuito, 300 correos por día) los envía en el ambiente desplegado. |
| Archivos | **PostgreSQL** | Logos, fotos y firmas de entrega en una tabla de archivos, con límite de 1 MB y acceso detrás de una interfaz de Infrastructure. |
| Pruebas | **xUnit, ArchUnitNET, WebApplicationFactory, Testcontainers, Respawn y Playwright** | Pruebas unitarias, de integración, aislamiento y arquitectura. La prueba de arquitectura usa ArchUnitNET, según el ADR-0001, addendum 3. Playwright, para las pruebas de extremo a extremo del requerimiento opcional 7.2. |
| Contenedores | **Docker Desktop y Docker Compose** | Entorno completo reproducible: servicios de soporte, API y worker. |
| Integración continua | **GitHub Actions** | Compilación, pruebas y construcción de las imágenes Docker en cada pull request. |
| Despliegue | **DigitalOcean App Platform** | Despliegue automático desde main de dos servicios: la API, que sirve también el Backoffice y las aplicaciones WebAssembly, y el worker. |
| Infraestructura como código | **Terraform** | Creación y reconstrucción reproducible de los recursos de DigitalOcean. |

# **3 Decisiones principales**

## **3.1 Arquitectura y organización**

La solución mantendrá la arquitectura de monolito modular definida en el ADR-0001. Cada módulo tendrá un proyecto principal con carpetas Domain, Application, Infrastructure y Presentation, y un proyecto Contracts con lo único que otros módulos pueden usar: su interfaz `I<Modulo>ModuleApi`, sus tipos de resultado y sus eventos de integración. Los casos de uso se organizarán como vertical slices, bajo `Application/Features/` y `Presentation/Features/` (ADR-0001, addendum 2).

```
Presentation (endpoint o página) -> Command o Query -> Handler (Application) -> Dominio
                                                         -> Contratos de otros módulos
Infrastructure -> EF Core, Valkey, RabbitMQ y servicios externos
```

El dominio no dependerá de ASP.NET Core, Entity Framework Core, Valkey, RabbitMQ ni servicios externos. Los módulos no compartirán entidades internas ni accederán al DbContext de otro módulo. El código técnico común a todos los módulos, como el aislamiento por inquilino y el Outbox, se ubica en `Logistica.SharedKernel` y `Logistica.BuildingBlocks.Infrastructure` (addendum 1).

## **3.2 Presentación web**

El backoffice se desarrollará con Razor Pages. El portal del comercio y el seguimiento público utilizarán Blazor WebAssembly. La aplicación del repartidor será una PWA Blazor WebAssembly. Este modo evita depender de una conexión permanente para renderizar la interfaz y permite implementar la operación offline exigida para el trabajo en calle.

La API es el único host web: sirve la API, el Backoffice y las tres aplicaciones WebAssembly desde un mismo origen, cada una bajo su propia ruta. El Backoffice es una biblioteca Razor y las páginas de cada caso de uso viven en la capa Presentation de su módulo (addendum 4). Las aplicaciones WebAssembly se publican bajo `/portal`, `/seguimiento` y `/repartidor`, y el service worker de la PWA se limita a `/repartidor` (addendum 5).

SignalR se utilizará únicamente para actualizaciones en tiempo real. No se utilizará Blazor Server ni el modo automático, con el fin de mantener un solo modelo de ejecución para las interfaces Blazor.

## **3.3 API y validación**

Los endpoints se implementarán con Minimal APIs, dentro de la capa Presentation del módulo, junto al caso de uso correspondiente. Cada endpoint traducirá la petición HTTP a un command o query, invocará al handler y convertirá el resultado en una respuesta HTTP. No contendrá reglas de negocio ni acceso directo a datos. Los tipos de request y response se definen en `Logistica.Http.Contracts`, compartido con las aplicaciones WebAssembly (addendum 5).

La validación de formato y datos obligatorios se realizará con las capacidades incorporadas en .NET 10\. Las reglas del negocio permanecerán en el dominio o en el handler. Los errores HTTP se representarán de forma consistente mediante ProblemDetails.

## **3.4 Identidad y seguridad**

ASP.NET Core Identity administrará usuarios, contraseñas, roles, claims, bloqueos y recuperación de cuenta. La sesión se mantendrá con cookies seguras, configuradas como HttpOnly y Secure. Como la API, el Backoffice y las aplicaciones WebAssembly comparten un mismo origen, la cookie puede emitirse con `SameSite=Strict` y no es necesario habilitar CORS: la política de CORS rechaza cualquier otro origen. El TenantId y, cuando corresponda, el ComercioId se resolverán del contexto autenticado y nunca se confiarán a partir de valores enviados libremente por el cliente.

## **3.5 Persistencia y multitenancy**

PostgreSQL será la base principal. Entity Framework Core se conectará mediante Npgsql. La instancia será compartida, pero cada módulo será dueño de sus tablas y utilizará su propio DbContext. El aislamiento por fila se aplicará con OperadorId y, cuando corresponda, ComercioId, conforme al ADR-0002.

Cuando un caso de uso invoca a otro módulo mediante su contrato síncrono y ambos modifican datos, los dos cambios se guardan en una sola transacción compartida (addendum 3). Se utilizarán migraciones controladas y pruebas automatizadas para impedir filtraciones entre inquilinos. Las entidades susceptibles de modificaciones simultáneas incorporarán control de concurrencia optimista.

## **3.6 Caché y mensajería**

Valkey se utilizará exclusivamente como caché distribuida. La aplicación accederá mediante IDistributedCache para evitar que los casos de uso dependan directamente del proveedor. Los primeros casos serán el seguimiento público y la configuración tarifaria, con políticas distintas de expiración e invalidación. Se registrarán métricas de aciertos y fallos.

RabbitMQ conectará la API con el worker independiente. No se usará para todas las interacciones internas: los eventos de dominio se despachan en memoria, y sólo van por Outbox y RabbitMQ las reacciones que no pueden perderse y el trabajo del worker o con sistemas externos (addendum 3). Los consumidores serán idempotentes e incluirán reintentos con espera creciente y una cola de mensajes fallidos. El ADR-0003 propone implementar el Outbox sobre el cliente oficial RabbitMQ.Client.

## **3.7 Observabilidad**

Serilog producirá logs estructurados. OpenTelemetry generará métricas y trazas distribuidas con identificadores de correlación. Aspire Dashboard recibirá la telemetría y mostrará latencias, errores, profundidad de cola, sincronizaciones pendientes, avisos fallidos y métricas de caché. No se adoptará el orquestador completo de .NET Aspire: solamente su panel de observabilidad.

## **3.8 Estrategia de pruebas**

xUnit será el framework único. Se escribirán pruebas unitarias para reglas de dominio y handlers, pruebas de integración con WebApplicationFactory y PostgreSQL real mediante Testcontainers, pruebas de aislamiento multitenant y pruebas automatizadas de arquitectura con ArchUnitNET. No se incorporará una biblioteca de mocks.

Además, el equipo eligió tres requerimientos opcionales de pruebas (Plan de casos de uso por hito, sección 4): pruebas de extremo a extremo con **Playwright** sobre los flujos críticos, integradas al pipeline; cobertura superior al 70 % en las capas de dominio y aplicación, con reporte publicado por el pipeline; y pruebas de resiliencia que interrumpen deliberadamente la caché, la cola, la base de datos y un servicio externo.

## **3.9 Correo y archivos**

La aplicación enviará los correos por SMTP, sin depender de un proveedor concreto: en desarrollo, Mailpit los captura y los muestra en una página web; en el ambiente desplegado se usará Brevo, cuyo plan gratuito permite 300 correos por día. Cambiar de proveedor sólo requiere cambiar la configuración.

Los logos, las fotos y las firmas de entrega se guardarán en PostgreSQL, en una tabla de archivos con un límite de 1 MB por archivo y el mismo aislamiento por inquilino que el resto de los datos. El acceso pasará por una interfaz de Infrastructure, de modo que podría cambiarse a un almacenamiento de objetos sin modificar los casos de uso.

# **4 Entorno de desarrollo**

## **4.1 Herramientas requeridas**

* Visual Studio 2026 Community con las cargas de trabajo ASP.NET y desarrollo web y herramientas de contenedores.  
* .NET 10 SDK fijado mediante global.json.  
* Docker Desktop con Docker Compose.  
* Git y acceso al repositorio de GitHub. En Windows, Git Bash para ejecutar los scripts del repositorio.  
* Un navegador basado en Chromium para probar la PWA, el service worker y el modo sin conexión.  
* Terraform para planificar y aplicar la infraestructura de DigitalOcean.

## **4.2 Servicios locales**

`docker compose up` levantará el entorno completo: PostgreSQL, Valkey, RabbitMQ con su interfaz administrativa, Aspire Dashboard, Mailpit, la API y el worker. Para depurar la API o el worker desde Visual Studio, se levantarán sólo los servicios de soporte.

El entorno local utilizará los mismos protocolos que producción, aunque los servicios administrados de DigitalOcean se contratarán solamente cuando sea necesario desplegar.

## **4.3 Configuración y secretos**

* appsettings.json contendrá valores no sensibles y configuraciones predeterminadas.  
* Los secretos locales se almacenarán en un archivo .env excluido de Git. El script `scripts/configurar-secretos.sh` los carga en dotnet user-secrets, compartidos por la API y el worker.  
* El repositorio incluirá .env.example sin contraseñas reales.  
* App Platform suministrará secretos y cadenas de conexión mediante variables protegidas.  
* Las versiones de paquetes se centralizarán en Directory.Packages.props y las imágenes Docker utilizarán etiquetas explícitas.

## **4.4 Puesta en marcha local**

1. Clonar el repositorio y copiar .env.example como .env.  
2. Ejecutar `docker compose up -d --build --wait` para levantar el entorno completo.  
3. Para desarrollar desde Visual Studio: levantar sólo los servicios de soporte, ejecutar `scripts/configurar-secretos.sh` y ejecutar la API con Visual Studio o con dotnet run. La API sirve también el Backoffice y las aplicaciones WebAssembly.  
4. Aplicar las migraciones controladas de cada módulo.  
5. Ejecutar dotnet test antes de publicar cambios. Si Windows bloquea los ensamblados compilados, `scripts/test-en-docker.sh` ejecuta las pruebas en un contenedor Linux, igual que el pipeline.

# **5 Organización inicial del repositorio**

```
src/
  Logistica.Api/                    host único: API, Backoffice y aplicaciones WebAssembly
  Logistica.Backoffice/             biblioteca Razor: layout y páginas comunes
  Logistica.PortalComercio/         Blazor WebAssembly (/portal)
  Logistica.SeguimientoPublico/     Blazor WebAssembly (/seguimiento)
  Logistica.Repartidor.Pwa/         Blazor WebAssembly PWA (/repartidor)
  Logistica.Http.Contracts/         DTOs de la API, compartidos con las aplicaciones WebAssembly
  Logistica.Worker/
  BuildingBlocks/
    Logistica.SharedKernel/
    Logistica.BuildingBlocks.Infrastructure/
  Modules/
    Envios/
      Logistica.Modules.Envios/
        Domain/
        Application/Features/
        Infrastructure/
        Presentation/Features/
        EnviosModule.cs
      Logistica.Modules.Envios.Contracts/
    Administracion/  Planificacion/  Ejecucion/  Seguimiento/  Deposito/
tests/
  Logistica.UnitTests/
  Logistica.IntegrationTests/
  Logistica.ArchitectureTests/
infra/terraform/
scripts/
docs/
docker-compose.yml
.env.example
global.json
Directory.Build.props
Directory.Packages.props
Logistica.slnx
```

Cada módulo conserva internamente Domain, Application, Infrastructure y Presentation, y publica solamente su proyecto Contracts. Las pruebas de arquitectura verifican estas reglas en el pipeline.

# **6 Integración y despliegue**

```
Rama de trabajo -> Pull request -> GitHub Actions
GitHub Actions  -> restore -> build -> test, y construcción de las imágenes de la API y el worker
Merge aprobado a main -> DigitalOcean App Platform -> despliegue automático de la API y el worker
Terraform -> creación, actualización y destrucción de la infraestructura
```

La rama main permanecerá protegida y solamente recibirá cambios mediante pull requests con verificaciones exitosas. App Platform observará main y desplegará dos servicios: la API, que sirve también el Backoffice y las aplicaciones WebAssembly, y el worker. Terraform definirá los recursos de DigitalOcean, sus variables por ambiente y las operaciones reproducibles de plan, aplicación y destrucción.

App Platform administrará el enrutamiento público y HTTPS, por lo que no se incorporará Caddy. El equipo de tres integrantes está eximido del requisito de ejecutar dos instancias balanceadas de la API.

# **7 Decisiones operativas pendientes**

* Alojamiento definitivo de RabbitMQ en producción: servicio administrado o instancia separada.  
* Contratación de DigitalOcean Managed Valkey y PostgreSQL según el costo disponible.  
* Despliegue y protección del Aspire Dashboard en el ambiente remoto.  
* ~~Biblioteca cliente de RabbitMQ.~~ Propuesta en el ADR-0003: RabbitMQ.Client.  
* ~~Almacenamiento de fotografías, firmas y otros archivos de evidencia.~~ Resuelto: en PostgreSQL (sección 3.9).  
* ~~Proveedor de correo.~~ Resuelto: SMTP, con Mailpit en desarrollo y Brevo en el ambiente desplegado (sección 3.9).

Estas decisiones no impiden presentar el stack ni comenzar el desarrollo. Se cerrarán antes de implementar o desplegar el componente correspondiente.

# **8 Decisiones que requieren ADR posterior**

| Decisión | Estado |
| :---- | :---- |
| Modo de renderizado de Blazor WebAssembly y sus consecuencias sobre escalado, estado, latencia y recursos (obligatorio, letra 6.2 y 8.2) | Pendiente |
| Política de resolución de conflictos de sincronización de la PWA (obligatorio, letra 8.2) | Pendiente |
| Casos de uso almacenados en caché y reglas de expiración e invalidación (obligatorio, letra 8.2) | Pendiente |
| Garantía de consistencia entre persistencia y publicación de eventos mediante Outbox (obligatorio, letra 8.2) | Propuesto: ADR-0003 |

# **9 Referencias**

* Enunciado del laboratorio, secciones 6.1 a 6.15 y 8.1 a 8.3.  
* ADR-0001 · Estilo arquitectónico interno y organización del código, y sus addenda 1 a 5.  
* ADR-0002 · Estrategia de multitenancy.  
* ADR-0003 · Outbox, mensajería y consistencia entre módulos (propuesto).  
* Microsoft Learn. ASP.NET Core, Blazor WebAssembly, Identity, Minimal APIs y pruebas de integración.  
* Npgsql Documentation. Entity Framework Core Provider for PostgreSQL.  
* RabbitMQ Documentation. Work Queues y Dead Letter Exchanges.  
* DigitalOcean Documentation. App Platform, Managed Databases y Terraform Provider.  
* OpenTelemetry y .NET Aspire Documentation.

# **10 Historial de versiones**

| Versión | Fecha | Descripción |
| :---: | :---: | :---- |
| 1.0 | 25/09/2026 | Propuesta inicial aceptada. |
| 1.1 | 01/10/2026 | Se incorporan las decisiones de los addenda 1 a 5 del ADR-0001 (host único, código compartido, endpoints en los módulos, comunicación entre módulos, ArchUnitNET), el ADR-0003 propuesto, el correo y el almacenamiento de archivos; se actualizan el entorno local, la organización del repositorio y el despliegue. |
| 1.2 | 03/10/2026 | Se incorporan Playwright y los requerimientos opcionales de pruebas elegidos por el equipo (sección 3.8). |
