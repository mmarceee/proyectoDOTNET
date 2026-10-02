# **ADR-0001 · Addendum 3 · Comunicación entre módulos y biblioteca de pruebas de arquitectura**

| Estado | Aceptado |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Responsable del ADR-0001** | Lucas Ottonello |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Modifica** | ADR-0001, sección 2.4 (Regla de dependencias) y sección 6 (Compromisos de implementación) |
| **Relacionado con** | Guía de decisiones tecnológicas, secciones 2.4 y 8; Propuesta de stack, sección 2 |

**Este addendum precisa cuándo se usa cada mecanismo de comunicación entre módulos —contratos síncronos, eventos en memoria u Outbox con RabbitMQ— y reemplaza NetArchTest.Rules por ArchUnitNET como biblioteca de pruebas de arquitectura.**

# **1\. Contexto**

## **1.1 Comunicación entre módulos**

El ADR-0001, sección 2.4, establece que las reacciones posteriores entre módulos se resuelven con eventos internos y que la cola durable se reserva para el worker y para trabajos que requieran persistencia o reintentos. La Guía de decisiones tecnológicas, en cambio, indicaba que las reacciones posteriores entre módulos se resuelven con eventos por RabbitMQ.

Ninguna de las dos redacciones responde con precisión cuándo un evento puede despacharse en memoria y cuándo debe ser durable. Usar RabbitMQ para toda interacción interna agregaría serialización, reintentos, mensajes duplicados y consistencia eventual incluso entre módulos que se ejecutan en el mismo proceso. Pero despachar en memoria una reacción imprescindible tiene el riesgo opuesto: si el módulo que reacciona falla, la reacción se pierde y los datos quedan inconsistentes.

La documentación de Microsoft sobre arquitectura de microservicios y DDD en .NET hace la misma distinción: los eventos de dominio son normalmente internos al proceso, y los eventos de integración, asíncronos, se propagan a otros subsistemas. También advierte que usar mensajería real para eventos internos puede ser excesivo.

## **1.2 Biblioteca de pruebas de arquitectura**

El ADR-0001 eligió NetArchTest.Rules. Al revisar el estado de ambas bibliotecas en NuGet el 30 de septiembre de 2026:

| Biblioteca | Última versión | Fecha de publicación |
| :---- | :---- | :---- |
| NetArchTest.Rules | 1.3.2 | 23 de mayo de 2021 |
| TngTech.ArchUnitNET y TngTech.ArchUnitNET.xUnit | 0.13.4 | 20 de agosto de 2026 |

NetArchTest.Rules no publica versiones desde hace más de cinco años. ArchUnitNET se mantiene activamente y ofrece reglas más expresivas sobre tipos, namespaces, ensamblados y miembros.

Las pruebas de arquitectura estaban implementadas con NetArchTest.Rules desde el esqueleto de la solución, pero los módulos todavía no tienen código, por lo que el costo del cambio se limita a reescribir dos archivos de pruebas.

# **2\. Decisión**

## **2.1 Comunicación entre módulos (reemplaza las viñetas correspondientes de la sección 2.4 del ADR-0001)**

Los eventos de dominio internos se despacharán en memoria. La comunicación síncrona entre módulos se realizará mediante contratos públicos. Los eventos de integración que requieran procesamiento durable, ejecución en el Worker o comunicación con sistemas externos se registrarán mediante el patrón Outbox y se publicarán en RabbitMQ. RabbitMQ no se utilizará para todas las interacciones internas del monolito.

Para decidir entre un evento en memoria y uno durable se aplica esta regla:

**Si perder la reacción deja datos inconsistentes, la reacción se publica mediante Outbox. Si la reacción puede perderse sin consecuencias, puede despacharse en memoria.**

| Necesidad | Mecanismo | Ejemplo |
| :---- | :---- | :---- |
| Consulta o respuesta inmediata | Contrato síncrono en memoria, publicado en `Contracts` | Planificación consulta si un envío puede asignarse a una ruta. |
| Reacción que puede perderse sin dejar datos inconsistentes | Evento de dominio en memoria | Al cambiar el estado de un envío se invalida la caché del seguimiento público; si falla, la caché expira sola. |
| Reacción que no puede perderse | Outbox y RabbitMQ | Cuando Envíos registra una entrega, Depósito y liquidaciones incorpora el envío a la liquidación del comercio. |
| Trabajo en el Worker o con sistemas externos | Outbox, RabbitMQ y Worker | Notificaciones al destinatario y webhooks a los comercios. |

**Transacción única en las llamadas síncronas.** Cuando un caso de uso invoca a otro módulo mediante su contrato síncrono y ambos modifican datos, los dos cambios se guardan en una sola transacción: los `DbContext` de los módulos comparten la conexión a la misma base PostgreSQL. Si cualquiera de los dos falla, se revierten ambos.

Se mantienen sin cambios las demás reglas de la sección 2.4: no se usan llamadas HTTP entre módulos, y un módulo sólo puede usar el proyecto `Contracts` de otro.

## **2.2 Biblioteca de pruebas de arquitectura (reemplaza la mención a NetArchTest.Rules en las secciones 2.4 y 6 del ADR-0001)**

Las reglas de dependencias se verificarán mediante un proyecto de pruebas con **xUnit y ArchUnitNET** (`TngTech.ArchUnitNET.xUnit`), ejecutado con `dotnet test` en el pipeline de integración continua.

El aislamiento entre módulos se verifica con una regla por módulo: los tipos de `Logistica.Modules.<Modulo>` no pueden depender de los namespaces `Domain`, `Application`, `Infrastructure` ni `Presentation` de ningún otro módulo. Los proyectos `Contracts` no se cargan en la arquitectura analizada, de modo que usar el `Contracts` de otro módulo está permitido y usar cualquier otra parte de ese módulo hace fallar la prueba.

Se evaluó expresar esta regla con las reglas de *slices* de ArchUnitNET (`Slices().Matching("Logistica.Modules.(*)..")`), pero en la versión 0.13.4 el patrón agrupa cada sub-namespace como un slice distinto: una página de `Presentation` que usa un handler de `Application` del mismo módulo se reportaba como dependencia entre módulos. Se detectó al verificar el Backoffice (addendum 4) y se reemplazó por la regla explícita por módulo.

# **3\. Alternativas consideradas**

## **3.1 RabbitMQ para toda la comunicación entre módulos**

**Motivo de descarte:** agrega serialización, reintentos, duplicados y consistencia eventual a interacciones que ocurren dentro del mismo proceso, sin beneficio para el alcance del laboratorio.

## **3.2 Todos los eventos entre módulos en memoria**

**Motivo de descarte:** una reacción imprescindible, como la incorporación de un envío entregado a la liquidación, se perdería si el módulo que reacciona falla después de que el primer módulo confirmó su transacción.

## **3.3 Mantener NetArchTest.Rules**

**Motivo de descarte:** cubre las reglas básicas, pero no publica versiones desde 2021. Siendo bajo el costo del cambio en este momento, se prefiere una biblioteca mantenida activamente.

# **4\. Consecuencias**

## **4.1 Positivas**

•  Cada interacción entre módulos tiene un criterio explícito para elegir su mecanismo.  
•  Las reacciones imprescindibles no se pierden ante fallos.  
•  RabbitMQ se usa donde aporta durabilidad, no por defecto.  
•  La biblioteca de pruebas de arquitectura está mantenida activamente.

## **4.2 Negativas y riesgos**

•  Las reacciones por Outbox son eventualmente consistentes: entre el cambio original y la reacción puede pasar un tiempo, y el consumidor debe ser idempotente.  
•  Clasificar una reacción como descartable cuando no lo es produce inconsistencias difíciles de detectar. Ante la duda, se usa Outbox.  
•  ArchUnitNET todavía está en versiones 0.x y su API puede cambiar entre versiones. Se mitiga fijando la versión en `Directory.Packages.props`.

# **5\. Verificación**

1\.  La Guía de decisiones tecnológicas (sección 2.4) y la Propuesta de stack (sección 2) reflejan este addendum.

2\.  Las pruebas de arquitectura usan ArchUnitNET (`tests/Logistica.ArchitectureTests`).

3\.  Existe una prueba que falla si un módulo depende de algo distinto del `Contracts` de otro módulo, y que no falla si usa ese `Contracts`.

Los puntos 1 a 3 ya están implementados. Se verificó con pruebas de mutación que las reglas detectan un Domain que usa ASP.NET Core y un módulo que usa el Domain de otro, y que no marcan como error el uso del `Contracts` de otro módulo.

# **6\. Referencias**

•  Microsoft Learn. *Domain events: design and implementation* (.NET Microservices: Architecture for Containerized .NET Applications).  
•  Repositorio y paquetes NuGet de ArchUnitNET (TNG Technology Consulting).  
•  Repositorio y paquete NuGet de NetArchTest.Rules.

# **7\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Propuesta inicial del addendum. | Ezequiel Marcenal |
| 0.2 | 30/09/2026 | La regla de aislamiento entre módulos pasa de slices a una regla explícita por módulo (sección 2.2). | Ezequiel Marcenal |
| 0.3 | 30/09/2026 | Se agrega la transacción única en las llamadas síncronas entre módulos (sección 2.1). | Ezequiel Marcenal |
| 0.4 | 02/10/2026 | Revisado y aceptado por el responsable del ADR-0001. | Lucas Ottonello |
