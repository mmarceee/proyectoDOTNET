# RF 34 · Bultos pendientes en una nueva hoja de ruta

Revisión: 10/11/2026 (America/Montevideo). Base revisada: `04ee4a4`, más los cambios de esta tarea en el árbol de trabajo. Implementación asistida por Codex solicitada por Lucas. El responsable de RF 34 sigue siendo Lucas como propuesta; este registro no formaliza una asignación del equipo.

## Autoridad y alcance

La letra original conserva RF 13–19 y sus restricciones de asignación, concurrencia, capacidad y carga. El documento docente de cambios, §2.1, incorpora independencia por bulto, reglas versionadas y RF 34. La interpretación vigente está cerrada en [decisiones, §4.2 y §6.3](../decisiones-entrega-parcial.md) y [ADR-0007 / D1–D7](../anexo-tecnico-ADR-0007-entrega-parcial.md). Esta implementación aplica esas decisiones; no redefine disponibilidad como `Reprogramado` ni delega pertenencia a CU-42.

Se implementan reserva, custodia y reasignación individual; selección y capacidad; carga; cierre de la reserva de los entregados; retorno físico al depósito y rendición. La devolución final de subconjuntos al comercio es el trabajo separado de D3/CU-31: retornar al depósito no declara un bulto `Devuelto`. Tampoco se amplían el extravío individual, las transferencias entre vehículos, D8 ni D9.

## Propiedad y persistencia

| Propietario | Modelo implementado |
| --- | --- |
| Planificación | `ReservaBulto`: operador, bulto, envío, ruta, parada, apertura/cierre, causa, operación, responsable y revisión. Índice único parcial `UX_ReservasBulto_Activa` sobre operador/bulto con cierre nulo. `OperacionBultos` conserva huella y resultado de crear, agregar o quitar. |
| Envíos | El estado operativo permanece en `Bulto`. La custodia guarda ubicación, ruta, responsable, operación, instante UTC y revisión; `MovimientoCustodia` conserva cada confirmación o discrepancia. El plazo de reintento también se conserva por bulto. |
| Depósito | `OperacionCustodia` conserva huella y resultado de conciliación, cierre de rendición y recuperación de no cargados. La confirmación física procede del operario autenticado. |
| Ejecución | Mantiene `OperacionId`, escaneos, evidencia y diagnóstico. Una visita continúa siendo atómica por envío dentro de una parada. |

Las acciones entre módulos se exponen exclusivamente por `IPlanificacionModuleApi` e `IEnviosModuleApi`, conforme al ADR-0001. Los repositorios y registros de idempotencia son internos de cada propietario. No se accede a DbContext ajenos.

No se elimina ni se reutiliza una reserva cerrada. Si se quita y vuelve a agregar el mismo bulto a una ruta, se crea otra reserva. El manifiesto usa la pertenencia vigente y conserva la evidencia histórica.

## Disponibilidad y recorrido

Un bulto se ofrece para otra planificación sólo si está `EnDeposito` o `Reprogramado`, tiene custodia `Deposito` confirmada, no tiene una reserva abierta y cumple compromiso/franja/plazo. No se ofrecen terminales, bultos en devolución ni ubicaciones desconocidas. La exclusividad es global por operador/bulto, no sólo por día.

1. Despacho selecciona envíos y sus bultos. La pantalla permite elegir subconjuntos; desmarcarlos todos produce error y nunca significa asignar todo el envío. Prevalidar no escribe.
2. Crear o agregar revalida los bultos seleccionados bajo el bloqueo por operador, verifica revisiones y capacidad y confirma estados, reservas, revisión de ruta y Outbox en la unidad de trabajo compartida. Un envío puede aportar conjuntos distintos a rutas distintas, sin compartir un bulto. También se puede agregar otro bulto del mismo envío a la ruta planificada.
3. La carga admite únicamente el manifiesto propio y la custodia confirmada. Los escaneados pasan a custodia del repartidor/ruta y conservan su reserva. Se mantienen carga en etapas, inicio explícito y la prohibición de parcial cuando lo determina la versión del envío.
4. La visita considera los bultos en tránsito de esa ruta. Una entrega confirma custodia del destinatario y cierra esa reserva en la misma transacción. Un intento fallido mantiene reserva y custodia hasta el retorno. La visita rechazada no aplica líneas válidas, cargos, evidencia aplicada ni eventos de entrega.
5. Depósito puede conciliar un subconjunto desde `/backoffice/custodia`. Confirmar presencia permite T4/T8 cuando corresponde y cierra sólo sus reservas. Un retorno tras intento conserva `Reprogramado` o `EnDevolucion` y sus intentos. Declarar no localizado registra custodia desconocida y mantiene el bloqueo.
6. La rendición compara bultos individuales. Cierra sólo las reservas de los retornados físicamente; los faltantes quedan con discrepancia e incidencia, aunque la ruta termine. Los no cargados requieren confirmación física explícita. Cierre y recuperación exigen la revisión mostrada.

Hoja y rendición utilizan las reservas de la ruta, no la igualdad entre el estado del bulto y el estado global del envío. Se conservan entregas de la visita actual y se excluyen pertenencias de otras rutas. Un bulto liberado y luego asignado a otra ruta se muestra resuelto/en depósito en la hoja histórica anterior; su nuevo estado no lo habilita para otra visita en la ruta vieja.

La reprogramación pública valida todos los pendientes por bulto, excluye terminales y devolución, respeta custodia/plazos/intentos y retira las reservas de rutas planificadas en la misma transacción. No se utiliza el estado global como autorización para mover un bulto en otro vehículo. El puente global permanece para otros consumidores aún pendientes del ADR-0007; un agregado uniforme corrupto sigue rechazándose, sin relajar guardas para aceptar una visita.

## Contratos y compatibilidad

Las solicitudes de prevalidar/crear/agregar conservan `EnvioIds` e incorporan `BultoIds` y `RevisionesBultos` (diccionario BultoId → revisión de custodia). Crear/agregar/quitar incorporan `OperacionId`. El listado de disponibles incluye el código, estado y revisión de cada bulto ofrecido. La pantalla envía una selección explícita.

Los pedidos antiguos de planificación sin `BultoIds` se adaptan bajo bloqueo al conjunto elegible del envío y se vuelven a validar; la selección confirmada queda persistida en las reservas y en la evidencia de capacidad. Si se suministran bultos, una lista vacía o una revisión faltante/obsoleta se rechaza. La revisión de una ruta existente sigue siendo obligatoria para mutarla.

| Método/ruta | Uso y autorización |
| --- | --- |
| `GET /api/deposito/custodia/{envioId}` | Custodia, reservas y revisiones del envío del operador; sólo operario de depósito. Un envío ajeno responde 404. |
| `POST /api/deposito/custodia/conciliaciones` | Confirmación de un conjunto explícito con operación, ruta/revisión cuando hay reserva y revisión de cada bulto. Actor de la sesión, antiforgery con cookie. |
| `/backoffice/custodia` | Consulta por código y confirmación física por grupos de ruta; permite mantener no localizados bloqueados. |
| `/backoffice/rendiciones/{rutaId}` | Revisión y operación en cierre/recuperación; enlace a conciliación individual. |

El servidor compara pertenencia, estado y custodia antes de modificar. Si falla una línea del comando, se revierte todo el comando. Repetir su identidad y contenido devuelve el resultado persistido; reutilizarla con otro contenido o responsable se rechaza. La cola de la PWA y su schema 3 no se borran ni se reescriben. Las operaciones por envío antiguas conservan la traducción del ADR-0004; la validación de ruta/custodia también se aplica al sincronizarlas.

`PlanificacionBultosActualizada.v1` y `CustodiaBultosConciliada.v1` son hechos internos durables para invalidación de seguimiento y notificación al tablero. No amplían los avisos externos v1/v2. Los eventos de envío de asignación/reintegro sólo se emiten cuando representan el conjunto pendiente completo; el avance individual no se presenta como una entrega completa.

## Migraciones y actualización

- Envíos: `20261110210453_CustodiaBultosRF34`.
- Planificación: `20261110210531_ReservasBultosRF34`.
- Depósito: `20261111005549_IdempotenciaCustodiaRF34` (marca UTC; corresponde al 10/11 local).

La migración deja ubicación desconocida y un registro auditable cuando no hay confirmación suficiente. Sólo reconstruye reservas a partir de un manifiesto/pertenencia inequívocos. Considera también obligaciones sin conciliar de rutas cerradas. Una recepción inicial sin rutas posteriores o una carga inequívoca permiten reconstruir custodia desde esos hechos; no se deduce del mero estado. Duplicidad o falta de manifiesto no eligen una ruta arbitraria ni habilitan planificación. Los historiales, resultados, intentos, firmas/fotos y operaciones offline previos se conservan.

Aplicar según D6: preparar copia representativa, detener escrituras/Worker, respaldar, migrar todos los módulos, revisar invariantes y ambigüedades, desplegar componentes compatibles y reabrir escrituras conservando la cola local. Una ruta histórica sin pertenencia inequívoca requiere revisión operativa antes de habilitarla: no se resuelve borrando la cola ni fabricando un manifiesto. El retorno al respaldo sólo es una opción antes de admitir nuevas escrituras; después se corrige hacia adelante.

Las tres migraciones se aplicaron a las bases local y LAN tras ensayarlas sobre copias de sus respaldos. API y Worker se reconstruyeron y reiniciaron en ambos ambientes; también se publicó e inició la API nativa local. Véase el [registro del despliegue](despliegue-2026-11-10.md).

## Verificación y aceptación

La evidencia automatizada se registra en [verificacion.md](verificacion.md). Incluye concurrencia real PostgreSQL, última defensa del índice, selección/capacidad de subconjuntos, revisiones, idempotencia, retorno y bloqueo tras cierre, atomicidad, permisos/antiforgery/tenant, migración y regresiones. El ensayo distribuido usa hosts distintos de API y Worker, RabbitMQ y Valkey reales.

La aceptación RF 32 ya documentada conserva su alcance. Esta implementación requiere su propio ensayo publicado de RF 34: localizar un faltante, confirmar retorno/presencia, planificarlo en otra ruta y comprobar ambas hojas y su sincronización. El despliegue verificado no acredita esa operación física ni los recorridos separados de D3 y D8.
