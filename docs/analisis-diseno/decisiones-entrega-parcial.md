# Decisión cerrada · Entrega parcial de bultos (RF 31 a 36)

| Dato | Valor |
| --- | --- |
| Estado | **Aceptada: decisión funcional cerrada. Implementación parcial; evidencia y pendientes en ADR-0007, secciones 4.4 a 4.7.** |
| Versión y fecha | 1.8 · 09/11/2026 (D8 ajustado a pesos enteros) |
| Responsable del registro | Lucas Ottonello |
| Equipo | Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| Alcance del acuerdo | Decisiones comunicadas por el equipo en el documento de Cristian y definición de resumen del envío y reglas confirmadas por Lucas. La ampliación del 08/11 sobre alcance de visita, foto compartida, faltantes y reservas fue confirmada por Lucas en el chat; no registra aprobaciones individuales adicionales de otros integrantes. |
| ADR relacionado | [ADR-0007 · Adaptación a entrega parcial](ADR-0007-adaptacion-a-entrega-parcial.md) |

## 1. Acuerdo para compartir con el equipo

**Los estados operativos pertenecen a los bultos. El envío conserva un resumen derivado: Creado, En operación, Entregado parcialmente o Entregado. Además indica si está Terminado y muestra el desglose de sus bultos.**

«En operación» reemplaza al nombre propuesto «En tránsito» para el resumen del envío: permite representar también recepción incompleta, preparación, reintentos, reintegros y devoluciones sin afirmar que los bultos estén viajando. «En tránsito» sigue siendo un estado operativo preciso del bulto.

«Entregado parcialmente» puede ser una situación abierta o el resultado definitivo. Si los bultos restantes se devuelven o se extravían, no se convierte el envío en Entregado.

Se mantiene el monolito modular y el agregado Envio como coordinador. Esta decisión no requiere convertir Bulto en un agregado independiente ni crear nuevos servicios.

## 2. Estado del bulto y resumen del envío

| Nivel | Información | Regla |
| --- | --- | --- |
| Bulto | Admitido, EnDeposito, AsignadoARuta, EnTransito, NoEntregado, Reprogramado, EnDevolucion, Entregado, Devuelto, Extraviado y Cancelado, según las transiciones que apliquen. | Se reutiliza el vocabulario existente; no se introduce un estado persistente adicional llamado Pendiente. La tabla por bulto debe explicitar acciones y guardas. |
| Envío | Creado, En operación, Entregado parcialmente, Entregado. | Resumen calculado; ningún endpoint ni usuario lo asigna directamente. |
| Envío | Terminado. | Verdadero si todos los bultos están en estados terminales: Entregado, Devuelto, Extraviado o Cancelado. El conjunto debe quedar declarado, no deducirse por el nombre del estado. |
| Envío | Entregados / total y desglose por resultado. | El contador siempre mide entregas efectivas. El total conserva todos los bultos originales, incluidos devueltos, extraviados y cancelados. |

El envío guarda su resumen recalculado para poder consultar y filtrar sin reconstruirlo en cada lectura. Se actualizan bultos, resumen, evidencia y Outbox correspondiente en la misma transacción. La derivación se ejecuta una sola vez al final de la operación, no después de modificar cada línea.

El esquema lógico acordado es:

| Condición | Resumen y presentación |
| --- | --- |
| Todos entregados | Entregado; Terminado = sí; N/N entregados. |
| Alguno entregado y menos que el total | Entregado parcialmente; Terminado depende de los restantes. |
| Ningún bulto recibido | Creado; si está terminado sin entrega, se presenta el resultado final en lugar de mostrar sólo «Creado». |
| Al menos un bulto recibido, ninguno entregado | En operación; si está terminado sin entrega, se presenta el resultado final en lugar de mostrar sólo «En operación». |

**La presentación de un cierre sin entrega es «Finalizado sin entrega» más el desglose, no un quinto estado persistido.** Un envío con todos sus bultos devueltos no se presenta como una operación activa ni como entregado.

**La ejecución se inicia con la recepción en depósito del primer bulto.** Crear el envío en el portal no la inicia: lo deja en Creado. Dentro de En operación, el desglose distingue recepción incompleta, preparación, tránsito, reintento o devolución.

La derivación no necesita un dato adicional de inicio: un bulto en un estado distinto de Admitido o Cancelado ya fue recibido, y ninguna transición vuelve a Admitido, de modo que un reintegro no devuelve el envío a Creado. Cancelado no indica si el bulto se había recibido; por eso el recálculo nunca baja de En operación a Creado.

Como el resumen cambia con el primer bulto, cada escaneo de recepción actualiza el estado de su bulto y recalcula el envío. La condición de recibir un bulto se valida contra ese bulto, no contra el resumen.

## 3. Ejemplos de aceptación del acuerdo

| Bultos | Resumen | Terminado | Presentación esperada |
| --- | --- | --- | --- |
| Tres admitidos, ninguno recibido | Creado | No | Creado · 0 de 3 entregados. |
| Uno en depósito y dos admitidos | En operación | No | En operación · 0 de 3 entregados; recepción incompleta. |
| Dos en tránsito y uno pendiente de entrega | En operación | No | En operación · 0 de 3 entregados; detalle de cada bulto. |
| Dos entregados y uno reprogramado | Entregado parcialmente | No | Entregado parcialmente · 2 de 3 entregados · En curso. |
| Dos entregados y uno devuelto | Entregado parcialmente | Sí | Entregado parcialmente · 2 de 3 entregados · Finalizado; 1 devuelto. |
| Uno entregado y dos extraviados | Entregado parcialmente | Sí | Entregado parcialmente · 1 de 3 entregados · Finalizado; 2 extraviados. |
| Tres entregados | Entregado | Sí | Entregado · 3 de 3 entregados · Finalizado. |
| Dos devueltos y uno extraviado | En operación | Sí | Finalizado sin entrega · 0 de 3 entregados · 2 devueltos y 1 extraviado. |
| Todos cancelados antes de recibir ningún bulto | Creado, con cierre por cancelación | Sí | Finalizado sin entrega · Cancelado · 0 de N entregados. |
| Todos cancelados después de recibir alguno | En operación, con cierre por cancelación | Sí | **Inalcanzable** (aprobado el 03/11/2026): T17 exige que ningún bulto se haya recibido, así que un bulto recibido ya no se cancela. Se conserva como referencia del cálculo y no tiene prueba de aceptación. |

«En curso» en esta tabla es un texto de presentación para Terminado = falso; no agrega un estado de envío ni reemplaza EstadoRuta.EnCurso.

## 4. Visita, resultados y evidencia

- Se registra una visita **por envío dentro de la parada**. Una parada puede agrupar varios envíos.
- Cada visita identifica el resultado de cada bulto de su alcance. La firma referencia explícitamente los bultos entregados. Una única foto de presencia en el lugar puede respaldar los resultados entregados y no entregados de esa visita; complementa la firma cuando las reglas la exigen y no elimina su obligatoriedad para una entrega. La asociación y el alcance de ambas pruebas se precisan en 4.1.
- Se mantiene atomicidad por visita de un envío: si falla una línea, se rechaza la visita completa y se conserva evidencia y diagnóstico. Otros envíos de la misma parada se procesan independientemente.
- Se conserva OperacionId como identificador idempotente. Un reenvío no duplica resultados, evidencia aplicada, eventos, avisos ni recargos.
- Una visita sin entregas registra su intento fallido y los pasos transitorios de los bultos. **No fuerza el resumen del envío a NoEntregado**: si hubo entregas anteriores, el resumen sigue siendo Entregado parcialmente. Debe adaptarse la semántica anterior de T7/T9/T10 a este nivel, no reproducirse un historial de envío que contradiga su derivación.

### 4.1 Alcance de visita y foto compartida · confirmado por Lucas el 08/11/2026

- La visita registra resultados de los bultos que viajaron y corresponden a ese envío en esa visita. La PWA distingue los cargados, los que quedaron en depósito y los ya resueltos. Un bulto que no salió no recibe un motivo de intento fallido en domicilio ni consume un intento por esa visita.
- **Se utiliza una sola foto por visita como evidencia de presencia en el lugar**, tanto si se entrega todo, nada o una parte. Puede ser referenciada por los distintos resultados de esa misma visita. No se exige una foto de entrega y otra foto adicional de rechazo. La obligatoriedad de aportar foto continúa determinada por las reglas del envío y los motivos seleccionados.
- La firma respalda exclusivamente el conjunto efectivamente entregado, mostrado antes de firmar. Si ese conjunto cambia después de la firma, se debe volver a firmar. Una visita sin entregas no exige firma de recepción.
- Ejemplo: una primera visita entrega los bultos 1 y 2 y usa la firma A y la foto A. Si el bulto 3 viajó pero no se entregó, conserva su motivo y puede compartir la foto A como evidencia de esa visita, sin quedar incluido en la firma. Si el tercero quedó en depósito, no forma parte de sus resultados. Cuando se entregue en una visita posterior, se registran firma B y foto B cuando corresponda, sin sobrescribir las pruebas anteriores.
- El modelo permite vincular cada resultado a sus BultoIds y referenciar la misma foto desde la prueba exitosa y los intentos fallidos. Esta decisión no exige dividir el campo Foto en fotos independientes de entrega y rechazo. Al 09/11 la PWA y la integración por bulto están implementadas y cubiertas por `Rf32PwaTests` y las pruebas JavaScript; falta la aceptación del circuito publicado en teléfono (ADR-0007, 4.6).

### 4.2 Faltantes de carga y exclusividad · confirmado por Lucas el 08/11/2026

Se contempla carga de parte de los bultos cuando el operador admite parcial, como prevé el contexto docente de 2.1 y T5 de la tabla del equipo. Si no la admite y falta un bulto necesario para completar la entrega, ese envío no sale incompleto; otros envíos independientes pueden continuar. En una revisita no se vuelve a exigir cargar los ya entregados.

El faltante se informa al confirmar la carga, con un motivo de carga, y depósito verifica la ubicación. No se interpreta automáticamente la ausencia de escaneo como prueba de custodia en depósito.

| Situación | Disponibilidad y reserva |
| --- | --- |
| Bulto cargado | Conserva su asignación activa a la ruta y pasa a EnTransito. |
| No cargado y confirmado en depósito | Vuelve a EnDeposito, se cierra su asignación a la ruta y queda disponible para otra planificación. |
| No localizado | Se registra la discrepancia y permanece bloqueado para otra asignación hasta conciliación por depósito. No se declara Extraviado automáticamente. |
| Entregado | Se cierra su asignación activa, conservando el historial. |
| Viajó y no se entregó | No queda disponible para otro vehículo mientras conserve la custodia el repartidor. Se libera para nueva planificación al confirmar retorno o transferencia autorizada. El estado Reprogramado por sí solo no acredita disponibilidad física. |

Existe como máximo una asignación activa por bulto, con protección frente a planificación concurrente. Estados y reservas se confirman juntos en el servidor. La PWA puede conservar la carga pendiente sin red, pero no libera una reserva global por una decisión exclusivamente local. No se borra el historial de asignaciones.

Un faltante de carga no consume un intento de entrega ni genera un recargo al confirmar la carga. La eventual visita posterior se trata según RF 33 y la versión de reglas del envío. Estas decisiones funcionales requieren concretar los contratos, motivos de carga, conciliación de custodia y controles de concurrencia; no afirman que el código actual ya las implemente.

## 5. Reglas del operador acordadas (RF 33)

| Tema | Decisión |
| --- | --- |
| No admite entrega parcial | Entrega completa del conjunto del envío que corresponde entregar, o ninguna. Entregar sólo «todos los cargados» no basta si falta otro bulto. Validar también en carga para evitar una salida que ya se sabe incompletable, sin bloquear otros envíos independientes de la ruta. |
| Pendientes | Nueva visita sin recargo, nueva visita con recargo o devolución, según reglas configurables y versionadas del operador. |
| Motivo no reprogramable | El bulto pasa al circuito de devolución. |
| Máximo de intentos | Por bulto y por intento real fallido. Cargar, reasignar o reintegrar sin intentar no consume intentos. Una visita rechazada al sincronizar tampoco los consume (ADR-0004, sección 2.11). Al agotarse no se generan visitas automáticas ilimitadas. **Decidido el 03/11/2026:** se conserva la política de devolución que ya existe en la versión de reglas (`DevolverTrasIntentosAgotados`). Con sí, el bulto pasa a devolución (T10); con no, queda Reprogramado y decide el despachador (CU-19, A2, y CU-20). Esa decisión del despachador es la «revisión»; quitar la opción obligaba a rehacer dos casos ya probados. |
| Entrega parcial en versiones ya publicadas | **Decidido el 03/11/2026:** `AdmiteEntregaParcial` vale falso en las versiones de reglas publicadas antes del cambio, que se comportan como hasta ahora (todo o nada). Administración permite configurarlo en las versiones nuevas (RF 33). |
| Versión de reglas | Se conserva la del envío para las revisitas; no se toma silenciosamente la versión vigente al sincronizar. |
| Recargo de revisita | Monto fijo por visita adicional de un envío efectivamente realizada, aunque falle, registrado una sola vez. No se cobra al planificar, por cada bulto ni por reenviar la sincronización. Liquidación consume el cargo registrado, no lo recalcula con tarifas futuras. |

**Cierre confirmado por Lucas el 09/11/2026:** UYU en pesos enteros, sin centésimos, con redondeo al peso más cercano y 0,50 hacia arriba. La base se cobra una sola vez al terminar todos los bultos si al menos uno fue entregado o devuelto; se excluye si todos quedaron cancelados/extraviados. Los recargos por visitas realizadas se liquidan aparte. D8 del [cierre técnico](cierre-tecnico-entrega-parcial.md) define identidad de visita, unicidad por concepto, períodos, borradores y totales, sin duplicar la base. La decisión está cerrada; implementar el contrato sigue pendiente.

**Captura confirmada por Lucas el 09/11/2026:** conservar cifrados también firma, foto, receptor y observaciones antes de confirmar, recuperarlos al reabrir y exigir nueva firma si cambia el conjunto entregado (D9). Esta confirmación posterior sustituye la opción anterior de conservar evidencia sólo en pantalla y advertir al salir; su implementación debe verificarse antes de aceptar recuperación completa.

## 6. Qué queda pendiente de especificación o implementación

La decisión funcional anterior se complementa con el [cierre técnico del ADR-0007](cierre-tecnico-entrega-parcial.md). D1 a D9 especifican custodia, reservas, concurrencia, devolución, consumidores, progreso, migración, aceptación, liquidación y recuperación de captura. Los pendientes siguientes registran avance e implementación; no autorizan decisiones contradictorias con ese cierre.

**Avance del 08/11/2026:** [RF 32 en la PWA](rf-32/README.md) implementa visita por bulto, foto única, guardado cifrado, diagnóstico de rechazos y adaptaciones de carga/hoja/rendición. La conciliación de custodia y las reservas/reasignación por bulto siguen siendo pendientes de RF 34; el avance no los da por implementados.

1. ~~Tabla completa y verificable de transiciones por bulto y de cambios del resumen del envío, y sus guardas.~~ **Resuelto el 03/11/2026:** el equipo aprobó la [tabla por bulto](tabla-de-transiciones-de-estados.md#entrega-parcial-transiciones-por-bulto-y-resumen-del-envío) de Cristian, con sus pares inalcanzables y los hechos que no cambian el resumen. También aprobó mantener T17 y tratar como inalcanzable el ejemplo «Todos cancelados después de recibir alguno» (sección 3).
2. Completar contratos e implementación de resultados por bulto, escaneo, faltantes no escaneables y evidencia. **Definición funcional precisada por Lucas el 08/11/2026 en 4.1 y 4.2:** alcance de la visita sobre los que viajaron, una foto compartida por visita, firma de los entregados y tratamiento del faltante según custodia. **Avance al 09/11:** motivos de carga, hoja schema 3, almacenamiento cifrado y rendición por bulto están implementados (README RF 32 y ADR-0007, 4.4). Quedan la conciliación individual de ubicación, la devolución de un bulto suelto y la aceptación integrada; recuperar envíos completos no cargados exige confirmación física y no completa la custodia individual.
3. Implementar exclusividad de asignación activa por bulto y liberación de reservas según 4.2. **Diseño especificado el 09/11 en D1/D2:** reserva propia de Planificación, índice único parcial por operador/bulto, bloqueo y revisión, custodia confirmada y transacción con estado/Outbox. **Criterio funcional confirmado por Lucas el 08/11/2026:** sólo se habilita nueva asignación con disponibilidad física confirmada; Reprogramado no basta. CU-42 ordena las visitas resultantes y no decide esta pertenencia.
4. ~~Confirmación del alcance de extravío individual y de sus autorizaciones.~~ **Resuelto el 03/11/2026:** para el 12/11 el extravío sigue siendo del envío entero (CU-21). Pasan a Extraviado los bultos que acompañan al envío, es decir, los que están en su mismo estado; los ya entregados no cambian. El extravío de un bulto suelto queda para después. Con eso, una visita sobre un envío extraviado cae entera en conflicto (ADR-0004, secciones 2.11 y 4.2).
5. ~~Migración de datos existentes y operaciones offline antiguas.~~ **Resuelto el 03/11/2026:**
   - **Datos.** La migración `EntregaParcialBultos` copia a cada bulto el estado de su envío y sus intentos fallidos, y conserva las recepciones parciales ya escaneadas. No inventa historial por bulto.
   - **Operaciones sin conexión antiguas.** Una entrega o un intento fallido por envío que llegue desde la cola de la PWA se traduce a una visita con el mismo resultado para todos los bultos del envío, que es lo que significaban antes de RF 31. Si algún bulto ya no está en tránsito, la operación se rechaza como cualquier visita, sin inventar un resultado (ADR-0004, sección 2.11).
   - **PWA.** La versión nueva envía sólo visitas (Lucas).
6. ~~Avisos v1/v2: eventos, selección de versión, traducción compatible para v1, firma del payload, reintentos y reenvíos con la misma versión. Un receptor v1 no debe recibir enums desconocidos ni darse por enterado de una entrega completa que no ocurrió.~~ **Resuelto el 09/11/2026 (RF 36):**
   - La versión es de la suscripción, se elige al crearla y no cambia; las existentes son v1.
   - Envíos publica `BultoEntregado`, `BultoNoEntregado` y `BultoDevuelto`, sólo para v2. `BultoExtraviado` espera el extravío individual (pendiente 4).
   - v1 recibe el mismo contenido de siempre, sin valores nuevos. `EnvioEntregado` sale sólo con todos los bultos entregados.
   - **Cierre mixto** (devuelto con bultos ya entregados): v1 no lo recibe, para no darle un estado final falso. Para verlo, el comercio pasa a v2.
   - La entrega guarda versión y bulto: reintentos y reenvíos mandan el mismo cuerpo, con la misma firma.
   - Contrato en [integracion-cu64.md](cu-63/integracion-cu64.md#mensaje-v2-rf-36).
7. Invalidación de seguimiento y actualización del tablero por cambios de bulto, incluso cuando el resumen siga siendo Entregado parcialmente (por ejemplo, de 1/3 a 2/3). **Avance al 09/11:** `InvalidarSeguimiento` consume eventos de bulto; la regresión 1/3 → 2/3 está incluida en la revisión del ADR-0007. Siguen pendientes el tablero (no está suscrito a esos eventos) y el ensayo con Worker/RabbitMQ/Valkey reales; la prueba local de consumidor no acredita ese circuito completo.
8. Migración de consultas y consumidores que hoy deciden por EstadoEnvio: retirar esa dependencia de las decisiones operativas. Actualizar dominio, casos de uso y ADR-0004/0006 con la implementación real.
   - **Avance del 09/11/2026 (nueva visita, RF 34):** planificación y capacidad usan los bultos que acompañan al envío (`Envio.BultosQueAcompanan`, la regla del puente de `Transicionar`), excluyendo los ya entregados. Carga usa `BultosParaCarga`: además conserva los todavía AsignadoARuta si el primer lote dejó el envío EnTransito, para permitir continuar antes de Comenzar ruta. La lectura base de la hoja conserva todos los estados y Ejecución selecciona el manifiesto de la ruta, compartido con la rendición: mantiene entregados en esta visita y omite entregados anteriores. La rendición conserva su consulta separada completa. Se comprueba con `NuevaVisitaTests`, `IniciarRendicionTests` y `Rf32PwaTests`, incluida carga en dos etapas y revisita con recargo idempotente.
   - **Para el 15/11:** envíos activos de un comercio (`EnviosDeComercio`, por `Terminado`), envíos liquidables (CU-32), reprogramación desde el seguimiento (CU-61) y textos de la PWA. D4 define el conjunto reprogramable y las guardas por bulto; D5 define el progreso/tablero; D6 fija cómo retirar el puente sin alterar historiales ni colas.

## 7. Referencias y vigencia

- Documento docente *Cambios de Requerimientos Planificados.pdf*, secciones 2.1 (RF 31 a 36) y 2.3 (demostración y ADR del cambio). Para el equipo de tres no corresponde el retiro en domicilio de RF 37 a 41.
- Documento de Cristian *Entrega parcial de bultos: lo acordado y lo que falta decidir*, del 02/11/2026, y acuerdos posteriores confirmados por Lucas.
- [ADR-0007](ADR-0007-adaptacion-a-entrega-parcial.md), [modelo de dominio](modelo-de-dominio.md), [tabla de transiciones](tabla-de-transiciones-de-estados.md), [casos de uso](casos-de-uso.md) e [informe previo](revision-cambio-requerimientos-y-aclaraciones.md).

Este documento reemplaza las propuestas anteriores de derivar el envío con todos los estados físicos del bulto, elegir «el más atrasado/más avanzado» y usar En tránsito como resumen general. Los documentos históricos conservan evidencia del diseño anterior y no deben interpretarse como especificación final de esta decisión.

| Versión | Fecha | Cambio | Responsable |
| --- | --- | --- | --- |
| 1.0 | 02/11/2026 | Registro de la decisión cerrada y separación de pendientes técnicos. | Lucas Ottonello |
| 1.1 | 02/11/2026 | La ejecución se inicia con la recepción del primer bulto; se actualizan el esquema, los ejemplos y el pendiente 1. | Ezequiel Marcenal |
| 1.2 | 03/11/2026 | Decisiones del equipo: tabla por bulto aprobada y ejemplo de cancelación después de recibir declarado inalcanzable (pendiente 1 y sección 3); extravío del envío entero para el 12/11 (pendiente 4); migración y traducción de las operaciones antiguas (pendiente 5); política de intentos agotados y `AdmiteEntregaParcial` falso en las versiones publicadas (sección 5). | Ezequiel Marcenal |
| 1.3 | 08/11/2026 | Confirmación de Lucas: alcance de visita sobre bultos que viajaron, una foto compartida por visita, firma de los entregados, faltantes de carga según custodia y exclusividad/liberación de reservas. Se conserva como pendiente la especificación técnica e implementación. | Lucas Ottonello |
| 1.4 | 09/11/2026 | Integración de avisos v1/v2 (pendiente 6) y lectura de rendición por bulto; conserva los acuerdos de RF 32 sobre inicio de ruta, escaneos que no salieron y confirmación de custodia antes de liberar una asignación. | Lucas Ottonello |
| 1.5 | 09/11/2026 | Pendiente 8: avance para la nueva visita (bultos que acompañan al envío en planificar, hoja de ruta y carga) y lo que queda para el 15/11. | Cristian Reyes |
| 1.6 | 09/11/2026 | Estado parcial explícito y actualización de 4.1 y pendientes 2/7 con la implementación RF 32, invalidación por bulto y límites de aceptación registrados en ADR-0007. No modifica acuerdos funcionales. | Revisión asistida por Codex |
| 1.7 | 09/11/2026 | Lucas confirma moneda/redondeo, base de cierres mixtos y recargos separados, y recuperación completa cifrada de captura sin confirmar. Cierre técnico D1 a D9, con implementación pendiente diferenciada. | Lucas Ottonello, registro asistido por Codex |
| 1.8 | 09/11/2026 | Ajuste de Lucas a pesos UYU enteros sin centésimos; redondeo al peso más cercano, 0,50 hacia arriba y una normalización por concepto, según D8. | Lucas Ottonello, registro asistido por Codex |
