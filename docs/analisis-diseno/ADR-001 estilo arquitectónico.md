# **ADR 001 Estilo arquitectónico interno y organización del código**

| Estado | Aceptado |
| :---- | :---- |
| **Fecha** | 24 de septiembre de 2026 |
| **Responsable** | Lucas Ottonello |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Organización interna del monolito modular |

**Este registro propone organizar la solución como un monolito modular por capacidades de negocio, con casos de uso estructurados verticalmente y dependencias orientadas hacia el dominio. La decisión busca reducir el acoplamiento y facilitar la incorporación del cambio de requerimientos previsto para el 29 de octubre.**

# **1\. Contexto y restricciones**

El sistema a construir es una plataforma multioperador de distribución de última milla. Debe atender a varios operadores logísticos en un único despliegue, manteniendo aislados sus datos, usuarios, configuraciones y reglas de negocio.

La letra del laboratorio establece las siguientes restricciones arquitectónicas:

•  La solución se desarrollará en .NET 10\.  
•  La API y las aplicaciones web formarán un monolito modular.  
•  Existirá un servicio de procesamiento en segundo plano (worker) desplegado de forma independiente.  
•  El worker se comunicará con el resto de la solución de forma asíncrona mediante una cola de mensajes.  
•  La lógica de dominio no podrá depender de la infraestructura, del acceso a datos ni de los frameworks de presentación.  
•  La regla de dependencias deberá verificarse mediante una prueba automatizada de arquitectura ejecutada por el pipeline.  
•  El entorno completo deberá poder ejecutarse con Docker Compose.  
•  El 29 de octubre el equipo recibirá un cambio de requerimientos no anunciado que deberá incorporar obligatoriamente.

El dominio incluye áreas con responsabilidades diferentes, entre ellas tenencia y usuarios, configuración del operador, envíos, planificación de rutas, ejecución de entregas, notificaciones y liquidaciones. Si estas áreas se implementan sin límites claros, un cambio funcional puede afectar archivos y reglas distribuidos por toda la solución.

Por tanto, se necesita una organización que:

•  Mantenga la lógica de negocio independiente de ASP.NET Core, Entity Framework Core, la base de datos, la cola y otros servicios externos.  
•  Permita ubicar rápidamente el código correspondiente a una capacidad o caso de uso.  
•  Reduzca el impacto de cambios en una parte del negocio sobre las demás.  
•  Evite que clases de servicio generales concentren demasiadas responsabilidades.  
•  Sea comprensible y aplicable por todos los integrantes dentro del plazo del laboratorio.

# **2\. Decisión**

Se adoptará un **monolito modular organizado por capacidades de negocio**. Dentro de cada módulo, los casos de uso se organizarán como **vertical slices**, manteniendo una regla de dependencias inspirada en Clean Architecture y en arquitectura hexagonal.

Esta decisión complementa, pero no reemplaza, la arquitectura de despliegue exigida por la letra. El monolito modular y el worker independiente son restricciones del laboratorio; la organización por capacidades, vertical slices y dependencias hacia el dominio constituye la decisión interna del equipo.

## **2.1 Módulos de negocio**

De acuerdo con el modelo de dominio elaborado por el equipo, la aplicación se dividirá en los siguientes módulos:

1\.  Administración y configuración.

2\.  Envíos y entregas.

3\.  Planificación de rutas.

4\.  Ejecución de rutas.

5\.  Seguimiento y notificaciones.

6\.  Depósito y liquidaciones.

La propiedad principal de los conceptos se distribuirá de la siguiente manera:

•  **Administración y configuración:** Operador, Comercio, Usuario, Repartidor, Vehículo, Zona, versiones de reglas y tarifas.  
•  **Envíos y entregas:** Envío, Bulto, Destinatario, EventoEnvio, IntentoEntrega, Incidencia y DevoluciOn.  
•  **Planificación de rutas:** Ruta, Parada, criterios de ordenamiento y estados de planificación.  
•  **Ejecución de rutas:** EscaneoCarga, PosicionVehiculo, Rendición y LineaRendicion.  
•  **Seguimiento y notificaciones:** SuscripcionAviso, Notificacion y EntregaAviso.  
•  **Depósito y liquidaciones:** RecepcionDeposito, Liquidación y LineaLiquidacion.

Cada concepto de dominio tendrá un único módulo propietario. Cuando un concepto aparezca en otro módulo, se representará mediante su identificador o mediante un contrato público; no se duplicará la entidad ni se modificará directamente su estado interno.

## **2.2 Organización interna**

Cada módulo distinguirá las siguientes responsabilidades:

•  **Domain:** entidades, value objects, agregados, invariantes, eventos de dominio y servicios de dominio.  
•  **Application:** casos de uso, comandos, consultas, validación de entrada y coordinación de operaciones.  
•  **Infrastructure:** persistencia con Entity Framework Core, implementación de repositorios, mensajería, caché y adaptadores de servicios externos.  
•  **Presentation:** endpoints de API y componentes de presentación que invoquen casos de uso, ubicados en el proyecto anfitrión y organizados por módulo.

Para equilibrar el aislamiento con el tamaño del equipo, cada módulo se implementará mediante dos proyectos de biblioteca:

Logistica.Envíos  
  Domain/  
  Application/  
  Infrastructure/  
   
Logistica.Envíos.Contracts  
  Commands/  
  Queries/  
  Results/  
  Events/

El primer proyecto contendrá la implementación interna del módulo, separada mediante carpetas y namespaces. El proyecto Contracts contendrá exclusivamente los tipos públicos que puedan utilizar otros módulos. La API y el worker serán los proyectos ejecutables; las bibliotecas de los módulos se compilaran y desplegaran junto con el monolito.

Los endpoints de presentación se ubicarán en el proyecto de la API, organizados por módulo y caso de uso. Una prueba automatizada de arquitectura verificará las dependencias permitidas entre namespaces y ensamblados.

## **2.3 Vertical slices**

Dentro de Application y Presentation, el código se organizará principalmente por caso de uso. Por ejemplo:

Modules/  
  Envíos/  
    Domain/  
      Envío.cs  
      Bulto.cs  
      EventoEnvio.cs  
    Application/  
      CrearEnvio/  
        Command.cs  
        Validator.cs  
        Handler.cs  
      TransicionarEnvio/  
        Command.cs  
        Validator.cs  
        Handler.cs  
      ConsultarEnvio/  
        Query.cs  
        Handler.cs  
    Infrastructure/  
      Persistence/  
      Messaging/

Las reglas propias del negocio no se duplicarán dentro de los handlers. Por ejemplo, el handler de transición coordinará el caso de uso, pero la entidad Envío será responsable de determinar si la transición solicitada es válida.

## **2.4 Regla de dependencias**

Se aplicarán las siguientes reglas:

•  Domain no dependerá de Application, Infrastructure ni Presentation.  
•  Application podrá depender de Domain, pero no de Infrastructure ni Presentation.  
•  Infrastructure podrá depender de Application y Domain para implementar sus contratos.  
•  Presentation podrá depender de Application y de los contratos necesarios para iniciar casos de uso.  
•  Un módulo no podrá acceder directamente al DbContext, las tablas, los repositorios internos ni las entidades privadas de otro módulo.  
•  Un módulo sólo podrá referenciar el proyecto Contracts de otro módulo; no podrá referenciar sus namespaces internos de Domain, Application o Infrastructure.  
•  Cuando un caso de uso necesite una respuesta inmediata, la comunicación entre módulos será sincrónica y en memoria mediante una interfaz o caso de uso publicado en Contracts.  
•  Cuando un módulo solo necesite informar un hecho para que otro reaccione posteriormente, se utilizará un evento interno con un contrato público estable.  
•  No se utilizarán llamadas HTTP entre los módulos del monolito.  
•  La cola durable se reservará para la comunicación asíncrona con el worker independiente y para trabajos que requieran persistencia, reintentos o procesamiento fuera del proceso de la API.  
•  Las integraciones con base de datos, caché, mensajería, almacenamiento, correo y webhooks se implementarán como adaptadores externos a la lógica de dominio.

Estas reglas se controlarán mediante un proyecto de pruebas con **xUnit y NetArchTest.Rules**. Como mínimo, las pruebas verificarán que Domain no dependa de Infrastructure, Entity Framework Core ni ASP.NET Core; que Application no dependa de Infrastructure; y que un módulo no dependa de los namespaces internos de otro. Las pruebas se ejecutarán mediante dotnet test en el pipeline de integración continua.

## **2.5 Preparación para cambios**

No se intentará anticipar el requerimiento desconocido del 29 de octubre. En su lugar, se buscará que:

•  Las reglas de negocio tengan un único lugar de definición.  
•  Las reglas configurables no se escriban directamente en controladores, vistas o adaptadores.  
•  Los casos de uso estén localizados y tengan dependencias explicitas.  
•  Los módulos expongan contratos pequeños y estables.  
•  La infraestructura pueda cambiar sin modificar las entidades del dominio.

Esta estructura no garantiza que todo cambio afecte un solo archivo o módulo, pero reduce el acoplamiento accidental y permite identificar con mayor claridad los componentes involucrados.

# **3\. Alternativas consideradas**

## **3.1 Arquitectura tradicional por capas**

Se consideró organizar la solución en capas globales de presentación, servicios, repositorios y entidades.

**Motivo de descarte:** aunque es sencilla al inicio, distribuye cada funcionalidad entre carpetas técnicas globales. Un cambio de negocio puede requerir modificaciones en distintos sectores de la solución y favorecer la aparicion de servicios generales con demasiadas responsabilidades. Tampoco garantiza por sí sola el aislamiento entre los módulos del negocio.

## **3.2 Clean Architecture global**

Se consideró crear capas globales de Domain, Application, Infrastructure y Presentation para toda la solución.

**Motivo de descarte como organización principal:** cumple correctamente la regla de dependencias, pero una implementación exclusivamente global puede debilitar los límites entre capacidades de negocio. También puede producir demasiadas abstracciones compartidas y hacer que un cambio funcional atraviese varias carpetas generales.

Se conservará su regla de dependencias hacia el dominio dentro de la decisión adoptada.

## **3.3 Vertical Slice Architecture sin límites de dominio**

Se consideró organizar toda la solución únicamente por casos de uso.

**Motivo de descarte como solución completa:** facilita localizar funcionalidades, pero no determina donde deben residir las invariantes compartidas ni cómo impedir que los handlers dependan directamente de Entity Framework Core u otras implementaciones. Sin límites adicionales, la lógica de negocio podría quedar duplicada o dispersa.

Se conservará la organización por casos de uso dentro de cada módulo.

## **3.4 Arquitectura hexagonal aplicada a toda clase**

Se consideró modelar todos los componentes mediante puertos y adaptadores.

**Motivo de descarte como regla universal:** es valiosa para las dependencias externas, pero aplicada indiscriminadamente puede generar interfaces y adaptadores sin una necesidad real, aumentando el código ceremonial.

Se utilizará en los límites que interactúan con persistencia, mensajería, caché, almacenamiento y servicios externos.

## **3.5 Microservicios**

Se consideró separar las capacidades en servicios desplegables independientes.

**Motivo de descarte:** contradice la arquitectura de monolito modular establecida por la letra y agrega costos innecesarios de despliegue, comunicación por red, consistencia distribuida, observabilidad y operación. El tamaño del equipo y el plazo tampoco justifican esa complejidad.

# **4\. Consecuencias**

## **4.1 Consecuencias positivas**

•  Los cambios funcionales tenderán a quedar localizados en un módulo y en un conjunto acotado de casos de uso.  
•  La lógica de dominio podrá probarse sin iniciar la base de datos, la cola ni las aplicaciones web.  
•  Las dependencias externas podrán sustituirse o simularse mediante adaptadores.  
•  La estructura del código reflejará las capacidades del negocio representadas en el modelo de dominio.  
•  Se reducirá el riesgo de crear servicios globales con demasiadas responsabilidades.  
•  La prueba de arquitectura detectará dependencias prohibidas durante el pipeline.  
•  La separación facilitará que distintos integrantes trabajen en capacidades diferentes con menor interferencia.

## **4.2 Consecuencias negativas y dificultades**

•  La solución tendrá más carpetas, contratos y posiblemente más proyectos que una arquitectura tradicional por capas.  
•  El equipo deberá aprender y respetar las reglas de dependencia y propiedad de cada módulo.  
•  Algunos casos de uso atravesarán varios módulos y requerirán coordinación explícita.  
•  El equipo deberá distinguir cuando una comunicación entre módulos requiere una respuesta sincrónica y cuando admite una reacción posterior mediante eventos.  
•  Los reportes que consulten información de varios módulos pueden requerir modelos de lectura específicos.  
•  Una aplicación excesivamente estricta de interfaces, comandos y adaptadores puede introducir complejidad innecesaria; el equipo deberá evitar abstracciones que no respondan a un problema real.  
•  Los límites iniciales de los módulos pueden necesitar ajustes a medida que el equipo comprenda mejor el dominio.

# **5\. Verificación de la decisión**

La adopción de esta decisión se considerará verificable cuando:

1\.  La solución tenga módulos identificables por capacidad de negocio.

2\.  Sea posible ejecutar una prueba unitaria del dominio sin infraestructura.

3\.  Exista una prueba automatizada que falle si Domain depende de Infrastructure o Presentation.

4\.  Ningún módulo acceda directamente a la persistencia interna de otro módulo.

5\.  Al menos un flujo de extremo a extremo, inicialmente la creación de un envío, atraviese Presentation, Application, Domain e Infrastructure respetando las dependencias definidas.

6\.  La prueba de arquitectura se ejecute en el pipeline de integración continua.

# **6\. Compromisos de implementación**

•  Crear un proyecto interno y un proyecto Contracts por cada módulo.  
•  Definir primero los contratos necesarios para los flujos entre Administración y configuración, Envíos y entregas, y Planificación de rutas.  
•  Incorporar el proyecto de pruebas arquitectónicas con xUnit y NetArchTest.Rules desde el esqueleto inicial de la solución.  
•  Ejecutar las pruebas arquitectónicas con dotnet test dentro del pipeline de integración continua.  
•  Revisar los límites de los módulos si la implementación demuestra que una responsabilidad fue asignada al propietario incorrecto; cualquier cambio relevante se documentará mediante una nueva versión o un ADR sucesor.

# **7\. Referencias al enunciado**

•  Sección 6.1, Plataforma y arquitectura.  
•  Sección 6.15, Calidad.  
•  Sección 8.1, Entrega de análisis y diseño.  
•  Sección 8.2, Registros de decisión de arquitectura.  
•  Sección 8.3, cambio obligatorio de requerimientos del 29 de octubre.

