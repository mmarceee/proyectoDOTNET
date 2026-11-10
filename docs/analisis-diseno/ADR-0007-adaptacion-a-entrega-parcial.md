# ADR-0007 · Adaptación del dominio y los contratos a la entrega parcial

| Dato | Valor |
| --- | --- |
| Estado | **Aceptado como decisión de diseño. Implementación y evaluación retrospectiva pendientes.** |
| Versión y fecha | 1.8 · 09/11/2026 |
| Responsable | Lucas Ottonello |
| Equipo | Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| Base de código inspeccionada | `586f630` (CU-42) |
| Decisión funcional | [Decisiones cerradas de entrega parcial](decisiones-entrega-parcial.md) |
| Relacionados | ADR-0001 a ADR-0006; RF 31 a 36; CU-06, CU-17/18/20/21/30/31/40/41/42/43/50/51/52/54/55/60/64/65/66 y liquidaciones. |

## 1. Contexto y restricciones

La letra original, sección 8.2, pide un responsable identificado y cuatro partes: contexto y restricciones, decisión, alternativas y consecuencias. El documento docente de cambio, sección 2.3, exige además analizar qué componentes hubo que modificar, qué resultó sencillo, qué no y por qué. Este ADR registra ahora la decisión y la evaluación del diseño; la tabla retrospectiva se completará con la implementación y su evidencia, sin presentar trabajo previsto como ya realizado.

Para el equipo de tres integrantes es obligatoria la entrega parcial (RF 31 a 36), no el retiro en domicilio. Debe demostrarse sin conexión el 12/11, sincronizar y comprobar backoffice, seguimiento público y aviso al comercio. La entrega final permanece el 15/11 a las 23:59 y la defensa el 19/11.

El cambio exige resultado y estado por bulto, estado del envío derivado, transiciones declarativas verificables, reglas versionadas por operador, reasignación de pendientes, progreso público sin identificadores internos y avisos compatibles mediante versionado. Se mantienen aislamiento, Outbox, idempotencia y operación offline.

En `586f630`, Bulto todavía carece de estado. Envio.Transicionar cambia el estado del envío y registra su historial; los handlers/repositorios registran separadamente los eventos de integración en Outbox. IntentoEntrega y los contratos de sincronización aún representan un resultado por envío. Planificación y carga usan pertenencia/estado por envío. Por tanto, la entrega parcial no está implementada en esta base.

La arquitectura existente aporta límites de módulo y mecanismos reutilizables, pero no elimina el coste de haber situado el resultado operativo en el envío. El plazo y el tamaño del equipo obligan a extender esas piezas sin reemplazar el monolito ni construir una infraestructura nueva.

## 2. Decisión adoptada

### 2.1 Dominio, resumen e invariantes

Se mantiene Envio como raíz coordinadora con bultos identificables. Los estados operativos y sus guardas se aplican a cada bulto. El envío conserva un resumen persistido y derivado: **Creado, En operación, Entregado parcialmente y Entregado**, más **Terminado** y cantidades por resultado. La definición funcional y ejemplos están en el documento enlazado; no se duplican aquí matrices contradictorias.

El resumen no gobierna recepción, carga, asignación o devolución. Cada operación valida los bultos afectados y recalcula el envío una sola vez, al final. Un cierre parcial conserva Entregado parcialmente; Terminado distingue si queda trabajo. Un cierre sin entregas se muestra como Finalizado sin entrega con desglose, sin introducir un quinto estado persistido del resumen.

La tabla de transiciones de bulto y la del resumen deben seguir siendo declarativas y verificables. No se omiten controles para hacer pasar combinaciones nuevas ni se fuerzan estados artificiales del envío para conservar T1 a T19 sin revisión. Un intento fallido de revisita no borra entregas anteriores. La ejecución se inicia con la recepción del primer bulto; ningún reintegro ni cancelación posterior hace volver el resumen a Creado.

### 2.2 Comunicación, atomicidad y sincronización

Los casos de uso continúan como vertical slices en el módulo propietario (ADR-0001). La comunicación inmediata usa Contracts en memoria; ningún módulo lee o modifica el DbContext ajeno.

Una visita pertenece a un envío dentro de una parada y contiene resultados por bulto. Se conserva OperacionId y se extiende ADR-0004 con atomicidad por visita de un envío: resultados, evidencia aplicada, estado derivado, registro de operación y Outbox se confirman juntos o se rechazan juntos. Otros envíos del lote continúan. Un rechazo conserva la evidencia y el diagnóstico; un reenvío devuelve el resultado registrado.

Los cambios que alimentan al Worker, avisos y proyecciones durables se registran por Outbox (ADR-0003). El consumidor mantiene idempotencia. Un cambio de bulto puede generar un evento aunque el resumen del envío no cambie. Los DTOs HTTP se amplían en Http.Contracts; las operaciones offline antiguas se traducen a una visita con el mismo resultado para todos los bultos del envío (ADR-0004, sección 2.11).

### 2.3 Configuración, aislamiento y compatibilidad

Administración incorpora las reglas RF 33 a versiones por operador; el envío conserva su versión para visitas y sincronizaciones posteriores. El cargo fijo de una visita adicional se registra una sola vez al realizarla y se integra con liquidación mediante su contrato de negocio.

Se mantienen tenant de sesión/mensaje, filtros e interceptor del ADR-0002. La pertenencia de cada BultoId debe comprobarse contra operador, comercio cuando corresponda, envío y visita/ruta. Los datos de dispositivo no fijan el tenant ni pueden trasladar evidencia a otro envío.

Seguimiento preserva el token público y no expone identificadores internos. Su caché sigue separado por operador (ADR-0006) y se invalida también cuando cambia el progreso por bulto. Los avisos v2 incorporan eventos de bulto; v1 mantiene semántica compatible, sin nuevos valores inesperados ni falsas entregas completas. Payload, firma y versión deben ser reproducibles en reintentos y reenvíos. El contrato se especificó e implementó el 09/11 (RF 36): la versión es de la suscripción y la entrega la conserva; ver el [contrato de avisos](cu-63/integracion-cu64.md#mensaje-v2-rf-36).

### 2.4 Adaptación incremental y alcance por componente

| Componente y responsable de referencia | Se reutiliza | Modificación prevista para el cambio |
| --- | --- | --- |
| Envíos y Administración · Cristian | Agregado Envio, catálogo de motivos, versiones de reglas, filtros y repositorios. | Estados/transiciones por bulto, resultados de visita, resumen/Terminado, reglas de parcial y consultas por bulto; migraciones. |
| Planificación y Ejecución/PWA · Lucas, con coordinación del servidor de sincronización | Paradas agrupadas, contratos entre módulos, revisión de ruta, IndexedDB cifrado, OperacionId, unidad de trabajo y registro de conflictos. | Pertenencia y reservas por bulto, carga/rendición, selección por escaneo, evidencia, resultados offline y sincronización de la visita. |
| Seguimiento y avisos · Ezequiel; progreso público con Cristian | Outbox, RabbitMQ, inbox/idempotencia, Worker, suscripciones, firma, reintentos, fallidos y reenvío. | Eventos de bulto, v1/v2, avance/contadores, invalidación, tablero y consultas del historial. |
| Depósito y liquidaciones · coordinación con Ezequiel | Recepción escaneada, medidas, transacciones compartidas y contratos. | Recepción/devolución/rendición por bulto y lectura del cargo de revisita. |
| CU-42 · Lucas | Ordenamiento de paradas por identificador, franja, zona y código postal. | No requiere interpretar resultados de bulto; RF 34 le entrega las visitas resultantes. Revalidar si cambia su agrupación. |

Los estados internos actuales se conservan únicamente como compatibilidad durante la transición, donde haga falta; no constituyen el modelo final acordado ni una segunda fuente editable de verdad. Cada consumidor debe migrarse y verificarse antes de retirar el camino anterior. No se cambia globalmente el enum existente sin revisar consultas, historiales y contratos v1.

El detalle de tareas y disponibilidad de cada integrante se coordina con el plan de trabajo; esta tabla identifica componentes y no reasigna unilateralmente todo el servidor de sincronización a Lucas.

## 3. Alternativas consideradas y motivo del descarte

| Alternativa | Evaluación |
| --- | --- |
| Agregar únicamente EntregadoParcialmente al enum del envío | Descartada: no permite resultado, evidencia, conflictos ni reasignación independientes por bulto. |
| Usar todos los estados físicos del bulto como resumen del envío y elegir «el más avanzado/atrasado» | Descartada como resumen final: mezcla custodia con resultado del conjunto, deja precedencias ambiguas y puede ocultar cierres mixtos. Se adopta En operación y el desglose. |
| Calcular el resumen en cada consulta sin persistirlo | Descartada para este alcance: encarece filtros y obliga a modificar inmediatamente muchas consultas. Persistirlo exige un invariante y pruebas contra desincronización. |
| Asignar manualmente el estado del envío además de los bultos | Descartada: introduce dos fuentes de verdad y contradice la derivación requerida. |
| Convertir cada bulto en agregado o servicio independiente | Descartada para este cambio: añade coordinación distribuida y costes de consistencia sin necesidad demostrada; el agregado Envio puede proteger los cambios en una transacción. |
| Aplicar sólo las líneas válidas de una visita | Descartada para la política inicial: complica qué conjunto respalda la firma y la recuperación de resultados parciales. Se conserva evidencia de toda visita rechazada. |
| Romper v1 o avisarle Entregado cuando sólo se entregó una parte | Descartada: afecta integraciones existentes y comunica un resultado falso. La proyección compatible debe acordarse explícitamente. |
| Reescribir el sistema o cambiar el stack para incorporar parcial | Descartada: no aborda el problema del modelo y aumenta el riesgo de calendario. |

## 4. Consecuencias y evaluación de la adaptación

### 4.1 Qué facilita la arquitectura actual

La propiedad de los conceptos está localizada: Envíos concentra el agregado; Administración las reglas; Planificación las visitas; Ejecución las operaciones offline y Seguimiento los avisos. Esto permite localizar cambios y mantener las fronteras. Son ventajas observadas en el código, no una afirmación de que RF 31 a 36 ya funcionan.

Outbox, la unidad de trabajo compartida, el registro idempotente de operaciones, el catálogo versionado, el aislamiento y el Worker son mecanismos existentes que pueden ampliarse. No es necesario añadir otra cola, otro host ni otro almacén local.

CU-42 aporta una evidencia acotada de separación: se implementó sobre las paradas existentes sin modificar estados de bulto ni contratos offline. Su comprobación anterior reunió 28 pruebas unitarias de Planificación, 56 de arquitectura, 11 de integración de CU-42 y 35 regresiones de rutas aprobadas. **Esas 130 pruebas no validan la entrega parcial ni demuestran su aceptación final.**

### 4.2 Qué resulta costoso y por qué

- El estado del envío se usa hoy para permitir o impedir operaciones; convertirlo en resumen obliga a revisar esas decisiones y mover sus guardas al bulto. La modularidad limita el acceso, pero no elimina este acoplamiento semántico.
- Cambia el límite de intento/visita/evidencia y la pertenencia a rutas. No basta con modificar una pantalla o añadir una columna.
- Hay dos dimensiones de compatibilidad: dispositivos con operaciones pendientes y comercios con receptores v1. Se requiere un despliegue coordinado y reglas explícitas de traducción.
- Historial, recepción y carga existentes pueden contener más información que el estado global del envío. Una migración que copie sólo ese estado a todos los bultos perdería detalle.
- Un resumen persistido acelera lecturas, pero toda vía de escritura debe mantenerlo consistente, incluidos procesos automáticos, devoluciones, cancelación, extravío y reintentos.

### 4.3 Riesgos futuros y documentación afectada

El cierre resumido no reemplaza la custodia física, la rendición ni el cierre financiero. Las consultas deben usar la dimensión adecuada. Una corrección posterior de un estado terminal no se introduce como excepción de sincronización: exige su propia transición y política.

**Bultos entregados que pueden quedar sin aplicarse.** Por la atomicidad por visita de un envío (sección 2.2; [ADR-0004](ADR-0004-resolucion-de-conflictos-de-sincronizacion.md), sección 2.11), si un bulto de la visita está en conflicto, los demás bultos entregados en esa visita tampoco se aplican: siguen en tránsito en el servidor, el resumen no los cuenta y el comercio no recibe su aviso, hasta que el despachador revise el conflicto. La visita queda guardada con su evidencia y el diagnóstico por bulto, pero no hay una operación para aplicar después sus bultos válidos. RF 32 no exige que el conflicto de un bulto deje aplicar los demás; elegir la visita como unidad es una interpretación del equipo, justificada en el ADR-0004 (sección 2.11) y verificada con su prueba 10. Si el extravío individual (pendiente 4 de la decisión) se vuelve frecuente, conviene revisarla.

Se deben actualizar [modelo de dominio](modelo-de-dominio.md), [tabla de transiciones](tabla-de-transiciones-de-estados.md), [casos de uso](casos-de-uso.md), ADR-0004, ADR-0006, contratos de avisos y manuales de prueba. Los vínculos añadidos ahora señalan la decisión nueva; no convierten automáticamente sus tablas históricas en el diseño ya implementado.

Quedan por especificar los puntos enumerados en la sección 6 de [decisiones de entrega parcial](decisiones-entrega-parcial.md): custodia y carga incompleta, exclusividad, avisos, progreso y contrato monetario. Este ADR no los presenta como resueltos por omisión. El 03/11/2026 se resolvieron allí la matriz por bulto (pendiente 1), el alcance del extravío para el 12/11 (pendiente 4) y la migración con la traducción de las operaciones antiguas (pendiente 5).

### 4.4 Criterios de verificación antes de cerrar la implementación

| Evidencia requerida | Qué debe comprobar | Estado al 02/11/2026 |
| --- | --- | --- |
| Pruebas de dominio | Tablas declarativas, pares prohibidos, combinaciones alcanzables, resumen, Terminado y ausencia de eventos intermedios ficticios. | Hecho al 09/11: `ResumenEnvioTests` (los ejemplos de la sección 3 de las decisiones, incluido Finalizado sin entrega), `VisitaPorBultoTests` (visita parcial, devolución, recargo y bultos que acompañan al envío); los pares prohibidos, en `EnvioTests` y `ResumenEnvioTests` (`TransicionInvalidaException`). |
| Atomicidad y concurrencia | Fallo de una línea revierte toda la visita; otros envíos continúan; dos operaciones no resuelven dos veces el mismo bulto. | Política y pruebas en el ADR-0004 (sección 2.11 y pruebas 10 a 15). Servidor implementado el 03/11: se evalúan todos los bultos antes de aplicar; una visita rechazada no cambia bultos, resumen ni Outbox; la visita de otro envío del lote se aplica, y un bulto ya resuelto es conflicto (`VisitaPorBultoTests` y `DiagnosticoVisitaTests`). El 09/11: una visita que el envío rechaza al registrarla tampoco deja cambios (`Envio.RegistrarVisita` comprueba la transición antes de tocar los bultos). Falta la PWA nueva. |
| Reenvío idempotente | Un OperacionId repetido no duplica evidencia, intentos, eventos ni cargos; el consumidor no duplica avisos. | Servidor verificado el 09/11: reenviar una visita aplicada con recargo responde *YaAplicada* y no cambia intentos, evidencia, eventos, Outbox ni recargos (`VisitaPorBultoTests`, prueba 14 del ADR-0004). Los consumidores ya descartan mensajes repetidos con su inbox (ADR-0003). Falta la PWA nueva. |
| Rutas y depósito | Sólo pendientes elegibles se reasignan; no hay doble reserva activa por bulto; recepción y rendición preservan custodia y estados. | Parcial al 09/11: la recepción cambia cada bulto y recalcula el resumen (`RecibirBultoTests`); la devolución al comercio tiene sólo los bultos que vuelven, y la rendición devuelve al depósito (T4) lo que no salió (`ConfirmarDevolucionTests` y `ConfirmarRendicionTests`). La rendición del repartidor usa el estado de cada bulto (`IniciarRendicionTests`); falta que carga, hoja de ruta y planificación usen sólo los bultos que acompañan al envío (Cristian) y la devolución de un bulto suelto (pendiente 2 de la decisión). |
| Aislamiento | IDs/evidencia ajenos no acceden ni escriben entre operadores o comercios. | Pendiente sobre los contratos nuevos. |
| Migración/formatos antiguos | Historial preservado y operaciones pendientes tratadas sin inventar resultados ambiguos. | Migración `EntregaParcialBultos` y traducción de las operaciones antiguas a visita, con sus pruebas (`VisitaPorBultoTests`, 03/11). Falta la PWA nueva. |
| Compatibilidad y caché | Receptor v1 compatible, v2 recibe resultados por bulto; pasar de 1/3 a 2/3 actualiza seguimiento aunque el resumen no cambie. | Avisos verificados el 09/11 (RF 36): una suscripción v1 recibe el mismo contenido byte a byte, sin hechos de bulto ni el cierre mixto, y nunca una entrega completa que no ocurrió; una v2 recibe además `BultoEntregado`, `BultoNoEntregado` y `BultoDevuelto`, con el mismo cuerpo y firma en reintentos y reenvíos (`AvisosVersionadosTests`, `EntregarAvisosTests` y `EventosDeBultoTests`). El seguimiento público también se invalida con los eventos de bulto (`InvalidarSeguimiento`); falta verificar de punta a punta el paso de 1/3 a 2/3 y el tablero (pendiente 7). |
| Escenario docente | Tres bultos: dos entregados offline, uno con motivo; sincronizar, comprobar backoffice/público/aviso y reenviar sin duplicados. | Pendiente, objetivo 12/11. |

### 4.5 Retrospectiva obligatoria de la implementación

Completar esta tabla con commits, pruebas y resultados antes de entregar el ADR final. No sustituir evidencia por la afirmación genérica de que «la arquitectura se adapta».

| Componente modificado realmente | Commit y cambio | Qué fue sencillo y por qué | Qué fue difícil y por qué | Evidencia y consecuencias |
| --- | --- | --- | --- | --- |
| RF 31 · dominio por bulto y resumen (Cristian Reyes) | `b36699c` y `df72a26` (PR #71, 03/11): tabla por bulto en `tabla-de-transiciones-de-estados.md`; `Bulto.Estado` e `IntentosFallidos`; `Envio.Resumen`, `Terminado` y `BultosEntregados`, recalculados una vez al final de cada operación; el puente de `Transicionar`; `RecibirBulto`; `RegistrarVisita` con el resultado de cada bulto; migraciones `EntregaParcialBultos` y `BultosDeCadaIntento`. `9dcd5c3` (PR #90, 09/11): `Envio.BultosQueAcompanan` en planificar, hoja de ruta y carga. | El agregado `Envio` ya era el coordinador: los bultos eran sus hijos, así que no hizo falta otro agregado ni otro servicio, y la visita entera se guarda en una transacción. Reutilizar `EstadoEnvio` y la `TablaTransiciones` declarativa para el bulto evitó una segunda máquina de estados. El puente (mover con el envío los bultos que están en su mismo estado) dejó funcionando, sin cambios, a los módulos que todavía decidían por el envío entero. Los marcadores de inquilino y el filtro "Tenant" cubrieron los campos nuevos sin código extra (ADR-0002). | El puente evita reescribir todo de una vez, pero deja un estado de compatibilidad que sólo se calcula y que varios consumidores seguían usando para decidir: la nueva visita (RF 34) no podía confirmar la carga porque se pedía escanear los bultos ya entregados; se corrigió el 09/11 filtrando las consultas de Envíos. La migración tuvo que conservar las recepciones parciales ya escaneadas y se regeneró al integrar `AmbienteEnviosApi` de Lucas. Ezequiel detectó que los bultos no se cargaban con el envío y el resumen se calculaba sobre una lista vacía (`AutoInclude`). Una prueba de Lucas ponía el envío en Reprogramado con SQL sin mover los bultos: un estado imposible desde RF 31. | `ResumenEnvioTests` (los ejemplos de la sección 3 de las decisiones), `VisitaPorBultoTests` (unitarias), `RecibirBultoTests` y `NuevaVisitaTests` (integración, verificada por mutación). Al integrar el 03/11: 391 unitarias, 56 de arquitectura y 560 de integración. El estado de compatibilidad se retira del todo después del 12/11 (decisiones, pendiente 8). |
| RF 33 · reglas de la entrega parcial (Cristian Reyes) | `6108759` (PR #80, 05/11): `ReglasEntregaParcial` dentro de `VersionReglas`, migración `EntregaParcialReglas`, pantalla de CU-06, contrato `ReglasOperativas` y `ReglasEntrega.DebeDevolver`. `3b7d090` (PR #81, 07/11): `RecargoNuevaVisita` en el agregado `Envio`, migración `RecargoNuevaVisita` y `IEnviosModuleApi.ListarRecargosAsync`. | Las reglas ya eran versionadas (CU-06, letra 6.6): bastó un value object nuevo en la versión, y cada envío conserva la suya sin código adicional. El contrato que ya leía Envíos y su caché no cambiaron de forma. La idempotencia del recargo salió gratis: la sincronización descarta una operación repetida antes de llegar a `RegistrarVisita`. | El valor de las versiones ya publicadas: EF proponía un texto vacío que no era un valor válido del enum, y hubo que fijar los valores "como antes". Los datos iniciales no pisan una versión existente, así que en las bases que ya existían Envíos del Este muestra "sin recargo". El recargo faltaba en la primera entrega: se detectó al revisar la tarjeta contra el código. La consulta de la liquidación no se podía traducir a SQL (un `OrderBy` después de armar el resultado); lo encontró la prueba de integración. | `VersionReglasTests`, `EntregaParcialReglasTests`, 5 pruebas unitarias del recargo y `RecargoNuevaVisitaTests` (con aislamiento entre operadores). Cobrar el recargo en la liquidación queda para CU-32 (Ezequiel); la validación en la carga, a coordinar. |
| RF 32/34 · PWA, sincronización y rutas | Pendiente de implementación. | Por registrar. | Por registrar. | Por registrar. |
| RF 35 · progreso público y backoffice (Cristian Reyes) | `83d16f9` (PR #75, 04/11–05/11): columna "Entrega" del listado del Backoffice. `7b8e049` (PR #81, 07/11): `SeguimientoEnvio` con el resumen, los entregados y cada bulto; `TextosSeguimiento` (título, "N de M bultos entregados" y "BTO N"); página pública. | El seguimiento de CU-60 ya tenía su contrato, su caché y su invalidación por eventos: alcanzó con sumar campos. Cada visita ya publicaba `EnvioEntregado` o `IntentoFallidoRegistrado`, así que el avance se actualiza al sincronizar sin eventos nuevos. El detalle del Backoffice ya mostraba el resumen y el estado de cada bulto desde RF 31. | Con 2 de 3 entregados, la página decía "Entrega reprogramada": el estado de compatibilidad sigue el camino del intento fallido por el bulto pendiente, y hubo que tomar el título del resumen. "Sin exponer identificadores internos" obligó a decidir cómo nombrar cada bulto: lo acordó el equipo ("BTO 1, 2, 3"). La recepción de un bulto suelto no publica un evento y puede tardar hasta un minuto en verse (ADR-0006). | `TextosSeguimientoTests` y `SeguimientoPublicoTests`: con una entrega parcial, el JSON no contiene el Id ni el código de ningún bulto. Prueba manual con un envío de 3 bultos (0 de 3 y 2 de 3). |
| RF 36 · avisos v1 y v2 | Ver la fila "Compatibilidad y caché" de la sección 4.4. | Por registrar (Ezequiel Marcenal). | Por registrar. | Por registrar. |
| Migración, regresiones y despliegue | Pendiente de ejecución. | Por registrar. | Por registrar. | Por registrar. |

## Referencias e historial

- Letra original: [Laboratorio .NET 2026](Laboratorio__NET_2026.pdf), sección 8.2. Cambio docente: *Cambios de Requerimientos Planificados.pdf*, secciones 2.1 y 2.3, aportado al equipo.
- [Decisión funcional cerrada](decisiones-entrega-parcial.md), [análisis previo y evidencia](revision-cambio-requerimientos-y-aclaraciones.md) y [CU-42](cu-42/README.md).
- [ADR-0001](ADR-0001-estilo-arquitectonico.md), [ADR-0002](ADR-0002-estrategia-de-multitenancy.md), [ADR-0003](ADR-0003-outbox-y-mensajeria.md), [ADR-0004](ADR-0004-resolucion-de-conflictos-de-sincronizacion.md), [ADR-0005](ADR-0005-modo-de-renderizado-de-blazor.md) y [ADR-0006](ADR-0006-estrategia-de-cache.md).

Este registro es el ADR-0007 del catálogo numerado actual. El documento docente permite contarlo entre los ocho exigidos; crearlo no completa por sí solo el mínimo de ocho ni la evaluación retrospectiva del cambio.

| Versión | Fecha | Cambio | Responsable |
| --- | --- | --- | --- |
| 1.0 | 02/11/2026 | Decisión de diseño aceptada, impacto identificado y criterios de verificación/retrospectiva pendientes explícitos. | Lucas Ottonello |
| 1.1 | 02/11/2026 | Criterio de inicio de ejecución: recepción del primer bulto. | Ezequiel Marcenal |
| 1.2 | 03/11/2026 | Sección 4.3: riesgo de bultos entregados sin aplicar por la atomicidad por visita; sección 4.4: la atomicidad queda especificada en el ADR-0004, versión 1.4. | Ezequiel Marcenal |
| 1.3 | 03/11/2026 | Sección 4.3: pendientes 1, 4 y 5 de la decisión resueltos; sección 4.4: estado de la migración y de los formatos antiguos. | Ezequiel Marcenal |
| 1.4 | 06/11/2026 | Sección 4.4: la atomicidad por visita queda implementada en el servidor, con sus pruebas. Sección 2.2: las operaciones offline antiguas ya están especificadas en el ADR-0004. | Ezequiel Marcenal |
| 1.5 | 09/11/2026 | Sección 4.4: reenvío idempotente de la visita verificado en el servidor, y atomicidad ante un rechazo al registrar la visita. | Ezequiel Marcenal |
| 1.6 | 09/11/2026 | Sección 4.4: estado de rutas y depósito (recepción, devolución por bulto y T4 en la rendición). | Ezequiel Marcenal |
| 1.7 | 09/11/2026 | Secciones 2.3 y 4.4: avisos v1 y v2 con los eventos de bulto (RF 36), especificados e implementados. | Ezequiel Marcenal |
| 1.8 | 09/11/2026 | Sección 4.4: la rendición del repartidor usa el estado de cada bulto. | Ezequiel Marcenal |
| 1.9 | 09/11/2026 | Sección 4.5: retrospectiva de RF 31, RF 33 y RF 35 (commits, qué fue sencillo, qué fue difícil y evidencia); la fila de RF 35/36 se separa para que Ezequiel complete RF 36. Sección 4.4: pruebas de dominio hechas. | Cristian Reyes |
