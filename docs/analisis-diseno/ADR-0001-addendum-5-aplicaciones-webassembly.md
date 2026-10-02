# **ADR-0001 · Addendum 5 · Aplicaciones Blazor WebAssembly en el mismo host y DTOs HTTP compartidos**

| Estado | Aceptado |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Responsable del ADR-0001** | Lucas Ottonello |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Modifica** | ADR-0001, sección 2.2 (Organización interna); addendum 2, sección 2.1 (ubicación de los DTOs de request) |
| **Relacionado con** | ADR-0001 · Addendum 4 · Backoffice; Guía de decisiones tecnológicas, secciones 3.1 y 4.1; Propuesta de stack, sección 3.4 |

**Este addendum resuelve la decisión pendiente del addendum 4: las tres aplicaciones Blazor WebAssembly se sirven desde `Logistica.Api`, cada una bajo su propia ruta, en el mismo origen que la API y el Backoffice. Los DTOs de request y response de la API HTTP se ubican en un proyecto compartido, `Logistica.Http.Contracts`.**

# **1\. Contexto**

El portal del comercio, el seguimiento público y la aplicación del repartidor son aplicaciones Blazor WebAssembly: se descargan al navegador y llaman a la API por HTTP. La Guía de decisiones tecnológicas, sección 4.1, establece que las aplicaciones se publicarán bajo un mismo dominio lógico para que el navegador envíe la cookie de Identity a la API de forma controlada.

Con el addendum 4, `Logistica.Api` ya sirve la API y el Backoffice. Quedaban por resolver:

1\.  Desde dónde se sirven los archivos de las aplicaciones WebAssembly y bajo qué rutas.

2\.  Cómo se ejecutan durante el desarrollo.

3\.  El alcance del service worker de la aplicación del repartidor.

4\.  Dónde se definen los tipos de request y response que comparten las aplicaciones y la API.

# **2\. Decisión**

## **2.1 Un único host y un único origen**

`Logistica.Api` sirve los archivos estáticos de las tres aplicaciones:

| Ruta | Contenido |
| :---- | :---- |
| `/api/...` | Minimal APIs de los módulos |
| `/backoffice/...` | Razor Pages del Backoffice (addendum 4) |
| `/portal/...` | Portal del comercio |
| `/seguimiento/...` | Seguimiento público; el enlace de un envío es `/seguimiento/{token}` (ADR-0002) |
| `/repartidor/...` | Aplicación del repartidor (PWA) |
| `/health` | Health check |

Como todo comparte origen, no se configura CORS y la cookie de Identity puede emitirse con `SameSite=Strict`.

Cada aplicación declara su ruta con `StaticWebAssetBasePath` y con `<base href>` en su `index.html`. La API mapea, para cada una, un fallback que devuelve su `index.html` ante cualquier ruta sin extensión de archivo, de modo que la navegación interna de Blazor funcione al recargar la página o al abrir un enlace directo.

## **2.2 Desarrollo desde la API**

Durante el desarrollo se ejecuta un único proyecto, `Logistica.Api`, que sirve las tres aplicaciones y habilita la depuración de WebAssembly (`UseWebAssemblyDebugging`). No se usan los servidores de desarrollo propios de cada aplicación: tendrían otro origen y obligarían a configurar CORS y la cookie de forma distinta que en producción.

## **2.3 Alcance del service worker de la PWA**

El service worker de la aplicación del repartidor se registra con alcance `/repartidor/`, y el manifiesto de la PWA declara `scope` y `start_url` relativos a esa ruta. El service worker no intercepta las peticiones del portal, del seguimiento, del Backoffice ni de otras rutas del sitio.

Las URLs del manifiesto de recursos generado por Blazor (`service-worker-assets.js`) ya incluyen el prefijo `repartidor/`. Por eso el service worker publicado las resuelve desde la raíz del sitio y no desde su propia ubicación.

## **2.4 DTOs HTTP en Logistica.Http.Contracts**

Los tipos de request y response de los endpoints se ubican en un proyecto compartido, `Logistica.Http.Contracts`:

•  Lo referencian los proyectos de los módulos, que lo usan en sus endpoints, y las tres aplicaciones WebAssembly.  
•  No tiene dependencias: también se compila para el navegador.  
•  Es distinto de los proyectos `Contracts` de cada módulo, que definen la comunicación entre módulos (addenda 2 y 3) y nunca llegan al navegador.

Esto reemplaza la ubicación de `CrearEnvioRequest.cs` en `Presentation/Features/` que mostraba el ejemplo del addendum 2: los DTOs de un endpoint que consume una aplicación WebAssembly se definen en `Logistica.Http.Contracts`.

## **2.5 Regla de dependencias (agrega a la sección 2.4 del ADR-0001)**

•  `Logistica.Http.Contracts` no depende de los módulos, de los BuildingBlocks, de ASP.NET Core ni de Entity Framework Core.  
•  Las aplicaciones WebAssembly no dependen de los módulos, de los BuildingBlocks ni del Backoffice: sólo se comunican con la API por HTTP.

# **3\. Alternativas consideradas**

## **3.1 Cada aplicación WebAssembly en su propio servicio o dominio**

**Motivo de descarte:** exige configurar CORS, emitir la cookie con `SameSite=None` y protegerla adicionalmente contra CSRF, y desplegar tres servicios estáticos más.

## **3.2 Servidores de desarrollo propios de cada aplicación**

**Motivo de descarte:** ofrecen recarga en caliente más cómoda, pero con otro origen: la autenticación se comportaría distinto en desarrollo que en producción.

## **3.3 DTOs duplicados en cada aplicación WebAssembly**

**Motivo de descarte:** un cambio en un endpoint no rompería la compilación del cliente que lo consume; el error aparecería recién en ejecución.

## **3.4 Clientes generados a partir de OpenAPI**

**Motivo de descarte:** agrega una herramienta y un paso de generación al flujo de trabajo, sin ventajas frente a un proyecto compartido cuando cliente y servidor están en la misma solución.

# **4\. Consecuencias**

## **4.1 Positivas**

•  Un solo origen: sin CORS, con la cookie más restrictiva y con la misma configuración en desarrollo y en producción.  
•  Un solo servicio web para desplegar en App Platform.  
•  El compilador detecta cuando un cambio en la API rompe a un cliente.  
•  La PWA funciona sin conexión dentro de su alcance, sin afectar a las demás aplicaciones.

## **4.2 Negativas y riesgos**

•  **Se desactiva `OverrideHtmlAssetPlaceholders`** en las tres aplicaciones. En .NET 10 esa opción publica el `index.html` procesado fuera de `StaticWebAssetBasePath`, y las tres aplicaciones chocan en `wwwroot/index.html` al publicar la API (error NETSDK1152). Sin ella, `index.html` referencia `_framework/blazor.webassembly.js` sin huella de contenido, como hasta .NET 9. Se pierde la precarga y el mapa de importación con huellas; la caché del navegador se sigue invalidando por los recursos con huella de `_framework`. Se puede revisar en una versión futura de .NET.  
•  El service worker queda instalado en el navegador de cada repartidor: un error en él es costoso de corregir. Cualquier cambio en `service-worker.published.js` debe verificarse con una prueba en navegador.  
•  La imagen de la API incluye las tres aplicaciones: un cambio en cualquiera de ellas requiere volver a desplegar la API.

# **5\. Verificación**

1\.  `tests/Logistica.IntegrationTests/AplicacionesWebAssemblyTests.cs` verifica que cada ruta de cada aplicación devuelve su `index.html`, que el runtime de Blazor se sirve bajo la ruta de la aplicación y que el service worker de la PWA se sirve en `/repartidor/`.

2\.  `tests/Logistica.ArchitectureTests/ClientDependencyTests.cs` verifica las reglas de la sección 2.5.

3\.  La imagen publicada de la API (`docker compose up`) se verificó en un navegador Chromium: las tres aplicaciones arrancan; el service worker de la PWA se activa con alcance `/repartidor/`; controla la PWA y no el portal; y con la red desconectada la PWA vuelve a cargar desde la caché.

# **6\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Propuesta inicial del addendum. | Ezequiel Marcenal |
| 0.2 | 02/10/2026 | Revisado y aceptado por el responsable del ADR-0001. | Lucas Ottonello |
