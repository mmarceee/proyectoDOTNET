# **ADR-0003 · Outbox, mensajería y consistencia entre módulos**

| Estado | Aceptado — aprobado por el equipo el 12/10/2026. Implementados: la tabla outbox (secciones 2.1 y 2.2) y, desde el 16/10, el publicador, la topología, la inbox de Seguimiento, los reintentos, la cola de fallidos y las trazas. El registrador de fallidos, la retención y las métricas se implementan para el 29/10 (sección 7). |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 (actualizado el 16 de octubre de 2026) |
| **Autor** | Ezequiel Marcenal |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Garantía de consistencia entre persistencia y publicación de eventos (Propuesta de stack, sección 8) |
| **Relacionado con** | ADR-0001 y addenda 1 a 3; ADR-0002 (secciones 2.4, 2.5 y 4); CU-09; Guía de decisiones tecnológicas, secciones 6.2 y 7.2 |

**Este registro decide cómo se publican los eventos de integración sin perderlos ni duplicar sus efectos: una tabla outbox por módulo escrita en la misma transacción que el cambio, un publicador en segundo plano dentro de la API, consumidores idempotentes con tabla inbox, reintentos con espera creciente, una cola de fallidos que el administrador consulta y reprocesa desde el Backoffice, y una implementación propia sobre el cliente oficial de RabbitMQ.**

# **1\. Contexto y restricciones**

## **1.1 El problema**

Cuando un caso de uso modifica datos y además debe avisar a otro módulo o al Worker, las dos operaciones tienen que ocurrir juntas. Por ejemplo, cuando Envíos registra una entrega:

1\.  Se guarda el nuevo estado del envío en PostgreSQL.

2\.  Se publica en RabbitMQ que el envío entregado debe incorporarse a la liquidación del comercio.

PostgreSQL y RabbitMQ no comparten una transacción. Si se guarda el cambio y la publicación falla, el aviso se pierde y la liquidación queda incompleta. Si se publica el aviso y el guardado falla, se liquida un envío que no se entregó.

El patrón **Outbox** resuelve este problema: el evento se guarda como una fila más, en la misma transacción que el cambio, y un proceso separado lo publica después. Si el cambio se confirma, el evento queda registrado y tarde o temprano se publica; si el cambio se revierte, el evento desaparece con él.

## **1.2 Restricciones**

•  La letra (RNF 6.8) exige el patrón Outbox ("que no exista posibilidad de que un cambio se persista sin que su evento se publique, ni a la inversa"), idempotencia en el consumidor, reintentos con espera creciente, cola de mensajes fallidos y **visibilidad operativa: el administrador debe poder consultar los mensajes fallidos y reintentarlos**.  
•  El addendum 3 del ADR-0001 define qué va por Outbox: toda reacción cuya pérdida deja datos inconsistentes, y todo trabajo del Worker o con sistemas externos. Los eventos de dominio que pueden perderse sin consecuencias se despachan en memoria.  
•  Cada módulo tiene su propio `DbContext` y es dueño de sus tablas (ADR-0001, Guía sección 5.1). Cuando un caso de uso escribe en dos módulos, los dos contextos comparten la transacción de la unidad de trabajo (ADR-0001, addendum 3).  
•  El Worker se comunica con los demás módulos sólo mediante la cola. Ejecuta el código del módulo Seguimiento (notificaciones y avisos a comercios), que es dueño de sus tablas; de los demás módulos sólo referencia los proyectos `Contracts`.  
•  El aislamiento entre inquilinos es por fila, con el filtro global "Tenant" y el interceptor de escrituras (ADR-0002). El consumidor de un mensaje resuelve el inquilino a partir de los metadatos del mensaje, nunca de su contenido (ADR-0002, sección 2.5).  
•  Un mismo identificador de correlación debe acompañar una operación desde la API, pasando por RabbitMQ, hasta el Worker (Guía, sección 7.2).  
•  La API se ejecuta en una sola instancia (exención del escalado horizontal).

# **2\. Decisión**

## **2.1 Una tabla outbox por módulo, en un schema por módulo**

Cada módulo tiene su propia tabla `outbox_messages` dentro de su `DbContext`. Es la única forma de que el evento y el cambio queden en la misma transacción, porque cada `DbContext` maneja su propia transacción. Cuando un caso de uso escribe en dos módulos, el evento se registra en el contexto del módulo que hace el cambio, y la unidad de trabajo confirma o revierte las filas de los dos módulos juntas.

Cada módulo usa su propio schema de PostgreSQL (`envios`, `planificacion`, `deposito`, etc.), con sus tablas de negocio y sus tablas técnicas `outbox_messages` e `inbox_messages`. El aislamiento entre inquilinos sigue siendo por fila (ADR-0002).

La entidad `OutboxMessage` y su configuración se definen una sola vez en `Logistica.BuildingBlocks.Infrastructure`, y `ModuleDbContext` las registra en todos los módulos (addendum 1). Un módulo no configura su outbox: lo recibe al heredar de `ModuleDbContext`.

| Columna | Uso |
| :---- | :---- |
| `Id` | `MessageId` del sobre: identificador único (Guid v7) y base de la idempotencia |
| `Tipo` | Nombre versionado del evento (por ejemplo, `EnvioAsignadoARuta.v1`); también es la routing key |
| `OcurridoEn` | Momento en que ocurrió el hecho |
| `OperadorId` | Inquilino del sobre (sección 2.2) |
| `ComercioId` | Segundo nivel del inquilino, cuando la sesión lo trae |
| `TraceParent` | Contexto de traza de OpenTelemetry del request que generó el evento |
| `Contenido` | Datos del evento, en JSON |
| `PublicadoEn` | `null` mientras está pendiente; lo completa el publicador |

Un **índice parcial** sobre `OcurridoEn`, sólo para las filas con `PublicadoEn IS NULL`, sostiene la consulta del publicador sin crecer con los mensajes ya publicados.

## **2.2 El outbox y el inquilino**

El outbox **no implementa `IOperadorOwned`** y no tiene el filtro "Tenant" (ADR-0002, sección 2.4). El publicador corre en segundo plano, sin inquilino: con el filtro, que falla cerrado, no vería ningún mensaje, y para leerlos necesitaría `IgnoreQueryFilters`, que el ADR-0002 prohíbe fuera de su lista cerrada.

El inquilino viaja igual con cada mensaje, como **metadato del sobre**:

•  La única forma de escribir en el outbox es `ModuleDbContext.AgregarAlOutbox(tipo, contenido, ocurridoEn)`. El constructor de `OutboxMessage` es interno a BuildingBlocks.  
•  `AgregarAlOutbox` toma `OperadorId` y `ComercioId` del `ICurrentTenant` de la sesión, **nunca del llamador ni del contenido del evento** (ADR-0002, sección 2.5). Sin operador en la sesión, lanza `TenantMismatchException`: falla cerrado, igual que el filtro y el interceptor.  
•  También toma el `TraceParent` de la traza actual.

Así el outbox queda legible para el publicador y, a la vez, nadie puede registrar un evento a nombre de otro inquilino.

## **2.3 Los eventos de integración se escriben de forma explícita**

•  Los **eventos de dominio** siguen despachándose en memoria (addendum 3).  
•  Un handler de la capa Application decide cuándo un hecho del dominio se convierte en un **evento de integración**, y lo registra mediante la interfaz `IIntegrationEventPublisher` de su módulo.  
•  **Cada módulo declara su propia `IIntegrationEventPublisher`**, `internal`, en su capa Application. La implementa su Infrastructure con una sola llamada a `AgregarAlOutbox` sobre el `DbContext` del módulo. Una interfaz única en SharedKernel no sirve: el contenedor de DI tendría una implementación por módulo para la misma interfaz y no sabría cuál dar, y cada evento tiene que ir al outbox del módulo que hace el cambio.  
•  La fila se confirma con el mismo `SaveChanges` que el cambio, o con la transacción de la unidad de trabajo si el caso de uso escribe en varios módulos.  
•  Los eventos de integración se definen como `record` en la carpeta `Events/` del proyecto `Contracts` del módulo que los publica (addendum 2, sección 2.6), con su `Tipo` versionado como constante. El contenido lleva sólo los datos del hecho: el `MessageId`, el inquilino, la fecha y la traza ya están en el sobre.  
•  **Versionado:** agregar un campo opcional no cambia la versión, y los consumidores ignoran los campos que no conocen. Un cambio incompatible es un evento nuevo (`.v2`), y durante la transición se publican las dos versiones.

Así queda explícito en el código qué eventos son durables y cuáles no.

## **2.4 Publicador en segundo plano dentro de la API**

Un `BackgroundService` en el proceso de la API publica los mensajes pendientes de todos los módulos en RabbitMQ:

•  Cada 1 o 2 segundos abre un ámbito de DI nuevo y recorre los `ModuleDbContext` registrados.  
•  Para cada módulo, dentro de una transacción, toma un lote de hasta 50 filas pendientes en orden de llegada con `SELECT ... WHERE "PublicadoEn" IS NULL ORDER BY "OcurridoEn" LIMIT 50 FOR UPDATE SKIP LOCKED`. Es SQL fijo, sin datos del usuario, sobre una tabla sin filtro de inquilino; es el caso revisado de SQL crudo que admite el ADR-0002 (sección 4).  
•  Publica cada mensaje con confirmación del broker (*publisher confirms*) y recién entonces le asigna `PublicadoEn`. Al terminar el lote, confirma la transacción.  
•  Si la publicación falla, la fila queda pendiente y se reintenta en la siguiente pasada. Si RabbitMQ no está disponible, los eventos se acumulan en el outbox y se publican cuando vuelve.  
•  El ámbito del publicador usa un inquilino vacío (`InquilinoMensaje.SinInquilino()`, sección 2.5): sólo toca el outbox, y si por error intentara escribir una entidad de un operador, el interceptor la rechazaría.  
•  **Sólo publica los tipos que tienen un consumidor declarado** (una `SuscripcionIntegracion` registrada). Publica con `mandatory`: un mensaje que ninguna cola recibe vuelve como no enrutable y haría fallar el lote. Los eventos sin consumidor quedan pendientes en el outbox y se publican cuando su consumidor existe. Por eso la API registra también las suscripciones del Worker; hoy lo hace porque las dos llaman a `AddSeguimientoModule`.

El publicador no se ubica en el Worker porque el Worker no tiene acceso a los `DbContext` de los módulos, y así debe seguir. Como la API corre en una sola instancia no hay competencia entre publicadores; `SKIP LOCKED` deja preparado el caso de varias instancias.

**Semántica de entrega:** *al menos una vez*. Si la API se cae después de publicar y antes de confirmar la transacción, el mensaje se vuelve a publicar en la siguiente pasada. La inbox (sección 2.5) hace que eso no duplique efectos.

**Orden:** dentro de un módulo, los mensajes se publican en orden de `OcurridoEn`, pero no hay orden garantizado entre módulos, y un reintento puede entregar un mensaje después de otro posterior. Los consumidores no suponen orden: verifican el estado actual antes de actuar (por ejemplo, la máquina de estados del envío rechaza una transición que ya no corresponde).

## **2.5 Consumidores idempotentes**

RabbitMQ garantiza entrega **al menos una vez**: un mensaje puede llegar más de una vez. Cada consumidor:

1\.  Abre un ámbito de DI por mensaje y fija el inquilino con los metadatos del sobre (`InquilinoMensaje.Fijar` con `OperadorId` y `ComercioId`), antes de resolver cualquier `DbContext` (ADR-0002, sección 2.5). `InquilinoMensaje` se registra con alcance de ámbito y se fija una sola vez, porque el inquilino se conoce recién después de crear el ámbito; en la API, `ICurrentTenant` es `InquilinoMensaje` cuando está fijado y, si no, la sesión.

2\.  Si el sobre no trae inquilino, o el contenido no se puede leer, el mensaje es inválido: va directo a la cola de fallidos (sección 2.6), sin reintentos y sin procesarse jamás sin filtro.

3\.  Registra el `MessageId` en la tabla `inbox_messages` de su módulo **en la misma transacción que el efecto**. La clave es (`MessageId`, `Consumidor`), porque un mismo mensaje puede interesarle a varios consumidores del módulo. Si la fila ya existe, el mensaje ya se procesó: se confirma (*ack*) sin repetir el efecto.

4\.  Confirma el mensaje (*ack* manual) recién después de confirmar la transacción.

Como el outbox, `inbox_messages` es una tabla técnica: no implementa `IOperadorOwned` ni guarda datos del operador.

El consumidor pide los mensajes a cada cola (`BasicGet`) en lugar de suscribirse (`BasicConsume`), en un solo canal: así nunca hay publicaciones ni confirmaciones simultáneas en el mismo canal. Recorre las colas tomando un mensaje de cada una y espera 250 ms sólo cuando todas estaban vacías, para que la espera no limite cuántos mensajes por segundo procesa.

## **2.6 Reintentos con espera creciente y cola de fallidos**

**Reintentos.** Ante un error transitorio, el consumidor reintenta el mensaje con demoras crecientes: **5 s, 30 s y 2 min**. Se implementa con colas de espera, sin plugins de RabbitMQ:

•  Cada cola de consumidor tiene tres colas de espera (`<cola>.espera.5s`, `.30s` y `.2m`) con `x-message-ttl` y, como *dead-letter*, la cola original. Al vencer la espera, el mensaje vuelve solo a su cola.  
•  Si el procesamiento falla, el consumidor publica una copia en la cola de espera que corresponde, con el número de intento en una cabecera, espera la confirmación del broker y recién entonces confirma el original. Así el mensaje nunca está en dos lugares ni en ninguno.

**Cola de fallidos.** Agotados los tres reintentos, o si el mensaje es inválido, el consumidor publica una copia en la cola **`logistica.fallidos`**, con cabeceras con la cola de origen, la cantidad de intentos y el último error, y confirma el original. Es la cola de fallidos de la letra, visible en la consola de RabbitMQ.

**Visibilidad operativa (CU-09).** La letra exige que el administrador consulte y reintente los fallidos desde el sistema, no desde la consola de RabbitMQ:

•  Un **registrador de fallidos**, un consumidor en segundo plano en la API, vacía `logistica.fallidos` y guarda cada mensaje en la tabla `mensajes_fallidos` del schema `mensajeria`. Es un `DbContext` técnico de BuildingBlocks, que hereda de `ModuleDbContext`. Guarda el sobre completo, la cola de origen, los intentos, el historial de errores y el estado (*Pendiente*, *Reintentado* o *Descartado*).  
•  `mensajes_fallidos` **sí implementa `IOperadorOwned`**: es información del operador, y con el filtro "Tenant" cada administrador ve sólo los fallidos de su operador (CU-09, requerimiento especial 1). El registrador fija el inquilino del sobre antes de guardar, igual que cualquier consumidor. La clave (`MessageId`, `ColaOrigen`) evita duplicados si el registrador recibe dos veces el mismo mensaje.  
•  Un mensaje fallido sin inquilino no pertenece a ningún operador: el registrador lo deja en `logistica.fallidos.sin-inquilino`, visible sólo en la consola de RabbitMQ.  
•  **Reintentar** (CU-09) vuelve a publicar el sobre guardado en su cola de origen, **con el mismo `MessageId`**, y marca la fila *Reintentada*. Si el consumidor ya lo había procesado, la inbox evita repetir el efecto. Si vuelve a fallar, recorre otra vez los reintentos y la fila vuelve a *Pendiente* con el error nuevo en su historial.  
•  **Descartar** marca la fila *Descartada*, con quién lo descartó, cuándo y por qué.  
•  CU-09 accede a esta tabla mediante una interfaz técnica en SharedKernel (como `IUnidadDeTrabajo`), implementada en BuildingBlocks.

## **2.7 Implementación propia sobre RabbitMQ.Client**

El outbox, el publicador, la inbox, los reintentos, el registrador de fallidos y la topología se implementan en `Logistica.BuildingBlocks.Infrastructure`, usando el cliente oficial **RabbitMQ.Client**. Esto cierra el pendiente "biblioteca cliente de RabbitMQ" de la Propuesta de stack y de la Guía.

## **2.8 Formato del mensaje y topología**

**El mensaje en RabbitMQ.** El sobre viaja en las propiedades estándar de AMQP y en cabeceras; el cuerpo es el contenido del evento:

| Columna del outbox | En RabbitMQ |
| :---- | :---- |
| `Id` | Propiedad `message-id` |
| `Tipo` | Propiedad `type` y routing key |
| `OcurridoEn` | Propiedad `timestamp` (en segundos: el consumidor no ve los milisegundos) |
| `OperadorId`, `ComercioId` | Cabeceras `operador-id` y `comercio-id` |
| `TraceParent` | Cabecera `traceparent` (W3C Trace Context) |
| `Contenido` | Cuerpo, en JSON (`content-type: application/json`) |

Todos los mensajes son persistentes (`delivery-mode` 2). El consumidor lee el inquilino y la traza de las cabeceras antes de deserializar el cuerpo.

**Topología:**

•  Un exchange `topic` durable, `logistica.events`. La routing key es el tipo de evento.  
•  Una cola durable por consumidor, con el nombre `<modulo>.<consumidor>` (por ejemplo, `seguimiento.notificar-destinatario`), enlazada a los tipos de evento que le interesan.  
•  Las tres colas de espera de cada cola de consumidor (sección 2.6).  
•  Las colas `logistica.fallidos` y `logistica.fallidos.sin-inquilino`.  
•  La topología la declara BuildingBlocks al iniciar la API y el Worker; declararla es idempotente.

**Retención:** las filas del outbox ya publicadas y las de la inbox se eliminan a los 7 días; lo hace el publicador, una vez por hora. Los fallidos cerrados (descartados o reintentados) se conservan 30 días, como registro de auditoría; los pendientes no se eliminan.

## **2.9 Observabilidad**

•  El `traceparent` del sobre continúa la traza del request en el publicador y en el consumidor, así una operación se sigue de la API a RabbitMQ y al Worker en Aspire Dashboard. La fuente de trazas `Logistica.Mensajeria` (BuildingBlocks), que registran la API y el Worker, genera un span *Publicar \<tipo\>* (productor), hijo del request que guardó el evento, y un span *Procesar \<cola\>* (consumidor), hijo de la publicación: la cabecera `traceparent` lleva el id del span de publicación. Un mensaje que falla marca su span con error, el intento y la cola de destino.  
•  Métricas de OpenTelemetry: mensajes pendientes en el outbox por módulo, antigüedad del pendiente más viejo, mensajes publicados, reintentos y fallidos.  
•  Cada reintento y cada mensaje fallido se registra en el log con su `MessageId`, tipo, cola, inquilino e intento.

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

## **3.5 Outbox con el filtro de inquilino**

Fue la primera implementación del outbox en CU-40: `OutboxMessage` implementaba `IOperadorOwned`.

**Motivo de descarte:** el publicador, sin inquilino, no vería ningún mensaje, y leerlos exigiría `IgnoreQueryFilters`, prohibido por el ADR-0002. El aislamiento que aportaba el filtro lo da `AgregarAlOutbox`, que toma el inquilino de la sesión (sección 2.2).

## **3.6 Recibir el inquilino del mensaje por parámetro**

**Motivo de descarte:** cualquier llamador podría registrar un evento a nombre de otro operador, y el consumidor ejecutaría su efecto con ese inquilino. El ADR-0002 (sección 2.5) exige que salga del `ICurrentTenant` del request original.

## **3.7 Una única `IIntegrationEventPublisher` en SharedKernel**

**Motivo de descarte:** cada módulo necesita una implementación sobre su propio `DbContext`, y el contenedor de DI no puede elegir entre varias implementaciones de la misma interfaz sin claves ni convenciones implícitas. Una interfaz `internal` por módulo es explícita y no necesita nada más (sección 2.3).

## **3.8 Mensajes fallidos sólo en la consola de RabbitMQ**

Era la propuesta de la versión 0.1.

**Motivo de descarte:** la letra (RNF 6.8) exige que el **administrador** consulte y reintente los fallidos, y CU-09 lo pide desde el Backoffice, sólo con los de su operador.

## **3.9 Consultar la cola de fallidos con la API de administración de RabbitMQ**

**Motivo de descarte:** una cola no se consulta: para listar hay que sacar los mensajes y volver a encolarlos, no se puede filtrar por operador sin leerlos todos, y reintentar uno en particular obliga a recorrer la cola. La tabla `mensajes_fallidos` da consultas, filtro por inquilino e historial.

## **3.10 Reintentos con `requeue` inmediato o con el plugin de mensajes demorados**

**Motivo de descarte:** el `requeue` inmediato no da espera creciente y puede dejar un mensaje girando sin pausa. El plugin `rabbitmq_delayed_message_exchange` agrega una dependencia del broker que no todos los planes administrados ofrecen. Las colas de espera con TTL usan sólo funciones estándar de RabbitMQ.

## **3.11 MassTransit**

Ofrece Outbox transaccional para EF Core, reintentos y dead-letter listos para usar.

**Motivo de descarte:** a partir de la versión 9 pasó a licencia comercial, y la versión 8 deja de evolucionar. Además es un componente grande que el equipo debería poder explicar en la defensa oral.

## **3.12 Wolverine**

Es de licencia MIT y muy completo, con Outbox para EF Core y transporte RabbitMQ.

**Motivo de descarte:** trae su propio modelo de mensajería y de handlers, con una curva de aprendizaje alta para el plazo del laboratorio. Por el mismo criterio con el que el ADR-0002 prefirió una implementación propia a Finbuckle, se prefiere una implementación acotada y explicable.

# **4\. Consecuencias**

## **4.1 Positivas**

•  Ningún evento de integración se pierde si el cambio que lo origina se confirmó, y ninguno se publica si el cambio se revirtió.  
•  Los efectos no se duplican aunque un mensaje llegue más de una vez.  
•  Los mensajes que fallan quedan visibles para el administrador de su operador y pueden reprocesarse sin duplicar efectos.  
•  El inquilino y la traza viajan con cada mensaje, y el inquilino nunca sale del llamador ni del contenido.  
•  Todo el mecanismo es código propio, acotado y explicable.

## **4.2 Negativas y riesgos**

•  Consistencia eventual: entre el cambio y la reacción pasan algunos segundos.  
•  El equipo debe implementar y probar el publicador, la inbox, los reintentos y el registrador de fallidos; es código de infraestructura sensible.  
•  Todo consumidor debe ser idempotente; un consumidor que no registre la inbox en la misma transacción que su efecto puede duplicar efectos.  
•  Los consumidores no pueden suponer orden entre mensajes.  
•  El publicador vive en la API: si la API está caída, los eventos se acumulan en el outbox hasta que vuelva. No se pierden.  
•  El outbox, la inbox y las consultas del publicador quedan fuera del filtro de inquilino. La protección depende de que `AgregarAlOutbox` sea el único camino de escritura y de que el publicador sea el único lector; las dos cosas se verifican con pruebas.  
•  La topología crece con cada consumidor: una cola y tres colas de espera por consumidor.  
•  El schema por módulo agrega una convención que deben respetar todas las migraciones.

# **5\. Verificación**

La decisión se considera verificada cuando existan pruebas automatizadas que demuestren lo siguiente:

| # | Comprobación | Estado al 16/10 |
| :---: | :---- | :---- |
| 1 | Si el `SaveChanges` falla, no queda ninguna fila en el outbox, también cuando el caso de uso escribe en dos módulos | Cubierta: `OutboxTests` y `ArmarRutaTests` (CU-40) |
| 2 | El publicador lee sin inquilino los pendientes de todos los operadores, con los metadatos de la sesión que los generó | Cubierta: `OutboxTests` |
| 3 | Sin operador en la sesión no se puede registrar un evento | Cubierta: `OutboxTests` |
| 4 | Cada módulo tiene su tabla outbox, en su schema y sin filtro "Tenant" | Cubierta: `OutboxTests` |
| 5 | Si RabbitMQ no está disponible, las filas quedan pendientes y se publican cuando vuelve | Cubierta: `AvisosRutaTests` (con el canal cerrado). Las pruebas de resiliencia lo extienden a la caída del broker |
| 6 | Un mismo mensaje entregado dos veces produce el efecto una sola vez | Cubierta: `AvisosRutaTests` |
| 7 | Un mensaje que falla recorre los reintentos con espera creciente y termina en la cola de fallidos y en `mensajes_fallidos` | Reintentos y cola de fallidos cubiertos en `AvisosRutaTests`; `mensajes_fallidos`, 29/10 |
| 8 | El `OperadorId` del sobre determina el inquilino del consumidor, y un mensaje sin inquilino nunca se procesa | Cubierta: `AvisosRutaTests` |
| 9 | Cada administrador ve sólo los fallidos de su operador, y reintentar conserva el `MessageId` sin duplicar efectos | 29/10 (CU-09) |
| 10 | La traza del request continúa en la publicación y en el consumidor del Worker | Cubierta: `TrazasMensajeriaTests` |

# **6\. Decisiones a revisar al medir**

| Tema | Valor inicial | Cuándo revisarlo |
| :---- | :---- | :---- |
| Intervalos de reintento | 5 s, 30 s y 2 min | Con las pruebas de resiliencia y los avisos a comercios (CU-64) |
| Intervalo y tamaño del lote del publicador | 1 o 2 s; 50 filas | Si la métrica de antigüedad del pendiente más viejo supera unos segundos |
| Retención | 7 días (outbox e inbox); 30 días (fallidos cerrados) | Antes del despliegue en producción del 5/11 |

Las preguntas abiertas de la versión 0.1 quedaron resueltas:
- **Schema por módulo:** se adoptó (migraciones del 6/10 y del 12/10).
- **Implementación propia o Wolverine:** implementación propia (sección 2.7).
- **Fallidos en la consola o en el sistema:** en el sistema, según CU-09 (sección 2.6).

# **7\. Plan de implementación**

| Hito | Qué | Responsable |
| :---- | :---- | :---- |
| 15/10 | Tabla outbox por módulo en `ModuleDbContext`, `AgregarAlOutbox` y migraciones. CU-40 registra `EnvioAsignadoARuta`. **Hecho el 12/10.** | Ezequiel |
| 16/10 | Publicador, topología, inbox de Seguimiento, consumidor con inquilino del sobre, reintentos y cola de fallidos, con el primer consumidor (`RutaDespachada`, CU-43). **Hecho el 16/10.** | Lucas |
| 16/10 | Trazas del publicador y del consumidor, OpenTelemetry en el Worker y espera del consumidor sólo con las colas vacías. **Hecho el 16/10.** | Ezequiel |
| 29/10 | Registrador de fallidos, tabla `mensajes_fallidos`, cola `logistica.fallidos.sin-inquilino`, retención y consumidores de Seguimiento para los demás eventos | Ezequiel |
| 29/10 | CU-09 en el Backoffice sobre `mensajes_fallidos` | Ezequiel |
| 5/11 | Métricas del outbox y de los fallidos en el entorno productivo | Ezequiel |

# **8\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Propuesta inicial. | Ezequiel Marcenal |
| 1.0 | 12/10/2026 | Se completa la decisión: el outbox sin filtro de inquilino y con el inquilino del sobre tomado de la sesión (`AgregarAlOutbox`), columnas e índice de la tabla, `IIntegrationEventPublisher` por módulo, semántica de entrega y orden, clave de la inbox, reintentos con colas de espera, cola de fallidos con registrador y tabla `mensajes_fallidos` para CU-09, propiedades AMQP del mensaje, retención, observabilidad y estado de la verificación. Se agregan las alternativas 3.5 a 3.10 y se resuelven las preguntas abiertas. | Ezequiel Marcenal |
| 1.1 | 12/10/2026 | Aprobado por el equipo (Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes), incluidos la tabla `mensajes_fallidos` con su registrador, `IIntegrationEventPublisher` por módulo y el sobre en cabeceras AMQP. | Ezequiel Marcenal |
| 1.2 | 16/10/2026 | Se documenta cómo quedó la implementación (Lucas Ottonello, CU-43): el publicador sólo publica los tipos con consumidor declarado; `InquilinoMensaje` reemplaza a `InquilinoFijo` en el publicador y el consumidor; consumo con `BasicGet` y espera sólo con las colas vacías; `timestamp` en segundos. Se detallan los spans de la fuente `Logistica.Mensajeria` (sección 2.9) y se actualizan la verificación y el plan. | Ezequiel Marcenal |
