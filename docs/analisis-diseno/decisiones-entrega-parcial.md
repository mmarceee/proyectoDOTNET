# Decisión cerrada · Entrega parcial de bultos (RF 31 a 36)

| Dato | Valor |
| --- | --- |
| Estado | **Aceptada: decisión funcional cerrada. Implementación pendiente.** |
| Versión y fecha | 1.0 · 02/11/2026 |
| Responsable del registro | Lucas Ottonello |
| Equipo | Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| Alcance del acuerdo | Decisiones comunicadas por el equipo en el documento de Cristian y definición de resumen del envío y reglas confirmadas por Lucas en este intercambio. No registra firmas ni aprobaciones individuales adicionales. |
| ADR relacionado | [ADR-0007 · Adaptación a entrega parcial](ADR-0007-adaptacion-a-entrega-parcial.md) |

## 1. Acuerdo para compartir con el equipo

**Los estados operativos pertenecen a los bultos. El envío conserva un resumen derivado: Creado, En operación, Entregado parcialmente o Entregado. Además indica si está Terminado y muestra el desglose de sus bultos.**

«En operación» reemplaza al nombre propuesto «En tránsito» para el resumen del envío: permite representar también reintentos, reintegros y devoluciones sin afirmar que los bultos estén viajando. «En tránsito» sigue siendo un estado operativo preciso del bulto.

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
| Ninguno entregado y ejecución todavía no iniciada | Creado; si está terminado sin entrega, se presenta el resultado final en lugar de mostrar sólo «Creado». |
| Ninguno entregado y ejecución iniciada | En operación; si está terminado sin entrega, se presenta el resultado final en lugar de mostrar sólo «En operación». |

**La presentación de un cierre sin entrega es «Finalizado sin entrega» más el desglose, no un quinto estado persistido.** Un envío con todos sus bultos devueltos no se presenta como una operación activa ni como entregado.

La distinción exacta entre preparación e inicio de ejecución debe concretarse en la matriz técnica con evidencia propia de los bultos. Sus estados actuales pueden coincidir antes y después de una salida, por ejemplo EnDeposito tras un reintegro. No se debe retroceder de En operación a Creado por esa ambigüedad: hace falta fijar el hecho de inicio o la evidencia de su ciclo operativo que usará la derivación. Esta precisión está pendiente y no reabre los cuatro nombres acordados.

## 3. Ejemplos de aceptación del acuerdo

| Bultos | Resumen | Terminado | Presentación esperada |
| --- | --- | --- | --- |
| Tres admitidos, sin iniciar ejecución | Creado | No | Creado · 0 de 3 entregados. |
| Dos en tránsito y uno pendiente de entrega | En operación | No | En operación · 0 de 3 entregados; detalle de cada bulto. |
| Dos entregados y uno reprogramado | Entregado parcialmente | No | Entregado parcialmente · 2 de 3 entregados · En curso. |
| Dos entregados y uno devuelto | Entregado parcialmente | Sí | Entregado parcialmente · 2 de 3 entregados · Finalizado; 1 devuelto. |
| Uno entregado y dos extraviados | Entregado parcialmente | Sí | Entregado parcialmente · 1 de 3 entregados · Finalizado; 2 extraviados. |
| Tres entregados | Entregado | Sí | Entregado · 3 de 3 entregados · Finalizado. |
| Dos devueltos y uno extraviado | Sin entrega; resumen según su ciclo de ejecución | Sí | Finalizado sin entrega · 0 de 3 entregados · 2 devueltos y 1 extraviado. |
| Todos cancelados antes de iniciar ejecución | Creado, con cierre por cancelación | Sí | Finalizado sin entrega · Cancelado · 0 de N entregados. |

«En curso» en esta tabla es un texto de presentación para Terminado = falso; no agrega un estado de envío ni reemplaza EstadoRuta.EnCurso.

## 4. Visita, resultados y evidencia

- Se registra una visita **por envío dentro de la parada**. Una parada puede agrupar varios envíos.
- Cada visita identifica el resultado de cada bulto de su alcance; la firma y la evidencia referencian explícitamente los bultos entregados. La foto complementa la firma cuando las reglas la exigen; no elimina la firma obligatoria del flujo de entrega existente.
- Se mantiene atomicidad por visita de un envío: si falla una línea, se rechaza la visita completa y se conserva evidencia y diagnóstico. Otros envíos de la misma parada se procesan independientemente.
- Se conserva OperacionId como identificador idempotente. Un reenvío no duplica resultados, evidencia aplicada, eventos, avisos ni recargos.
- Una visita sin entregas registra su intento fallido y los pasos transitorios de los bultos. **No fuerza el resumen del envío a NoEntregado**: si hubo entregas anteriores, el resumen sigue siendo Entregado parcialmente. Debe adaptarse la semántica anterior de T7/T9/T10 a este nivel, no reproducirse un historial de envío que contradiga su derivación.

## 5. Reglas del operador acordadas (RF 33)

| Tema | Decisión |
| --- | --- |
| No admite entrega parcial | Entrega completa del conjunto del envío que corresponde entregar, o ninguna. Entregar sólo «todos los cargados» no basta si falta otro bulto. Validar también en carga para evitar una salida que ya se sabe incompletable, sin bloquear otros envíos independientes de la ruta. |
| Pendientes | Nueva visita sin recargo, nueva visita con recargo o devolución, según reglas configurables y versionadas del operador. |
| Motivo no reprogramable | El bulto pasa al circuito de devolución. |
| Máximo de intentos | Por bulto y por intento real fallido. Cargar, reasignar o reintegrar sin intentar no consume intentos. Al agotarse no se generan visitas automáticas ilimitadas: devolución o revisión según la política definida. |
| Versión de reglas | Se conserva la del envío para las revisitas; no se toma silenciosamente la versión vigente al sincronizar. |
| Recargo de revisita | Monto fijo por visita adicional de un envío efectivamente realizada, aunque falle, registrado una sola vez. No se cobra al planificar, por cada bulto ni por reenviar la sincronización. Liquidación consume el cargo registrado, no lo recalcula con tarifas futuras. |

La estructura monetaria, moneda, redondeo, clave de unicidad del cargo y contrato con liquidaciones deben concretarse al implementar esta decisión. No constituyen autorización para duplicar el precio base del envío.

## 6. Qué queda pendiente de especificación o implementación

La decisión funcional anterior está cerrada. **No equivale a declarar terminada la especificación técnica de RF 31 a 36.** Falta:

1. Tabla completa y verificable de transiciones por bulto y de cambios del resumen del envío, guardas y criterio de inicio de ejecución. Definir los pares que son inalcanzables y los hechos de visita que no cambian el resumen.
2. Contratos de resultados por bulto, escaneo, faltantes no escaneables y evidencia por motivo. Política de carga incompleta para operadores que sí admiten parcial; custodia física, rendición y disponibilidad de faltantes.
3. Exclusividad de asignación activa por bulto y liberación de reservas: decidir cuándo un pendiente puede entrar en otra ruta sin asignarlo simultáneamente a dos. CU-42 ordena las visitas resultantes y no decide esta pertenencia.
4. Confirmación del alcance de extravío individual y de sus autorizaciones. El resumen ya contempla bultos extraviados, pero la operación que los declara debe especificarse.
5. Migración de datos existentes y operaciones offline antiguas. Usar recepción/carga histórica cuando exista; no inferir automáticamente una lista de bultos que el dispositivo nunca envió si es ambigua.
6. Avisos v1/v2: eventos, selección de versión, traducción compatible para v1, firma del payload, reintentos y reenvíos con la misma versión. Un receptor v1 no debe recibir enums desconocidos ni darse por enterado de una entrega completa que no ocurrió.
7. Invalidación de seguimiento y actualización del tablero por cambios de bulto, incluso cuando el resumen siga siendo Entregado parcialmente (por ejemplo, de 1/3 a 2/3).
8. Migración de consultas y consumidores que hoy deciden por EstadoEnvio: retirar esa dependencia de las decisiones operativas. Actualizar dominio, casos de uso y ADR-0004/0006 con la implementación real.

## 7. Referencias y vigencia

- Documento docente *Cambios de Requerimientos Planificados.pdf*, secciones 2.1 (RF 31 a 36) y 2.3 (demostración y ADR del cambio). Para el equipo de tres no corresponde el retiro en domicilio de RF 37 a 41.
- Documento de Cristian *Entrega parcial de bultos: lo acordado y lo que falta decidir*, del 02/11/2026, y acuerdos posteriores confirmados por Lucas.
- [ADR-0007](ADR-0007-adaptacion-a-entrega-parcial.md), [modelo de dominio](modelo-de-dominio.md), [tabla de transiciones](tabla-de-transiciones-de-estados.md), [casos de uso](casos-de-uso.md) e [informe previo](revision-cambio-requerimientos-y-aclaraciones.md).

Este documento reemplaza las propuestas anteriores de derivar el envío con todos los estados físicos del bulto, elegir «el más atrasado/más avanzado» y usar En tránsito como resumen general. Los documentos históricos conservan evidencia del diseño anterior y no deben interpretarse como especificación final de esta decisión.

| Versión | Fecha | Cambio | Responsable |
| --- | --- | --- | --- |
| 1.0 | 02/11/2026 | Registro de la decisión cerrada y separación de pendientes técnicos. | Lucas Ottonello |
