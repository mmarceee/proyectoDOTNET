# **ADR-0004 · Modo de renderizado de Blazor**

| Estado | Propuesto — pendiente de revisión por el equipo |
| :---- | :---- |
| **Versión** | 1.0 |
| **Fecha** | 12 de octubre de 2026 |
| **Responsable** | Lucas Ottonello |
| **Equipo** | Equipo 1 - Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Modo de renderizado de Blazor (obligatorio, letra 6.2 y 8.2) |
| **Relacionado con** | ADR-0001, addenda 4 y 5; ADR-0002; Propuesta de stack y entorno de desarrollo |

**Este registro fundamenta el uso de Blazor WebAssembly para el portal del comercio, el seguimiento público y la PWA del repartidor. La interfaz se ejecuta en el navegador y utiliza la API para consultar o modificar datos. Se prioriza reducir el trabajo de presentación en el servidor, facilitar un eventual escalado horizontal y permitir la operación sin conexión del repartidor, aceptando una descarga inicial mayor y más consumo de recursos del dispositivo.**

# **1\. Contexto y restricciones**

La sección 6.2 de la letra exige Razor Pages para el Backoffice y Blazor para el portal del comercio y el seguimiento público. Permite elegir Server, WebAssembly o Auto, pero exige analizar el escalado horizontal, el manejo de estado, la latencia y los recursos del servidor. La sección 8.2 exige documentar la decisión, las alternativas descartadas y sus consecuencias.

El equipo tiene tres integrantes y está eximido del requerimiento de escalabilidad y balanceo de carga. Esto permite trabajar con una instancia de la API, pero no elimina el análisis del escalado requerido por la sección 6.2. La letra también permite reemplazar la aplicación MAUI por una PWA con operación sin conexión.

Las aplicaciones tienen necesidades diferentes:

* **Portal del comercio:** formularios de alta, consultas y gestión de envíos. Las operaciones confirmadas requieren comunicación con la API.
* **Seguimiento público:** acceso mediante un enlace, sin autenticación de usuario. La letra lo identifica como el componente con mayor volumen de tráfico; también importa el tiempo de carga para destinatarios que lo abren por primera vez.
* **PWA del repartidor:** trabajo en calle con conectividad intermitente. Debe conservar información y operaciones pendientes en el dispositivo para sincronizarlas posteriormente.

El ADR-0001, addenda 4 y 5, ya define un único host web, `Logistica.Api`, que sirve desde el mismo origen:

| Ruta | Aplicación |
| :---- | :---- |
| `/api/...` | API de los módulos |
| `/backoffice/...` | Backoffice con Razor Pages |
| `/portal/...` | Portal del comercio |
| `/seguimiento/...` | Seguimiento público sin inicio de sesión; enlace individual `/seguimiento/{token}` |
| `/repartidor/...` | PWA del repartidor |

El token de seguimiento forma parte del enlace que recibe el destinatario. No es un token de inicio de sesión: permite acceder al envío correspondiente sin una cuenta de usuario. El CU-60 define un token firmado y no adivinable para evitar exponer identificadores internos o permitir recorrer envíos cambiando un número. Quien tenga un enlace válido podrá consultar ese envío. La letra exige acceso mediante enlace sin autenticación (RF 25) y protección contra la enumeración (6.3); el token firmado es la solución definida por el equipo, no un formato exigido por la letra. Su implementación corresponde al CU-60.

Este ADR complementa esas decisiones y fundamenta el modo de ejecución de las aplicaciones Blazor.

# **2\. Decisión adoptada**

## **2.1 Ejecución de la interfaz en el navegador**

Se utilizará **Blazor WebAssembly (client side)** en las tres aplicaciones, con capacidades de PWA para el repartidor. El navegador ejecuta los componentes de la interfaz; el servidor ejecuta los casos de uso y accede a la base de datos.

Por ejemplo, al crear un envío, el portal mantiene el formulario en el navegador. Al guardar, envía una petición HTTP a la API, que valida la operación y persiste el envío. El navegador muestra el resultado recibido.

Se mantendrán los proyectos WebAssembly separados y servidos por la API, sin renderizado de sus componentes en el servidor. Alojar sus archivos en ASP.NET Core no convierte su ejecución en Blazor Server. Esta distinción corresponde a los [modelos de alojamiento de Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-10.0).

## **2.2 Manejo de estado y autoridad del servidor**

Se distinguirán tres clases de estado:

| Estado | Ubicación y tratamiento |
| :---- | :---- |
| Interfaz temporal: filtros, selección y formulario sin guardar | Memoria del navegador. Puede perderse al recargar o cerrar la aplicación si no se persiste explícitamente. |
| Datos de negocio confirmados: envíos, rutas y entregas | Base de datos del servidor, accedida mediante la API. |
| Información descargada y operaciones pendientes de la PWA | Almacenamiento local persistente del dispositivo, con protección de datos y sincronización posterior. |

La persistencia del estado local debe implementarse explícitamente; mantenerlo en un componente o servicio en memoria no lo hace durable. Véase [manejo de estado en Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/?view=aspnetcore-10.0).

Las validaciones del cliente ayudan al usuario, pero la API vuelve a validar reglas, permisos y aislamiento por operador y comercio según el ADR-0002. Una entrega pendiente en el dispositivo no equivale a una entrega confirmada por el servidor. La política de conflictos e idempotencia de la sincronización se documentará en su ADR específico.

## **2.3 Escalado horizontal**

Escalar horizontalmente significa ejecutar varias instancias de la API detrás de un balanceador. La elección evita conservar componentes y formularios Blazor por usuario en esas instancias: la interfaz permanece en el navegador mientras las peticiones HTTP pueden atenderse en distintas instancias.

**Host único significa un único tipo de aplicación web desplegable, no una prohibición de ejecutar varias réplicas.** El laboratorio utilizará una instancia por la exención del equipo; una ampliación futura deberá resolver el estado compartido de la API, la caché, las tareas en segundo plano y la distribución de eventos SignalR. Las cookies requerirán claves de protección compatibles entre instancias, según la [configuración de Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

WebAssembly elimina la necesidad de afinidad por circuitos de Blazor, pero no garantiza por sí solo que todo el sistema esté preparado para balanceo o tolerancia a fallos.

## **2.4 Latencia y recursos**

Se acepta descargar el entorno .NET y el código cliente antes de utilizar la interfaz. Luego, las interacciones locales no necesitan viajar al servidor, aunque las consultas y escrituras de la API siguen dependiendo de la red.

La ejecución de componentes utiliza CPU y memoria del dispositivo. El servidor sigue atendiendo archivos, API y procesos de negocio, pero no ejecuta una interfaz Blazor por cada cliente. Estas ventajas y costos se describen en los [modelos de alojamiento](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-10.0).

## **2.5 Operación sin conexión y tiempo real**

La PWA conservará los recursos necesarios para abrir la aplicación y los datos necesarios para el trabajo sin conexión. Se deberán implementar almacenamiento local, registro de operaciones pendientes y sincronización al recuperar conectividad. La caché del service worker no resuelve por sí sola la persistencia de entregas. Véase [Blazor PWA](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0).

El service worker se limitará a `/repartidor/`, según el addendum 5. El portal y el seguimiento requieren conectividad para obtener datos actuales o confirmar operaciones.

SignalR se utilizará para las actualizaciones en tiempo real previstas en el stack. Transportar novedades mediante SignalR es independiente del lugar donde se ejecuta la interfaz: no convierte WebAssembly en Blazor Server.

# **3\. Alternativas consideradas y motivo de su descarte**

## **3.1 Blazor Server**

Blazor Server ejecuta los componentes en el servidor y comunica sus actualizaciones mediante SignalR. Mantiene un **circuito** por pantalla abierta: el contexto de los componentes y su estado, como un formulario todavía sin guardar. Su descarga inicial es menor y exige menos trabajo al dispositivo. Sin conexión no puede mantener la interacción de los componentes. Véase [modelos de alojamiento](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-10.0).

Con más clientes, se necesitan recursos para más circuitos. Al balancear, debe gestionarse la afinidad para dirigir al cliente a la instancia que conserva su circuito, y la recuperación ante interrupciones. Server puede escalar, pero agrega estas responsabilidades. Véase [alojamiento y escalabilidad de Blazor Server](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/server/?view=aspnetcore-10.0).

**Motivo de descarte:** no satisface la operación offline del repartidor y agrega trabajo de presentación al servidor. Para el portal y el seguimiento se prioriza compartir el modelo de ejecución con la PWA y evitar esa carga, aun aceptando una descarga inicial mayor.

## **3.2 Modo Auto**

Auto puede utilizar inicialmente Interactive Server mientras descarga WebAssembly y usar Interactive WebAssembly en visitas posteriores. No traslada automáticamente un componente activo del servidor al navegador cuando termina la descarga. Requiere contemplar ambos entornos de ejecución y sus servicios. Véase [modos de renderizado](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0).

**Motivo de descarte:** la mejora del arranque inicial no compensa, para el alcance y tamaño del equipo, la necesidad de implementar y verificar ambos modos. Durante la ejecución Server conserva sus necesidades de conectividad y circuitos. Además, incorporarlo requiere adaptar los proyectos actuales; no es una opción de configuración de los clientes WebAssembly existentes.

## **3.3 Modos diferentes para cada aplicación**

Se consideró WebAssembly para el repartidor y Server o Auto para el portal y el seguimiento. Esto permitiría priorizar una carga inicial menor en el seguimiento público.

**Motivo de descarte:** obliga a mantener distintos modelos de ejecución, manejo de estado y pruebas. Se prioriza la uniformidad del equipo. La carga inicial del seguimiento se medirá; si resulta inadecuada para su uso real, esta elección deberá revisarse.

# **4\. Consecuencias**

## **4.1 Positivas**

* La interfaz no necesita circuitos de Blazor en el servidor, lo que simplifica un eventual balanceo de las peticiones HTTP.
* Las tres aplicaciones comparten un modelo de ejecución y de acceso a los casos de uso mediante la API.
* La elección es compatible con la PWA offline prevista para el repartidor.
* Se preservan el host, las rutas y los límites entre cliente y módulos definidos en el ADR-0001.

## **4.2 Negativas y riesgos**

* La primera carga es más pesada, especialmente para el seguimiento público abierto por primera vez. Se recomienda controlar el tamaño de descarga y medir el arranque en red móvil como evaluación de este riesgo.
* Los dispositivos con menos recursos pueden ofrecer una experiencia inferior. Se recomienda evaluar la experiencia en un dispositivo representativo de los repartidores.
* La PWA requiere resolver datos pendientes, conflictos, protección del almacenamiento local y actualización de archivos en caché. La configuración actual no demuestra que esos flujos estén completos.
* La interfaz descargada puede inspeccionarse y modificarse. No se incluirán secretos ni se confiará en decisiones de autorización tomadas por el cliente.
* Las aplicaciones se publican junto con la API: cambiar el cliente requiere volver a desplegar el host, según el ADR-0001.
* Adoptar Server, Auto o prerenderizado en el futuro exigirá revisar el arranque, los servicios y el manejo de estado; no será un cambio puramente visual.

# **5\. Evidencia actual y verificaciones relacionadas**

La configuración actual respalda la ejecución cliente: los tres proyectos usan `Microsoft.NET.Sdk.BlazorWebAssembly`, arrancan con `WebAssemblyHostBuilder` y cargan `blazor.webassembly.js`. La API sirve archivos estáticos y el `index.html` correspondiente a cada ruta. La PWA registra su service worker bajo `/repartidor/`.

La letra no exige una batería específica de pruebas para este ADR: las secciones 6.2 y 8.2 exigen el análisis y la justificación de la decisión. Las comprobaciones siguientes distinguen recomendaciones de evaluación y obligaciones del sistema. No constituyen cinco tareas nuevas con fechas ya acordadas.

| Comprobación | Carácter y estado | Momento de referencia |
| :---- | :---- | :---- |
| Acceso directo a las rutas de las aplicaciones y entrega de sus recursos | Existen pruebas de integración en `AplicacionesWebAssemblyTests.cs` que comprueban el HTML por ruta, el script de Blazor y el service worker. No equivalen a una prueba completa de navegación en un navegador. | Durante la integración del host; repetir al modificar rutas o publicación. |
| Tamaño descargado y tiempo de arranque con caché vacía y reutilizada | Recomendación para evaluar el costo de WebAssembly. La letra no exige estas mediciones específicas. Sin fecha ni responsable de ejecución acordados en este ADR. | Propuesta: cuando el seguimiento público esté funcional y durante la revisión del despliegue. |
| Comportamiento con red lenta o interrumpida | Evaluación recomendada para las interfaces web. La continuidad offline del repartidor sí es obligatoria. | Para la PWA, junto con el hito del 22/10; para portal y seguimiento, al verificar sus flujos. |
| Operación offline, conservación de datos y sincronización de la PWA | Obligación funcional (RF 18 y 24); el cierre y reapertura es una comprobación propuesta de persistencia. Corresponde a CU-50 y CU-52 y al ADR de conflictos de sincronización. | Hito del 22/10 según el plan de casos de uso; repetir en la revisión final. |
| Permisos y aislamiento por operador y comercio en la API | Obligación de seguridad y multitenancy (6.3 y 6.5), independiente del modo de Blazor. Corresponde a las pruebas de seguridad y al ADR-0002. | Hito del 15/10 y al incorporar cada endpoint protegido. |

Las fechas de hitos provienen de la letra y del plan del equipo. No se presentan resultados de ejecución nuevos ni se fijan cifras de ahorro de memoria, latencia o capacidad sin mediciones del sistema.

# **6\. Referencias**

* Letra del laboratorio: secciones 4, 6.2, 6.3, 6.13, exención para equipos de tres y 8.2.
* [ADR-0001 · Estilo arquitectónico interno y organización del código](ADR-0001-estilo-arquitectonico.md), sección 2.6 y addenda 4 y 5 integrados en la versión 2.0.
* [ADR-0002 · Estrategia de multitenancy](ADR-0002-estrategia-de-multitenancy.md).
* [Propuesta de stack y entorno de desarrollo](propuesta-stack-y-entorno-desarrollo.md), capa de presentación y decisiones pendientes.
* [Casos de uso](casos-de-uso.md), CU-60, CU-50 y CU-52; [Plan de casos de uso por hito](plan-casos-de-uso-por-hito.md), hitos del 15/10 y 22/10.
* Microsoft Learn: documentación oficial enlazada en las secciones 2 y 3, consultada el 12 de octubre de 2026.

# **7\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---- | :---- | :---- | :---- |
| 1.0 | 12/10/2026 | Propuesta inicial: justificación de WebAssembly frente a Server y Auto, consecuencias y verificación prevista. | Lucas Ottonello |
