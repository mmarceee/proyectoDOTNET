# ADR-0007 · Adaptación del dominio y los contratos a la entrega parcial

| Dato | Valor |
| --- | --- |
| Estado | **Aceptado. Decisiones y evaluación retrospectiva cerradas para este alcance al 09/11/2026. Implementación y aceptación pendientes identificadas en 4.6.** |
| Versión y fecha | 2.5 · 10/11/2026 |
| Responsable | Lucas Ottonello |
| Equipo | Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| Base de código inspeccionada | Inicial: `586f630` (CU-42). Cierre documental: `549c1ae`, con la prueba de caché agregada en esta revisión. |
| Decisión funcional | [Decisiones cerradas de entrega parcial](decisiones-entrega-parcial.md) |
| Cierre técnico y estudio | [Decisiones técnicas](cierre-tecnico-entrega-parcial.md) · [Guía de estudio y defensa](guia-estudio-ADR-0007.md) |
| Relacionados | ADR-0001 a ADR-0006; RF 31 a 36; CU-06, CU-17/18/20/21/30/31/40/41/42/43/50/51/52/54/55/60/64/65/66 y liquidaciones. |

## 1. Contexto y restricciones

La letra original, sección 8.2, pide un responsable identificado y cuatro partes: contexto y restricciones, decisión, alternativas y consecuencias. El documento docente de cambio, sección 2.3, exige además analizar qué componentes hubo que modificar, qué resultó sencillo, qué no y por qué. Este ADR registra la decisión y la retrospectiva de la implementación disponible al 09/11. Su cierre documental no declara completadas RF 31 a 36: la sección 4.6 conserva los límites y la aceptación pendiente.

Para el equipo de tres integrantes es obligatoria la entrega parcial (RF 31 a 36), no el retiro en domicilio. Debe demostrarse sin conexión el 12/11, sincronizar y comprobar backoffice, seguimiento público y aviso al comercio. La entrega final permanece el 15/11 a las 23:59 y la defensa el 19/11.

El cambio exige resultado y estado por bulto, estado del envío derivado, transiciones declarativas verificables, reglas versionadas por operador, reasignación de pendientes, progreso público sin identificadores internos y avisos compatibles mediante versionado. Se mantienen aislamiento, Outbox, idempotencia y operación offline.

En la base inicial `586f630`, Bulto carecía de estado. Envio.Transicionar cambiaba el estado del envío y registraba su historial; los handlers/repositorios registraban separadamente los eventos de integración en Outbox. IntentoEntrega y los contratos de sincronización representaban un resultado por envío. Planificación y carga usaban pertenencia/estado por envío. Ese diagnóstico motivó el cambio; el avance real sobre `549c1ae` se evalúa en 4.4 y 4.5.

La arquitectura existente aporta límites de módulo y mecanismos reutilizables, pero no elimina el coste de haber situado el resultado operativo en el envío. El plazo y el tamaño del equipo obligan a extender esas piezas sin reemplazar el monolito ni construir una infraestructura nueva.

## 2. Decisión adoptada

### 2.1 Dominio, resumen e invariantes

Se mantiene Envio como raíz coordinadora con bultos identificables. Los estados operativos y sus guardas se aplican a cada bulto. El envío conserva un resumen persistido y derivado: **Creado, En operación, Entregado parcialmente y Entregado**, más **Terminado** y cantidades por resultado. La definición funcional y ejemplos están en el documento enlazado; no se duplican aquí matrices contradictorias.

El resumen no gobierna recepción, carga, asignación o devolución. Cada operación valida los bultos afectados y recalcula el envío una sola vez, al final. Un cierre parcial conserva Entregado parcialmente; Terminado distingue si queda trabajo. Un cierre sin entregas se muestra como Finalizado sin entrega con desglose, sin introducir un quinto estado persistido del resumen.

La máquina del bulto usa la tabla declarativa de pares permitidos. El resumen usa las condiciones ordenadas declaradas en la decisión funcional, implementadas como función pura y verificadas por ejemplos: no se agrega una segunda máquina editable. Ambas reglas deben seguir siendo verificables. No se omiten controles para hacer pasar combinaciones nuevas ni se fuerzan estados artificiales del envío para conservar T1 a T19 sin revisión. Un intento fallido de revisita no borra entregas anteriores. La ejecución se inicia con la recepción del primer bulto; ningún reintegro ni cancelación posterior hace volver el resumen a Creado.

### 2.2 Comunicación, atomicidad y sincronización

Los casos de uso continúan como vertical slices en el módulo propietario (ADR-0001). La comunicación inmediata usa Contracts en memoria; ningún módulo lee o modifica el DbContext ajeno.

Una visita pertenece a un envío dentro de una parada y contiene resultados por bulto. Se conserva OperacionId y se extiende ADR-0004 con atomicidad por visita de un envío: resultados, evidencia aplicada, estado derivado, registro de operación y Outbox se confirman juntos o se rechazan juntos. Otros envíos del lote continúan. Un rechazo conserva la evidencia y el diagnóstico; un reenvío devuelve el resultado registrado.

Los cambios que alimentan al Worker, avisos y proyecciones durables se registran por Outbox (ADR-0003). El consumidor mantiene idempotencia. Un cambio de bulto puede generar un evento aunque el resumen del envío no cambie. Los DTOs HTTP se amplían en Http.Contracts; las operaciones offline antiguas se traducen a una visita con el mismo resultado para todos los bultos del envío (ADR-0004, sección 2.11).

### 2.3 Configuración, aislamiento y compatibilidad

**Avance de RF 32 · 08/11/2026.** La [implementación de la PWA](rf-32/README.md) incorpora visita por bulto, foto compartida, hoja schema 3, borrador/operación cifrados, diagnóstico local de rechazo, carga parcial y proyección de rendición. No declara terminadas RF 34, las reservas por bulto ni el resto de la adaptación. El documento enlazado distingue pruebas, límites e integración pendiente. La firma respalda los entregados; la única foto de presencia puede compartirse con los resultados no entregados de esa misma visita, según la confirmación de Lucas del 08/11.

**Continuación RF 32 · 09/11/2026.** Los contratos y la recuperación se consolidan en la [especificación técnica](rf-32/especificacion-tecnica.md). Lucas eligió mantener firma, foto y textos sin confirmar en pantalla y advertir antes de salir; la captura confirmada conserva su evidencia cifrada. La advertencia de navegación/recarga y el shell publicado sin red tienen un ensayo reproducible de navegador; la [verificación de cierre](rf-32/verificacion-cierre.md) separa esos resultados de la aceptación física pendiente. No amplía la atomicidad ni incorpora reservas individuales.

Administración incorpora las reglas RF 33 a versiones por operador; el envío conserva su versión para visitas y sincronizaciones posteriores. El cargo fijo de una visita adicional se registra una sola vez al realizarla y se integra con liquidación mediante su contrato de negocio.

Se mantienen tenant de sesión/mensaje, filtros e interceptor del ADR-0002. La pertenencia de cada BultoId debe comprobarse contra operador, comercio cuando corresponda, envío y visita/ruta. Los datos de dispositivo no fijan el tenant ni pueden trasladar evidencia a otro envío.

Seguimiento preserva el token público y no expone identificadores internos. Su caché sigue separado por operador (ADR-0006) y se invalida también cuando cambia el progreso por bulto. Los avisos v2 incorporan eventos de bulto; v1 mantiene semántica compatible, sin nuevos valores inesperados ni falsas entregas completas. Payload, firma y versión deben ser reproducibles en reintentos y reenvíos. El contrato se especificó e implementó el 09/11 (RF 36): la versión es de la suscripción y la entrega la conserva; ver el [contrato de avisos](cu-63/integracion-cu64.md#mensaje-v2-rf-36).

### 2.4 Adaptación incremental y alcance por componente

La tabla conserva el reparto y el alcance previstos al decidir el cambio. La ejecución real y sus límites están en 4.4 a 4.6.

| Componente y responsable de referencia | Se reutiliza | Modificación prevista para el cambio |
| --- | --- | --- |
| Envíos y Administración · Cristian | Agregado Envio, catálogo de motivos, versiones de reglas, filtros y repositorios. | Estados/transiciones por bulto, resultados de visita, resumen/Terminado, reglas de parcial y consultas por bulto; migraciones. |
| Planificación y Ejecución/PWA · Lucas, con coordinación del servidor de sincronización | Paradas agrupadas, contratos entre módulos, revisión de ruta, IndexedDB cifrado, OperacionId, unidad de trabajo y registro de conflictos. | Pertenencia y reservas por bulto, carga/rendición, selección por escaneo, evidencia, resultados offline y sincronización de la visita. |
| Seguimiento y avisos · Ezequiel; progreso público con Cristian | Outbox, RabbitMQ, inbox/idempotencia, Worker, suscripciones, firma, reintentos, fallidos y reenvío. | Eventos de bulto, v1/v2, avance/contadores, invalidación, tablero y consultas del historial. |
| Depósito y liquidaciones · coordinación con Ezequiel | Recepción escaneada, medidas, transacciones compartidas y contratos. | Recepción/devolución/rendición por bulto y lectura del cargo de revisita. |
| CU-42 · Lucas | Ordenamiento de paradas por identificador, franja, zona y código postal. | No requiere interpretar resultados de bulto; RF 34 le entrega las visitas resultantes. Revalidar si cambia su agrupación. |

Los estados internos actuales se conservan únicamente como compatibilidad durante la transición, donde haga falta; no constituyen el modelo final acordado ni una segunda fuente editable de verdad. Cada consumidor debe migrarse y verificarse antes de retirar el camino anterior. No se cambia globalmente el enum existente sin revisar consultas, historiales y contratos v1.

El detalle de tareas y disponibilidad de cada integrante se coordina con el plan de trabajo; esta tabla identifica componentes y no reasigna unilateralmente todo el servidor de sincronización a Lucas.

### 2.5 Cierre de las decisiones técnicas

El [cierre técnico](cierre-tecnico-entrega-parcial.md) forma parte de esta decisión y especifica el modelo objetivo. D1 separa estado, custodia y reserva; D2 fija transacción, revisión e índice único por bulto; D3 conserva atomicidad y define devolución de subconjuntos; D4 migra consumidores y reprogramación; D5 cubre progreso, tablero y compatibilidad; D6 fija migración conservadora y retirada gradual del puente; D7 define aceptación observable; D8 fija el contrato monetario y D9 la recuperación de captura, ambas confirmadas expresamente por Lucas el 09/11.

Se usa UYU en pesos enteros, sin centésimos, con redondeo al peso más cercano y 0,50 hacia arriba. La tarifa base se cobra una vez al terminar todos los bultos, si al menos uno fue entregado o devuelto; no se cobra si todos quedaron cancelados/extraviados. Los recargos de visitas reales se liquidan aparte, una vez cada uno. La captura sin confirmar debe conservar también firma, foto y textos cifrados; cambiar entregados invalida la firma. D8/D9 concretan unicidad, períodos, concurrencia y recuperación. Estas reglas cierran las elecciones pendientes, sin declarar implementadas sus capacidades objetivo.

Las capacidades pendientes se implementan con esos criterios. No se acepta resolverlas relajando guardas, suponiendo custodia por falta de escaneo, interpretando Terminado como Entregado o reescribiendo una operación offline confirmada. La sección 4.6 identifica ejecución pendiente; no delega decisiones de diseño a quien implemente cada pantalla.

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

La sección 6 de [decisiones de entrega parcial](decisiones-entrega-parcial.md) registra el avance incremental: la matriz por bulto, el alcance del extravío, la migración y los avisos v1/v2 están resueltos dentro de sus límites. Persisten custodia y exclusividad individual, parte de los consumidores operativos, tablero y cobro del recargo. La sección 4.6 concreta esas deudas; no se dan por resueltas por omisión.

### 4.4 Verificación de la implementación al 09/11/2026

**Actualización del 10/11:** la tabla conserva el estado histórico del 09/11. La [aceptación integrada de RF 32](rf-32/aceptacion-integrada-2026-11-10.md) completó después el ensayo Android offline, conflicto, Backoffice/público, receptor externo v1/v2 y reenvío sin duplicados. Ese cierre no acredita D9, actualización de datos/colas anteriores ni el resto de las deudas de 4.6.

«Verificado» refiere al alcance de las pruebas citadas. No convierte una prueba de aplicación o de IndexedDB en una demostración del circuito publicado con teléfono, Worker y receptor externo.

| Evidencia requerida | Resultado observado y evidencia | Límite para la aceptación |
| --- | --- | --- |
| Dominio y resumen | `ResumenEnvioTests`, `EnvioTests`, `VisitaPorBultoTests` y `CargaParcialBultosTests`: estados individuales, pares prohibidos, resultados mixtos, terminales, recepción inicial, cancelación y conservación de entregas anteriores. `TablaTransiciones` declara los pares; `CalcularResumen` implementa las condiciones ordenadas del resumen. | La revisión 2.1 adopta explícitamente esa función ordenada como representación del resumen (2.1). Sigue existiendo `EstadoEnvio` de compatibilidad, cuya retirada está especificada en D4/D6. |
| Atomicidad y conflictos | `Ejecucion/VisitaPorBultoTests` y `DiagnosticoVisitaTests`: una línea inválida rechaza la visita completa; otro envío del lote continúa; un rechazo al registrar no deja bultos, resumen ni Outbox modificados; se conserva diagnóstico. | La política rechaza también las entregas físicamente realizadas de esa visita (4.3). Las reservas concurrentes por bulto son una deuda diferente. |
| Idempotencia | `Reenviar_una_visita_aplicada_no_duplica_resultados_evidencia_eventos_ni_recargos`, `Rf32PwaTests` y pruebas JavaScript: mismo OperacionId/payload, respuesta YaAplicada y ausencia de nuevos efectos. `EntregarAvisosTests` comprueba reintento/reenvío y la deduplicación del consumidor. | La garantía depende de atravesar el procesador transaccional; no basta invocar el agregado directamente. No sustituye el ensayo de pérdida de respuesta del circuito publicado. |
| Rutas, carga y depósito | `NuevaVisitaTests`, `ConfirmarCargaTests`, `DescargarHojaDeRutaTests`, `IniciarRendicionTests`, `Rf32PwaTests`, `RecibirBultoTests`, `ConfirmarDevolucionTests` y `ConfirmarRendicionTests`: carga sólo de pendientes, carga en etapas, manifiesto compartido con rendición, entregados anteriores preservados y recuperación de no cargados con confirmación física explícita. | RF 34 parcial: reserva todavía por envío; falta conciliación/liberación/reasignación individual. La devolución de un bulto suelto está implementada desde el 10/11: sólo el bulto que recibe el comercio pasa a Devuelto, con varias devoluciones por envío y una sola abierta (`ConfirmarDevolucionTests`). No se acredita custodia por ausencia de escaneo. |
| Aislamiento | Las suites de Tenancy, `AislamientoPlanificacionTests`, `SincronizacionTests` y `RecargoNuevaVisitaTests` cubren filtros, sesiones, rutas y cargos entre operadores. `VisitaPorBultoTests` rechaza bulto ajeno al envío; JavaScript comprueba separación local por identidad. | Esa cobertura no demuestra por sí sola una matriz completa de evidencia ajena entre operador/comercio/ruta con el contrato nuevo. Conservar esa verificación en la aceptación. |
| Migración y compatibilidad offline | Migraciones `EntregaParcialBultos`, `BultosDeCadaIntento`, `EntregaParcialReglas`, `RecargoNuevaVisita` y `AvisosVersionados`; traducción de operaciones antiguas en `VisitaPorBultoTests`; schemas 1/2/3 y conservación de cola en JavaScript. Las integraciones arrancan sobre PostgreSQL con migraciones. | Migrar bases nuevas no equivale a ensayar una actualización con copia representativa de datos y colas anteriores. No se ensayó aquí un despliegue de producción. |
| Progreso público y caché | `SeguimientoPublicoTests` verifica 2/3 sin Id/código interno. La nueva `CacheSeguimientoTests.Un_evento_de_bulto_actualiza_de_uno_a_dos_de_tres_aunque_el_resumen_no_cambie` comprueba caché anterior, evento `BultoEntregado` y lectura 2/3 con el mismo resumen. | Prueba de consumidor y consulta con caché en memoria: no recorre RabbitMQ/Worker/Valkey reales. La recepción de un bulto suelto continúa limitada por TTL de un minuto (ADR-0006). |
| Avisos compatibles | `AvisosVersionadosTests`, `EntregarAvisosTests` y `EventosDeBultoTests`: v1 conserva su contenido, excluye hechos de bulto/cierre mixto y no informa entrega completa falsa; v2 recibe resultados individuales; reintentos y reenvíos conservan cuerpo y firma. | Pendiente confirmar la recepción externa junto con el escenario docente publicado. |
| Tablero | `TableroTests`, `TableroEnVivoTests` y `NovedadesTableroTests` cubren el tablero existente. La inspección de `NotificarNovedadTablero.Tipos` muestra que no incluye los eventos de bulto; sus contadores siguen siendo por envío. | No se da por cubierto el cambio 1/3 → 2/3 en tablero (pendiente 7 de la decisión). |
| Escenario docente | `Rf32PwaTests` y JavaScript cubren tres bultos, dos entregados, motivo individual, foto compartida, nueva visita y reenvío sin duplicados. El README RF 32 registra además un ensayo anterior de Chrome con API simulada. | Falta el circuito conjunto publicado en teléfono sin conexión, sincronización, backoffice, seguimiento, Worker y aviso al comercio, con versión y evidencia registradas. Objetivo 12/11. |

### 4.5 Retrospectiva de la implementación

La revisión incorpora el aporte de Cristian registrado en `d698eb7` (RF 31/33/35), ahora integrado desde main y contrastado con el código. Su verificación por mutación de NuevaVisitaTests se conserva como evidencia histórica del aporte. Se ajusta su descripción de carga/hoja al merge `549c1ae`. Las dificultades de integración se apoyan también en el [registro RF 32](rf-32/README.md); los resultados de esta revisión se distinguen en 4.7.

| Componente modificado realmente | Commit y cambio | Qué fue sencillo y por qué | Qué fue difícil y por qué | Evidencia y consecuencias |
| --- | --- | --- | --- | --- |
| RF 31 · dominio por bulto y resumen · Cristian | `b36699c` y `df72a26`: estado e intentos por bulto, resumen/Terminado, recepción, visita y migraciones. `9dcd5c3`: selección de pendientes para nueva visita. | Envio ya coordinaba a sus bultos: mantuvo el límite transaccional sin otro agregado ni servicio. Se reutilizaron el enum y la tabla declarativa para los bultos y los filtros de inquilino, sin una segunda máquina de estados ni otro agregado. | El estado global estaba en guardas y consultas de muchos consumidores. El puente de `Transicionar` permitió migrar gradualmente, pero las revisitas exigían excluir entregados anteriores. Cargar los bultos con `AutoInclude` evitó recalcular sobre una colección vacía; la migración debió conservar recepciones parciales y se regeneró al integrar AmbienteEnviosApi. La prueba antigua que cambiaba sólo el envío a Reprogramado por SQL producía un estado imposible y debió mover también sus bultos. | `ResumenEnvioTests`, `EnvioTests`, `VisitaPorBultoTests`, `RecibirBultoTests` y `NuevaVisitaTests`. El estado global de compatibilidad permanece como deuda de consumidores; el resumen no lo sustituye automáticamente. |
| RF 33 · reglas y recargo · Cristian | `6108759`: value object `ReglasEntregaParcial`, CU-06 y migración. `3b7d090`: cargo persistido por nueva visita y `ListarRecargosAsync`. | Las versiones ya existían y cada envío conservaba la suya. El procesador idempotente evita volver a registrar el cargo cuando se reenvía una operación aplicada. | Las versiones publicadas necesitaban valores compatibles, no un enum vacío generado por defecto. Los datos iniciales no actualizan versiones existentes: en las bases anteriores, Envíos del Este conserva sin recargo. La prueba de consulta del recargo detectó un ordenamiento no traducible a SQL. | `VersionReglasTests`, `EntregaParcialReglasTests`, `VisitaPorBultoTests` y `RecargoNuevaVisitaTests`. La carga ya valida la prohibición de parcial. Registrar/consultar el cargo está implementado; cobrarlo en CU-32 sigue pendiente. El aporte original registró cinco pruebas unitarias del recargo y aislamiento entre operadores; se conserva como evidencia histórica, no como ejecución de este merge. |
| RF 32 · PWA y sincronización · Lucas, Ezequiel y Cristian según módulo | `12986f9`: servidor de visita y diagnóstico. `f429a00`, `c19edba` y `549c1ae`: formulario por bulto, foto única, cola cifrada, compatibilidad y manifiestos de ruta integrados. | Se reutilizaron Http.Contracts, IndexedDB con AES-GCM, OperacionId, unidad de trabajo, registro de rechazos y Outbox. La visita nueva no exigió otro endpoint ni almacén. | El alcance firmado debía coincidir con entregados, cargados y ruta. La clave vieja de borrador colisionaba entre carga/visitas; se recuperó sin borrar colas. La carga en etapas requería conservar asignados después del primer lote y separar Comenzar ruta. | `Rf32PwaTests`, `Ejecucion/VisitaPorBultoTests`, `DiagnosticoVisitaTests` y 72 pruebas JavaScript. El guardado confirmado incluye evidencia; firma/foto/textos sin confirmar se recapturan si se abandona la pantalla. El ensayo físico/publicado sigue pendiente. |
| RF 34 y depósito · coordinación entre módulos | `9dcd5c3`, `52bc7a4`, `7b21109` e integraciones `c19edba`/`549c1ae`: pendientes para capacidad/carga, rendición por bulto y reintegro con confirmación física. | Los contratos entre módulos y la unidad de trabajo permitieron mantener propiedad de datos y confirmar cierre/reintegro juntos. CU-42 siguió ordenando paradas sin interpretar los resultados. | Hoja y rendición debían compartir el manifiesto: mantener entregados actuales y excluir anteriores. Cerrar una ruta no demostraba que los no cargados estuvieran en depósito; hizo falta confirmación explícita y recuperación de cierres anteriores. | `ConfirmarCargaTests`, `IniciarRendicionTests`, `ConfirmarRendicionTests`, `NuevaVisitaTests` y `Rf32PwaTests`. La reserva por envío impide declarar completa RF 34: faltan reserva/custodia y reasignación independientes por bulto. |
| RF 35 · progreso público y backoffice · Cristian | `83d16f9`: columna Entrega en Backoffice. `7b8e049`: resumen, contadores, BTO N y vista pública. `0ffd81a`: invalidación también por eventos de bulto. | CU-60 ya tenía contrato, caché e invalidación; se ampliaron sus campos y consumidores. El detalle ya mostraba bultos desde RF 31. | El título tomado del estado de compatibilidad decía Entrega reprogramada pese a haber entregado 2/3; se cambió al resumen. La presentación pública necesitaba nombres sin Id/código interno. Los cambios de bulto pueden mantener el mismo resumen. | `TextosSeguimientoTests`, `SeguimientoPublicoTests` y la nueva regresión 1/3 → 2/3 de `CacheSeguimientoTests`. El tablero requiere su propia adaptación; la recepción individual sin evento depende del TTL. |
| RF 36 · avisos versionados · Ezequiel | `0ffd81a`: suscripción v1/v2, eventos individuales, entrega persistida con versión/bulto y serialización/firma correspondientes. | Se reutilizaron Outbox, Worker, inbox, suscripciones, reintentos y reenvíos; no fue necesario otro canal de mensajería. | La compatibilidad exigía conservar los bytes v1 y evitar una entrega completa falsa. El cierre mixto no tiene traducción fiel a v1; se omite para esa versión. La entrega debe preservar cuerpo/firma en reintentos. | `AvisosVersionadosTests`, `EntregarAvisosTests` y `EventosDeBultoTests`. El comercio necesita v2 para recibir hechos de bulto/cierres mixtos; v1 conserva su semántica. |
| Migración, regresiones y despliegue | Migraciones de dominio, reglas, recargos y avisos en los commits anteriores; CI en `.github/workflows/ci.yml`. Verificación local en 4.7. | La infraestructura existente permitió ejecutar la solución y probar contra bases PostgreSQL aisladas. Se usaron artefactos separados de los binarios de desarrollo. | Copiar sólo el estado del envío habría perdido recepciones parciales. La cola antigua y los contratos externos obligaron a conservar traducciones y formatos. Una base recién migrada no prueba toda actualización histórica. | Pruebas de compatibilidad y suites de dominio, arquitectura e integración. No se aplicaron migraciones a la base del usuario ni se desplegó desde esta revisión; siguen pendientes actualización representativa y circuito publicado. |

### 4.6 Decisiones especificadas y ejecución que continúa

La adaptación confirma que los límites del monolito, los contratos y los mecanismos de consistencia existentes fueron reutilizables. También demuestra un coste transversal: el resultado por envío estaba incorporado en guardas, consultas, evidencia y pertenencia a rutas. La arquitectura localizó propietarios y evitó nueva infraestructura, pero no evitó esos cambios semánticos. Con la retrospectiva y la verificación registradas se cierra la evaluación del ADR a esta fecha; la aceptación integral continúa abierta.

| Pendiente real | Condición verificable para cerrarlo | Referencia de coordinación |
| --- | --- | --- |
| Custodia y exclusividad individual (RF 34) | Confirmar ubicación de faltantes; reserva activa por bulto con concurrencia; liberar/reasignar sólo con custodia acreditada. La devolución de un bulto suelto ya está implementada (10/11, CU-31). La recuperación de envíos completos no cargados no cubre esto. | Decisión 4.2 y pendientes 2/3; Planificación, Depósito y Envíos. |
| Consumidores de estado global | Migrar envíos activos a Terminado, liquidables a la dimensión de cierre correcta y reprogramación pública/textos pendientes. Retirar el puente sólo tras verificar cada consumidor. | Decisión, pendiente 8; alcance previsto para el 15/11. |
| Cargo y liquidación | Implementar D8: UYU en pesos enteros/redondeo al peso más cercano (0,50 hacia arriba), base al cierre elegible y recargos separados, unicidad por visita y concepto imputado. El registro actual no resuelve aún todo ese contrato monetario. | Decisión 5 y D8; Depósito/Envíos. `GenerarLiquidacionHandler` aún no llama a `ListarRecargosAsync`. |
| Tablero y circuito de mensajería | Mostrar/actualizar progreso por bulto aun si el resumen no cambia. Comprobar 1/3 → 2/3 con Worker, RabbitMQ y Valkey reales, además de la prueba de consumidor local. | Decisión, pendiente 7; Seguimiento. |
| Aceptación y actualización | Registrar escenario docente publicado con teléfono sin conexión, evidencia, conflicto, reenvío, backoffice, público y receptor v2/v1; comprobar aislamiento de evidencia nueva y actualización desde datos/cola anteriores. | Prueba manual RF 32 y ADR-0004; equipo; objetivo de demostración 12/11. |
| Captura completa y documentación | Implementar D9, confirmado por Lucas: persistir cifrados firma/foto/textos antes de confirmar, recuperar por identidad y alcance, invalidar firma al cambiar entregados y verificar concurrencia/cuota. La advertencia anterior no sustituye esa recuperación. Actualizar las partes históricas de modelo/casos de uso conforme se retiren consumidores antiguos. | D9, [especificación RF 32](rf-32/especificacion-tecnica.md) y [verificación anterior](rf-32/verificacion-cierre.md). |

Los diseños de custodia/reservas, concurrencia, devolución, consumidores, eventos/tablero, actualización, liquidación y captura quedan especificados en D1 a D9 del cierre técnico. La [especificación técnica RF 32](rf-32/especificacion-tecnica.md) consolida los contratos de la PWA y distingue D9 del comportamiento anterior. Las pruebas nuevas y la demostración siguen siendo actividades de aceptación: su resultado no puede cerrarse mediante una decisión documental.

### 4.7 Ejecución reproducible de esta revisión

Base: `549c1ae`, más la regresión de caché incluida en esta revisión, compilada al iniciar la ejecución de las 18:35 (America/Montevideo). Durante la suite aparecieron cambios concurrentes de otra tarea en documentación y en `Entrega.razor`, `EntregaService.cs` y `evidencia.js` (18:41); no se modificaron aquí ni están cubiertos por estos resultados. Los resultados corresponden a los artefactos de esta ejecución, no certifican el árbol compartido posterior. Se usaron artefactos separados, sin detener la API de desarrollo ni modificar su base.

```powershell
dotnet build Logistica.slnx --configuration Release --artifacts-path TestResults/adr0007-build --verbosity minimal
dotnet test Logistica.slnx --configuration Release --artifacts-path TestResults/adr0007-build --no-restore --verbosity quiet --logger trx --results-directory TestResults/adr0007-verificacion --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
npm --prefix scripts/tests test
```

**Resultado del 09/11/2026:** compilación con 0 errores y 0 advertencias; 72/72 pruebas JavaScript, 490/490 unitarias, 56/56 de arquitectura y **641/641 de integración** aprobadas, sin omitidas. La suite completa de integración tardó 9 min 3 s e incluye la nueva regresión de caché 1/3 → 2/3. TRX en `TestResults/adr0007-verificacion` y salida .NET en `TestResults/adr0007-verificacion.log` (artefactos locales ignorados por Git). Esta ejecución no incluye medición de cobertura, imágenes Docker de producción, navegador ni cámara física; no equivale a ejecutar todos los jobs de CI ni al escenario docente.

## Referencias e historial

- Letra original: [Laboratorio .NET 2026](Laboratorio__NET_2026.pdf), sección 8.2. Cambio docente: *Cambios de Requerimientos Planificados.pdf*, secciones 2.1 y 2.3, aportado al equipo.
- [Decisión funcional cerrada](decisiones-entrega-parcial.md), [análisis previo y evidencia](revision-cambio-requerimientos-y-aclaraciones.md) y [CU-42](cu-42/README.md).
- [ADR-0001](ADR-0001-estilo-arquitectonico.md), [ADR-0002](ADR-0002-estrategia-de-multitenancy.md), [ADR-0003](ADR-0003-outbox-y-mensajeria.md), [ADR-0004](ADR-0004-resolucion-de-conflictos-de-sincronizacion.md), [ADR-0005](ADR-0005-modo-de-renderizado-de-blazor.md) y [ADR-0006](ADR-0006-estrategia-de-cache.md).

Este registro es el ADR-0007 del catálogo numerado actual. El documento docente permite contarlo entre los ocho exigidos; su evaluación retrospectiva queda registrada para la base revisada, sin sustituir la aceptación funcional pendiente.

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
| 1.9 | 09/11/2026 | Aporte retrospectivo RF 31/33/35 registrado en `d698eb7`, incorporado y contrastado en esta revisión. | Cristian Reyes |
| 2.0 | 09/11/2026 | Cierre documental sobre `549c1ae`: matriz de evidencia actualizada, retrospectiva de todos los componentes, diferencias respecto del diseño, deudas explícitas y verificación reproducible con regresión de caché 1/3 → 2/3. | Revisión asistida por Codex |
| 2.1 | 09/11/2026 | Cierre técnico de las deudas a pedido de Lucas, aceptación de la función ordenada de resumen y guía de estudio/defensa. Las decisiones objetivo no se presentan como implementación existente. | Revisión asistida por Codex |
| 2.2 | 09/11/2026 | Lucas confirma UYU/redondeo, cobro de base al cierre mixto elegible y recargos separados, y persistencia completa cifrada antes de confirmar. D1 a D9 cierran las decisiones del alcance; implementación y aceptación conservan sus pendientes. D9 sustituye la elección anterior de captura sólo en memoria. | Lucas Ottonello, registro asistido por Codex |
| 2.3 | 09/11/2026 | Lucas ajusta D8 a importes finales en pesos UYU enteros, sin centésimos. Se concreta redondeo al peso más cercano, con 0,50 hacia arriba y una sola normalización por concepto. Sustituye el criterio de dos decimales/redondeo al par. | Lucas Ottonello, registro asistido por Codex |
| 2.4 | 10/11/2026 | Integración de main: se conserva el aporte retrospectivo RF 31/33/35 de Cristian junto con la revisión posterior y D1–D9. Se enlaza la aceptación integrada de RF 32 del 10/11, sin presentar D9 ni las demás deudas como implementadas. | Lucas Ottonello, integración asistida por Codex |
| 2.5 | 10/11/2026 | Integración de marce: devolución de un bulto suelto al comercio implementada (CU-31), en la matriz de 4.4 y en las deudas de RF 34. | Ezequiel Marcenal |
