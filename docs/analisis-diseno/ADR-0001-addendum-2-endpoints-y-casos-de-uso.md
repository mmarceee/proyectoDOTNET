# **ADR-0001 · Addendum 2 · Ubicación de los endpoints, los casos de uso y los contratos**

| Estado | Propuesto — pendiente de revisión por el responsable del ADR-0001 |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Responsable del ADR-0001** | Lucas Ottonello |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Modifica** | ADR-0001, sección 2.2 (Organización interna y estructura de Contracts), sección 2.3 (Vertical slices) y sección 2.4 (Regla de dependencias) |
| **Relacionado con** | ADR-0001 · Addendum 1 · Código compartido; ADR-0001 · Addendum 3 · Comunicación entre módulos; Guía de decisiones tecnológicas, secciones 2.3 y 3.2 |

**Este addendum resuelve tres inconsistencias de estructura: los endpoints de cada caso de uso se ubican dentro del módulo, en una capa `Presentation`; los casos de uso se agrupan bajo una carpeta `Features`; y los proyectos `Contracts` se organizan según los mecanismos de comunicación del addendum 3. El proyecto `Logistica.Api` queda como anfitrión que compone los módulos.**

# **1\. Contexto**

Al crear el esqueleto de la solución se detectaron tres diferencias entre documentos aceptados, o entre ellos y las decisiones posteriores:

•  **Ubicación de los endpoints.** El ADR-0001, sección 2.2, establece que los endpoints se ubican *"en el proyecto anfitrión y organizados por módulo"*. La Guía de decisiones tecnológicas, sección 2.3, muestra en cambio el endpoint dentro de la carpeta del caso de uso (`CrearEnvio/CrearEnvioEndpoint.cs`), junto al command y el handler.  
•  **Carpeta de los casos de uso.** El ADR-0001, sección 2.3, ubica los casos de uso directamente bajo `Application/` (`Application/CrearEnvio/`), mientras que la estructura creada por el equipo usa `Application/Features/`.  
•  **Estructura de Contracts.** El ADR-0001, sección 2.2, organiza cada proyecto `Contracts` en `Commands/`, `Queries/`, `Results/` y `Events/`. Esa estructura supone que los módulos se envían commands y queries a través de un mediador, algo que no se decidió, y no prevé un lugar para las interfaces de comunicación síncrona que define el addendum 3.

La primera diferencia no se puede resolver adoptando literalmente la Guía: colocar el endpoint dentro de `Application/` haría que la capa Application dependa de ASP.NET Core, lo que viola la regla de dependencias del ADR-0001, sección 2.4.

# **2\. Decisión**

## **2.1 Los endpoints viven en el módulo, en la capa Presentation**

Cada proyecto de módulo incorpora una carpeta `Presentation/`, que refleja la estructura de `Application/Features/`:

Logistica.Modules.Envios/  
  Domain/  
  Application/  
    Features/  
      CrearEnvio/  
        CrearEnvioCommand.cs  
        CrearEnvioHandler.cs  
  Infrastructure/  
  Presentation/  
    Features/  
      CrearEnvio/  
        CrearEnvioEndpoint.cs  
        CrearEnvioRequest.cs  
  EnviosModule.cs

El endpoint traduce la petición HTTP a un command o query, invoca al handler y convierte el resultado en una respuesta HTTP, tal como establece la Propuesta de stack, sección 3.3. No contiene reglas de negocio ni acceso a datos.

## **2.2 Clase de entrada del módulo**

Cada módulo expone una única clase pública de entrada, `<Modulo>Module`, con dos métodos de extensión:

•  `Add<Modulo>Module(IServiceCollection, IConfiguration)`: registra los handlers, el `DbContext` y los adaptadores del módulo.  
•  `Map<Modulo>Endpoints(IEndpointRouteBuilder)`: registra los endpoints del módulo bajo un grupo de rutas propio (`/api/envios`, `/api/planificacion`, etc.).

`Logistica.Api` se limita a invocar estos métodos y a configurar lo transversal: autenticación, resolución del inquilino (ADR-0002), logging, telemetría y health checks. No contiene endpoints de casos de uso.

## **2.3 Encapsulamiento**

Como el endpoint y el handler están en el mismo proyecto, los commands, queries, handlers y entidades internas del módulo pueden declararse `internal`. Lo único público del proyecto del módulo es la clase de entrada `<Modulo>Module`; los tipos que otros módulos necesiten se publican, como hasta ahora, en el proyecto `Contracts`.

Para que las pruebas unitarias puedan ejercitar los tipos internos, cada módulo declara `InternalsVisibleTo` hacia `Logistica.UnitTests`.

## **2.4 Casos de uso bajo Features**

Los casos de uso se agrupan bajo `Application/Features/<CasoDeUso>/`, y sus endpoints bajo `Presentation/Features/<CasoDeUso>/`. Esto reemplaza el ejemplo de la sección 2.3 del ADR-0001, que los ubicaba directamente bajo `Application/`.

## **2.5 Regla de dependencias (agrega a la sección 2.4 del ADR-0001)**

El proyecto de cada módulo incorpora una referencia al framework ASP.NET Core (`Microsoft.AspNetCore.App`). Para que esa referencia no se filtre a las demás capas:

•  Domain no depende de Application, Infrastructure, Presentation, Entity Framework Core ni ASP.NET Core.  
•  Application no depende de Infrastructure, Presentation, Entity Framework Core ni ASP.NET Core.  
•  Infrastructure no depende de Presentation ni de ASP.NET Core.  
•  Presentation puede depender de Application y de Contracts, pero no de Infrastructure.  
•  Sólo `Presentation` y la clase de entrada `<Modulo>Module` pueden usar ASP.NET Core.  
•  Un módulo no puede depender de Domain, Application, Infrastructure ni Presentation de otro módulo.

## **2.6 Estructura de los proyectos Contracts (reemplaza la estructura de Contracts de la sección 2.2 del ADR-0001)**

El proyecto `Contracts` es la única parte de un módulo que los demás módulos pueden usar. Su contenido se corresponde con los mecanismos de comunicación del addendum 3:

Logistica.Modules.Envios.Contracts/  
  IEnviosModuleApi.cs  
  Results/  
    EnvioResumen.cs  
  Events/  
    EnvioEntregado.cs

•  **`I<Modulo>ModuleApi`**: interfaz con las consultas y acciones que otros módulos pueden invocar de forma síncrona y en memoria. La implementa una clase `internal` de la capa Application del módulo, registrada en `Add<Modulo>Module()`. Se evita el nombre `I<Modulo>Module` para no confundirla con la clase de entrada `<Modulo>Module` de la sección 2.2.  
•  **`Results/`**: tipos de datos, preferentemente `record` inmutables, que la interfaz recibe o devuelve. Nunca se exponen entidades del dominio.  
•  **`Events/`**: eventos de integración que el módulo publica para que otros reaccionen. Derivan del tipo base definido en `Logistica.SharedKernel` (addendum 1).

No se crean las carpetas `Commands/` ni `Queries/`: los módulos no se envían commands ni queries entre sí, sino que invocan los métodos de `I<Modulo>ModuleApi`. Los commands y queries de cada caso de uso siguen existiendo, pero son internos al módulo (`Application/Features/`).

Las carpetas se crean cuando se agrega el primer archivo que las necesita; no se versionan carpetas vacías.

# **3\. Alternativas consideradas**

## **3.1 Endpoints en el proyecto Logistica.Api (texto original del ADR-0001)**

Los endpoints se organizarían en `Logistica.Api/Endpoints/<Modulo>/<CasoDeUso>/`.

**Motivo de descarte:**

•  Cada caso de uso quedaría repartido en dos proyectos: el endpoint en la API y el command y el handler en el módulo. Agregar o modificar un caso de uso, incluido el cambio de requerimientos del 29 de octubre, obligaría a tocar ambos.  
•  Los commands, queries y handlers deberían ser públicos para que la API pueda usarlos, con lo cual el compilador deja de proteger el interior del módulo y esa protección queda sólo en manos de las pruebas de arquitectura.  
•  El proyecto de la API acumularía las carpetas de endpoints de los seis módulos, en lugar de limitarse a su rol de anfitrión.

Es una alternativa válida y funcional; se descarta por menor cohesión y menor encapsulamiento, no por ser incorrecta.

## **3.2 Endpoint dentro de la carpeta del caso de uso en Application (Guía, sección 2.3)**

**Motivo de descarte:** Application pasaría a depender de ASP.NET Core, lo que viola la regla de dependencias del ADR-0001 y haría fallar la prueba de arquitectura existente.

## **3.3 Un proyecto de presentación separado por módulo (Logistica.Modules.X.Presentation)**

**Motivo de descarte:** separaría físicamente ASP.NET Core del resto del módulo, pero agrega seis proyectos más a la solución y obliga a volver públicos los handlers para que el proyecto de presentación pueda invocarlos, con lo que se pierde el beneficio de la sección 2.3. La separación entre capas ya queda garantizada por las pruebas de arquitectura.

## **3.4 Mantener Commands/, Queries/, Results/ y Events/ en Contracts (estructura original del ADR-0001)**

**Motivo de descarte:** las carpetas `Commands/` y `Queries/` sólo tienen sentido si los módulos se comunican enviándose commands y queries a través de un mediador, lo que obligaría a incorporar una biblioteca adicional o a construir ese mecanismo. Una interfaz por módulo resuelve la comunicación síncrona con código más simple, más fácil de explicar y con el mismo aislamiento.

# **4\. Consecuencias**

## **4.1 Positivas**

•  Un caso de uso queda completo dentro de un único módulo: endpoint, command, handler y reglas de dominio.  
•  El interior del módulo puede ser `internal`, y el compilador impide usarlo desde fuera.  
•  `Logistica.Api` se mantiene pequeño y enfocado en aspectos transversales.  
•  El ADR-0001 y la Guía quedan alineados.

## **4.2 Negativas y riesgos**

•  El proyecto de cada módulo tiene disponible ASP.NET Core, por lo que la separación entre Presentation y las demás capas depende de las pruebas de arquitectura y no de las referencias entre proyectos.  
•  Las páginas del Backoffice (Razor Pages) no se ven afectadas por esta decisión; cómo invocan los casos de uso de los módulos queda como decisión abierta.

# **5\. Verificación**

La decisión se considera verificada cuando:

1\.  Cada módulo tiene las carpetas `Application/Features/` y `Presentation/Features/` y una clase `<Modulo>Module`.

2\.  `Logistica.Api` registra los seis módulos mediante `Add<Modulo>Module()` y `Map<Modulo>Endpoints()`.

3\.  Existen pruebas automatizadas que fallan si Domain, Application o Infrastructure dependen de ASP.NET Core o de Presentation, o si Presentation depende de Infrastructure.

4\.  Las pruebas se ejecutan en el pipeline de integración continua.

5\.  Los proyectos `Contracts` sólo contienen la interfaz `I<Modulo>ModuleApi` y las carpetas `Results/` y `Events/`, a medida que se necesitan.

Los puntos 1 a 4 ya están implementados en el esqueleto de la solución (`tests/Logistica.ArchitectureTests/ModuleDependencyTests.cs`). El punto 5 se verifica en la revisión de código al agregar el primer contrato de cada módulo.

# **6\. Decisiones abiertas**

| Tema | Por qué importa |
| :---- | :---- |
| Cómo invoca el Backoffice los casos de uso | Si el Backoffice llama a la API por HTTP, o si se ejecuta en el mismo proceso y usa directamente los módulos, cambia qué proyectos referencia y cómo se resuelve el inquilino. |

# **7\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Propuesta inicial del addendum. | Ezequiel Marcenal |
| 0.2 | 30/09/2026 | Se incorpora la estructura de los proyectos Contracts (sección 2.6 y alternativa 3.4). | Ezequiel Marcenal |
