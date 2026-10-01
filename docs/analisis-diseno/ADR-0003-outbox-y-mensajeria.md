# **ADR-0003 · Outbox, mensajería y consistencia entre módulos**

| Estado | Propuesto — pendiente de discusión con el equipo |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Garantía de consistencia entre persistencia y publicación de eventos (Propuesta de stack, sección 8) |
| **Relacionado con** | ADR-0001 y addenda 1 y 3; ADR-0002; Guía de decisiones tecnológicas, secciones 6.2 y 7.2 |

**Este registro propone cómo se publican los eventos de integración sin perderlos ni duplicar sus efectos: una tabla outbox por módulo escrita en la misma transacción que el cambio, un publicador en segundo plano dentro de la API, consumidores idempotentes con tabla inbox, y una implementación propia sobre el cliente oficial de RabbitMQ.**

# **1\. Contexto y restricciones**

## **1.1 El problema**

Cuando un caso de uso modifica datos y además debe avisar a otro módulo o al Worker, las dos operaciones tienen que ocurrir juntas. Por ejemplo, cuando Envíos registra una entrega:

1\.  Se guarda el nuevo estado del envío en PostgreSQL.

2\.  Se publica en RabbitMQ que Depósito y liquidaciones debe incorporar el envío a la liquidación del comercio.

PostgreSQL y RabbitMQ no comparten una transacción. Si se guarda el cambio y la publicación falla, el aviso se pierde y la liquidación queda incompleta. Si se publica el aviso y el guardado falla, se liquida un envío que no se entregó.

El patrón **Outbox** resuelve este problema: el evento se guarda como una fila más, en la misma transacción que el cambio, y un proceso separado lo publica después. Si el cambio se confirma, el evento queda registrado y tarde o temprano se publica; si el cambio se revierte, el evento desaparece con él.

## **1.2 Restricciones**

•  La letra (RNF 6.8) exige el patrón Outbox, idempotencia, reintentos con espera creciente y una cola de mensajes fallidos visible y reprocesable.  
•  El addendum 3 del ADR-0001 define qué va por Outbox: toda reacción cuya pérdida deja datos inconsistentes, y todo trabajo del Worker o con sistemas externos. Los eventos de dominio que pueden perderse sin consecuencias se despachan en memoria.  
•  Cada módulo tiene su propio `DbContext` y es dueño de sus tablas (ADR-0001, Guía sección 5.1).  
•  El Worker se comunica con los demás módulos sólo mediante la cola. Ejecuta el código del módulo Seguimiento (notificaciones y avisos a comercios), que es dueño de sus tablas; de los demás módulos sólo referencia los proyectos `Contracts`.  
•  El Worker resuelve el inquilino a partir de los metadatos del mensaje (ADR-0001, addendum 1, sección 2.3).  
•  Un mismo identificador de correlación debe acompañar una operación desde la API, pasando por RabbitMQ, hasta el Worker (Guía, sección 7.2).  
•  La API se ejecuta en una sola instancia (exención del escalado horizontal).

# **2\. Decisión propuesta**

## **2.1 Una tabla outbox por módulo, en un schema por módulo**

Cada módulo tiene su propia tabla outbox dentro de su `DbContext`. Es la única forma de que el evento y el cambio queden en la misma transacción, porque cada `DbContext` maneja su propia transacción.

Se propone además que **cada módulo use su propio schema de PostgreSQL** (`envios`, `planificacion`, `deposito`, etc.), con sus tablas de negocio y sus tablas `outbox_messages` e `inbox_messages`. Esto hace visible en la base qué tablas pertenecen a cada módulo y evita choques de nombres entre ellos. No cambia la estrategia de multitenancy del ADR-0002: el aislamiento entre inquilinos sigue siendo por fila.

La entidad y la configuración de la tabla se definen una sola vez en `Logistica.BuildingBlocks.Infrastructure`, y `ModuleDbContext` las registra en cada módulo (addendum 1).

## **2.2 Los eventos de integración se escriben de forma explícita**

•  Los **eventos de dominio** siguen despachándose en memoria (addendum 3).  
•  Un handler de la capa Application decide cuándo un hecho del dominio se convierte en un **evento de integración**, y lo registra mediante una interfaz `IIntegrationEventPublisher`.  
•  La implementación de esa interfaz agrega una fila a la tabla outbox del `DbContext` del módulo; la fila se confirma con el mismo `SaveChanges` que el cambio.  
•  Los eventos de integración se definen en la carpeta `Events/` del proyecto `Contracts` del módulo que los publica (addendum 2, sección 2.6).

Así queda explícito en el código qué eventos son durables y cuáles no.

## **2.3 Publicador en segundo plano dentro de la API**

Un `BackgroundService` en el proceso de la API lee la tabla outbox de cada módulo y publica los mensajes pendientes en RabbitMQ:

•  Consulta cada 1 o 2 segundos un lote de filas no publicadas, ordenadas por fecha, con `SELECT ... FOR UPDATE SKIP LOCKED`.  
•  Publica cada mensaje con confirmación del broker (*publisher confirms*) y recién entonces lo marca como publicado.  
•  Si la publicación falla, la fila queda pendiente y se reintenta en la siguiente pasada.

El publicador no se ubica en el Worker porque el Worker no tiene acceso a los `DbContext` de los módulos, y así debe seguir. Como la API corre en una sola instancia no hay competencia entre publicadores; `SKIP LOCKED` deja preparado el caso de varias instancias.

## **2.4 Consumidores idempotentes, reintentos y cola de fallidos**

RabbitMQ garantiza entrega **al menos una vez**: un mensaje puede llegar más de una vez. Por eso:

•  **Tabla inbox por consumidor.** Antes de procesar, el consumidor registra el `MessageId` en su tabla `inbox_messages`, en la misma transacción que el efecto. Si el `MessageId` ya existe, el mensaje se descarta sin repetir el efecto.  
•  **Reintentos con espera creciente.** Ante un error transitorio el mensaje se reintenta con demoras crecientes (por ejemplo 5 s, 30 s y 2 min).  
•  **Cola de fallidos.** Agotados los reintentos, el mensaje pasa a la dead-letter queue de su cola, visible en la consola de RabbitMQ y reprocesable.

## **2.5 Implementación propia sobre RabbitMQ.Client**

Se propone implementar el Outbox, el publicador, la inbox y la topología en `Logistica.BuildingBlocks.Infrastructure`, usando el cliente oficial **RabbitMQ.Client**. Esto cierra el pendiente "biblioteca cliente de RabbitMQ" de la Propuesta de stack y de la Guía.

## **2.6 Formato y topología de los mensajes**

**Sobre del mensaje (JSON):**

| Campo | Uso |
| :---- | :---- |
| `MessageId` | Identificador único; base de la idempotencia |
| `Type` | Nombre del evento de integración; también es la routing key |
| `OccurredAt` | Momento en que ocurrió el hecho |
| `OperadorId` | Inquilino; el consumidor lo usa para resolver `ICurrentTenant` |
| `ComercioId` | Segundo nivel de aislamiento, cuando corresponde |
| `traceparent` | Contexto de traza de OpenTelemetry, para correlacionar API, RabbitMQ y Worker |
| `Payload` | Datos del evento |

**Topología:**

•  Un exchange de tipo `topic`, `logistica.events`.  
•  La routing key es el tipo de evento.  
•  Una cola durable por consumidor, enlazada a los tipos de evento que le interesan, cada una con su dead-letter queue.

**Retención:** las filas del outbox ya publicadas y las de la inbox se eliminan a los 7 días.

# **3\. Alternativas consideradas**

## **3.1 Una única tabla outbox compartida**

**Motivo de descarte:** los módulos tienen `DbContext` separados; una tabla compartida obligaría a coordinar transacciones entre contextos o a que un módulo escriba en una tabla que no le pertenece.

## **3.2 Publicar directamente en RabbitMQ después de SaveChanges**

**Motivo de descarte:** es exactamente el problema de la sección 1.1: si la publicación falla después de confirmar el cambio, el evento se pierde.

## **3.3 Escribir en el outbox automáticamente todos los eventos de dominio**

Un interceptor convertiría cada evento de dominio en una fila del outbox.

**Motivo de descarte:** haría durables todos los eventos, contra el criterio del addendum 3, y ocultaría qué eventos cruzan los límites del módulo.

## **3.4 Publicador en el Worker**

**Motivo de descarte:** el Worker necesitaría acceso a las tablas de todos los módulos, lo que rompe el aislamiento del ADR-0001 y la regla de que el Worker sólo se comunica mediante la cola.

## **3.5 MassTransit**

Ofrece Outbox transaccional para EF Core, reintentos y dead-letter listos para usar.

**Motivo de descarte:** a partir de la versión 9 pasó a licencia comercial, y la versión 8 deja de evolucionar. Además es un componente grande que el equipo debería poder explicar en la defensa oral.

## **3.6 Wolverine**

Es de licencia MIT y muy completo, con Outbox para EF Core y transporte RabbitMQ.

**Motivo de descarte:** trae su propio modelo de mensajería y de handlers, con una curva de aprendizaje alta para el plazo del laboratorio. Por el mismo criterio con el que el ADR-0002 prefirió una implementación propia a Finbuckle, se prefiere una implementación acotada y explicable.

# **4\. Consecuencias**

## **4.1 Positivas**

•  Ningún evento de integración se pierde si el cambio que lo origina se confirmó.  
•  Los efectos no se duplican aunque un mensaje llegue más de una vez.  
•  Los mensajes que fallan quedan visibles y pueden reprocesarse.  
•  El inquilino y la traza viajan con cada mensaje.  
•  Todo el mecanismo es código propio, acotado y explicable.

## **4.2 Negativas y riesgos**

•  Consistencia eventual: entre el cambio y la reacción pasan algunos segundos.  
•  El equipo debe implementar y probar el publicador, la inbox y los reintentos; es código de infraestructura sensible.  
•  Todo consumidor debe ser idempotente; un consumidor que no registre la inbox en la misma transacción que su efecto puede duplicar efectos.  
•  El publicador vive en la API: si la API está caída, los eventos se acumulan en el outbox hasta que vuelva. No se pierden.  
•  El schema por módulo agrega una convención que deben respetar todas las migraciones.

# **5\. Verificación prevista**

La decisión se considerará verificada cuando existan pruebas automatizadas que demuestren que:

1\.  Si el `SaveChanges` falla, no queda ninguna fila en el outbox.

2\.  Si RabbitMQ no está disponible, las filas quedan pendientes y se publican cuando vuelve.

3\.  Un mismo mensaje entregado dos veces produce el efecto una sola vez.

4\.  Un mensaje que falla siempre termina en la dead-letter queue después de los reintentos.

5\.  El `OperadorId` del mensaje determina el inquilino con que se ejecuta el consumidor.

# **6\. Preguntas abiertas para el equipo**

| Tema | Por qué importa |
| :---- | :---- |
| Schema de PostgreSQL por módulo | Afecta cómo se configuran los `DbContext` y las migraciones del hito del 15/10 (Cristian). Si no se adopta, las tablas de cada módulo necesitan un prefijo propio para evitar choques de nombres. |
| Intervalos de reintento y retención | Los valores propuestos (5 s, 30 s y 2 min; 7 días) son iniciales y pueden ajustarse al medir. |
| Implementación propia frente a Wolverine | Si el equipo prefiere no mantener el código de mensajería, Wolverine es la alternativa libre más completa. |

# **7\. Plan de implementación**

La implementación corresponde al hito del 29 de octubre (RabbitMQ, Worker, reintentos y webhooks). Antes, en el hito del 15 de octubre, conviene que el `ModuleDbContext` de `BuildingBlocks.Infrastructure` ya incluya la tabla outbox y el schema por módulo, para no tener que migrar después las tablas de todos los módulos.

# **8\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Propuesta inicial. | Ezequiel Marcenal |
