# Anexo técnico del ADR-0007 · Entrega parcial

Fecha: 09/11/2026. Responsable del ADR: Lucas Ottonello. Registro preparado con asistencia de Codex a pedido de Lucas. Complementa la [decisión funcional](decisiones-entrega-parcial.md) sin atribuir nuevas aprobaciones a los demás integrantes. Los nombres de entidades, campos y operaciones **objetivo** de este documento son especificación para implementar, no APIs existentes.

El [ADR-0007](ADR-0007-adaptacion-a-entrega-parcial.md) conserva la evidencia de lo implementado. Este documento resuelve el diseño de las deudas: decidir una solución no equivale a haberla programado o aceptado en el circuito publicado.

## D1. Estado, custodia y reserva son dimensiones diferentes

Envíos es dueño del estado operativo del bulto y del resumen del envío. Planificación es dueña de su asignación a ruta. Depósito confirma físicamente recepción y retorno; Ejecución registra la carga y la custodia del repartidor. La coordinación utiliza contratos y la unidad de trabajo compartida, sin DbContext ajenos.

El modelo objetivo de Planificación incorpora una reserva por `(OperadorId, BultoId)`, vinculada a envío, ruta y parada, con fecha de apertura/cierre, causa y revisión. Un índice único parcial sobre `(OperadorId, BultoId)` donde la fecha de cierre sea nula impide dos reservas abiertas. El historial se conserva al cerrar, no se elimina ni se reutiliza su identidad.

La custodia guarda responsable/ubicación confirmados, instante, actor y referencia de la operación de confirmación. «No localizado» es una discrepancia que bloquea disponibilidad; no añade otro estado a la máquina del bulto ni declara extravío. La consulta de disponibilidad reúne por contrato estado elegible, custodia y ausencia de reserva abierta: ninguna de las tres condiciones basta sola.

| Hecho confirmado | Estado, custodia y reserva objetivo |
| --- | --- |
| Se asigna un bulto disponible | Estado AsignadoARuta; reserva abierta; custodia sigue en depósito hasta cargar. |
| Se carga | Estado EnTransito; custodia del repartidor/ruta; misma reserva. |
| No salió y depósito confirma presencia | T4 a EnDeposito para ese bulto; cerrar reserva; disponible para otra ruta. |
| No se localiza | Conservar bloqueo y registrar discrepancia; no liberar por ausencia de escaneo ni cierre de ruta. |
| Se entrega | Estado Entregado; registrar salida definitiva de custodia operativa y cerrar reserva en la misma transacción. |
| Se intentó y no se entregó | Reprogramado o EnDevolucion; conserva custodia del repartidor y reserva hasta retorno confirmado. |
| Retorna tras intento | Depósito confirma custodia y cierra reserva. Conservar Reprogramado o EnDevolucion: no inventar una transición hacia EnDeposito ni borrar el intento. |
| Vuelve sin intento | Aplicar T8 cuando corresponda, confirmar depósito y cerrar reserva sin sumar intento. |

Una ruta cerrada puede conservar una obligación de conciliación: su cierre administrativo no libera el bulto. No se agrega un flujo de transferencia directa entre vehículos en esta entrega; una futura transferencia exige autorización y confirmación física propias. La reasignación actual pasa por depósito.

## D2. Transacción y concurrencia de planificación

Toda operación de asignar, quitar, cargar o conciliar toma el bloqueo de planificación del operador que ya existe, vuelve a leer estado/custodia/reserva y compara la revisión esperada. El índice único es la última defensa ante concurrencia. Cada comando identifica exactamente sus bultos; si una línea falla, no se modifica ninguna de las líneas de ese comando. Otros comandos independientes pueden continuar.

La transacción confirma reserva, cambios de bulto, custodia, revisión de ruta y Outbox correspondientes. Un comando repetido usa su identificador idempotente y devuelve el resultado anterior. Una revisión obsoleta produce conflicto y obliga a actualizar; no se aplica la última escritura silenciosamente. No se libera una reserva por un hecho únicamente local de la PWA.

Los contratos objetivo permiten consultar disponibilidad y reservar/liberar conjuntos de BultoId desde Planificación; Envíos valida pertenencia y transiciones, y Depósito aporta confirmaciones físicas auditables. Se conservan los permisos de despacho para asignar, depósito para confirmar ubicación y repartidor para su carga/visita. El tenant proviene de sesión/mensaje, nunca del cuerpo como autoridad.

## D3. Visita, devolución y resolución de conflictos

La visita mantiene la atomicidad por envío dentro de una parada decidida en ADR-0004. Contiene exactamente los bultos que viajaron y siguen pendientes de resultado en esa visita. Un rechazo guarda evidencia y diagnóstico y no aplica entregas, intentos, cargo ni eventos de entrega. No se ofrece «aplicar los válidos» ni se fabrica otra visita para sortear el rechazo. La revisión del despachador documenta la discrepancia; no corrige estados terminales sin una transición específica futura.

La devolución individual usa una orden con el conjunto explícito de bultos no terminales elegibles, motivo y autorización existentes. Iniciar la orden aplica T12/T14 sólo a los seleccionados, en una transacción, sin tocar entregados anteriores. La recepción por el comercio puede confirmar un subconjunto de esa orden: aplica T15 a los efectivamente recibidos, registra receptor/evidencia y mantiene pendientes los demás. La atomicidad es la del comando de recepción seleccionado; recibir una parte no declara recibida toda la orden.

Un retorno de custodia al depósito y una devolución final al comercio son operaciones distintas. Todo cambio recalcula una vez el resumen del envío. El extravío individual continúa **fuera del alcance acordado para esta entrega**: se mantiene CU-21 con su alcance aprobado, sin ampliar autorizaciones ni generar una capacidad nueva por omisión.

## D4. Lecturas y reprogramación sin estado global operativo

| Consumidor | Regla objetivo |
| --- | --- |
| Activos de un comercio | `Terminado == false`, siempre filtrado por operador/comercio. Un parcial abierto sigue activo; un cierre mixto desaparece de activos sin presentarse Entregado. |
| Disponibles para planificar | Bultos EnDeposito o Reprogramado elegibles por reglas, con presencia en depósito confirmada, plazo cumplido y sin reserva abierta. Excluir terminales y discrepancias de custodia. |
| Carga/capacidad | Sólo bultos del manifiesto de esa ruta. Capacidad por peso/volumen de ese conjunto; carga en etapas conserva los aún asignados antes de Comenzar ruta. |
| Hoja/rendición | Mismo manifiesto de ruta; mantener entregados de la visita actual y excluir entregados anteriores. El total original del envío sigue disponible como contexto. |
| Público/PWA | Resumen, Terminado y entregados/total para progreso; estado individual para cada bulto; no deducir entregas del estado de compatibilidad. |

CU-61 conserva una fecha/franja de envío: no incorpora agenda independiente por bulto. La solicitud afecta al conjunto de pendientes de entrega, excluyendo entregados, devueltos, extraviados, cancelados y bultos ya en devolución. Se valida el conjunto completo antes de modificarlo: no terminal, no en tránsito, sin reserva despachada/custodia de repartidor o desconocida, anticipación y plazo válidos, y todos sus bultos por debajo del máximo de intentos. Los pendientes Admitidos conservan la excepción previa a recepción. Si alguno no es elegible, se rechaza la solicitud completa; despacho resuelve el caso. Quitar reservas de rutas Planificadas y reprogramar se confirma en la misma transacción.

El estado de compatibilidad se conserva hasta migrar todos los lectores/escritores identificados y las regresiones. No se elimina el enum usado por bultos ni se reescriben historiales o contratos v1 para retirar ese puente.

## D5. Progreso, eventos y compatibilidad

Cada cambio visible de bulto debe tener invalidación durable por Outbox, aunque no cambie el resumen. Los hechos existentes de entrega/no entrega/devolución se reutilizan. Para recepción individual se incorpora un hecho interno de recepción por bulto; el TTL de 60 segundos sigue siendo respaldo, no el mecanismo objetivo de actualización.

Seguimiento invalida la vista pública y notifica al grupo de tablero del operador. El tablero relee una proyección con resumen, Terminado y contadores por resultado, sin reemplazar sus métricas de rutas/envíos por contadores de bultos con el mismo nombre. Una notificación repetida sólo provoca otra lectura. El consumidor usa el OperadorId del sobre; no difunde novedades entre operadores. El público conserva BTO N y su token firmado, sin Id/código interno.

No se promete orden de llegada entre colas: la proyección se lee del estado confirmado; si se materializa una vista se deduplica por mensaje y se protege con revisión. API y Worker no comparten caché en memoria: el recorrido desplegado se prueba con RabbitMQ y Valkey. Los avisos externos mantienen v1/v2 ya acordados; el hecho interno de recepción no crea un aviso externo nuevo. Payload/firma/versión persistidos son inmutables en reintentos.

## D6. Migración y retirada del puente

La migración se prepara primero en copia representativa y conserva estados, historial, evidencia y colas. Se crean reservas sólo con pertenencia inequívoca de bulto a ruta. Duplicidad de rutas o custodia incierta produce registro de conciliación y bloqueo; no se elige una ruta arbitraria ni se declara el bulto disponible.

La actualización se realiza coordinadamente en ventana de mantenimiento: detener escrituras/Worker, respaldar, aplicar migraciones y controles de invariantes, desplegar API/Worker compatibles, habilitar escrituras y actualizar PWA conservando la cola. Las operaciones antiguas siguen entrando por la traducción del ADR-0004. No se exige borrar IndexedDB ni se reescribe una operación confirmada.

Antes de reabrir escrituras se puede volver al respaldo con los binarios anteriores si la validación falla. Después de admitir nuevas escrituras se corrige hacia adelante; restaurar un respaldo sin reconciliar operaciones perdería trabajo. Retirar columnas/puente se deja para una migración posterior a verificar consumidores, sin depender de que todos los teléfonos actualicen al mismo tiempo.

## D7. Aceptación y evidencia necesaria

| Escenario | Resultado exigido |
| --- | --- |
| Dos despachadores reservan el mismo bulto | Uno confirma y otro recibe conflicto; nunca dos reservas abiertas. |
| Falta de carga con custodia conocida/desconocida | Sólo se libera el confirmado físicamente; el no localizado queda bloqueado. |
| Reprogramado aún en vehículo | No se ofrece para otra ruta; retorno confirmado permite planificar sin sumar intentos. |
| Devolución de uno entre entregados | Sólo cambia el seleccionado; receptor y evidencia quedan asociados; resumen parcial final al terminar todos. |
| Evento de bulto de 1/3 a 2/3 | Público y tablero actualizan progreso con el mismo resumen; v1 no recibe entrega completa falsa. |
| Evidencia ajena por operador/comercio/envío/ruta | Rechazo sin lectura ni asociación indebidas; consulta pública sin identificadores internos. |
| Actualización de datos y cola antigua | Sin pérdidas; ambigüedades bloqueadas para conciliación; reenvío sin duplicados. |
| Teléfono con build publicado | Dos entregados y uno con motivo sin red, reapertura, sincronización y pérdida de respuesta; cotejar backoffice, público, Worker y receptor v1/v2. |

Registrar commit/build, fecha, dispositivo/navegador, OperacionId, pasos y resultados, sin publicar firmas, fotos o datos personales. Las pruebas automatizadas del ADR son evidencia parcial; la demostración física y los escenarios nuevos requieren su propia ejecución. Un fallo abre un defecto de implementación o, si cambia una regla, una revisión explícita del ADR.

## D8. Contrato monetario y liquidación · confirmado por Lucas

Lucas confirmó el 09/11/2026 UYU, cargo único por visita y una única inclusión en liquidación. Luego ajustó la decisión monetaria a pesos enteros, sin centésimos; esta revisión sustituye el criterio anterior de dos decimales y redondeo al par. También confirmó cobrar la base una vez al terminar si hubo al menos un bulto entregado o devuelto, excluirla si todos fueron cancelados/extraviados y liquidar recargos aparte. Las reglas siguientes concretan ese acuerdo y su implementación objetivo; no se atribuyen aprobaciones adicionales del equipo ni se presentan como código ya realizado.

- Moneda única UYU para este alcance; importes finales en pesos enteros, sin centésimos. Tarifas fijas y recargos configurados se ingresan como pesos enteros. Se conserva `decimal` para los cálculos, incluidos porcentajes, y se normaliza una sola vez cada concepto monetario final al registrarlo: peso más cercano, con 0,50 hacia arriba (`Math.Round(monto, 0, MidpointRounding.AwayFromZero)` para los importes no negativos de este alcance). Ejemplos: 1500 → 1500; 1500,49 → 1500; 1500,50 → 1501. La base se normaliza después de sumar sus componentes, no por cada bulto. Se persiste y muestra el importe sin decimales; liquidación suma conceptos ya normalizados, sin volver a redondear o aplicar tarifas futuras. No introducir conversión de moneda ni modificar pesos/medidas de los bultos.
- Tarifa base del envío una sola vez cuando todos sus bultos sean terminales y al menos uno esté Entregado o Devuelto. Sin base para un conjunto formado sólo por Cancelado/Extraviado. Un cierre mixto elegible cobra la tarifa de admisión del envío, sin multiplicarla ni prorratearla por bulto.
- El cargo de revisita se devenga por una visita adicional efectivamente registrada, aunque falle, con el monto y la versión de reglas del envío. La primera visita, faltantes de carga, planificación y visitas rechazadas no lo generan. Es independiente de que el envío ya haya terminado: se liquida por la fecha de la visita en la zona del operador, incluso si el envío termina en un período posterior.
- El modelo objetivo registra una identidad de visita vinculada a ruta/envío y OperacionId; no permite dos visitas confirmadas del mismo envío/ruta en este flujo. Unicidad del cargo por `(OperadorId, VisitaId)`. El reenvío conserva identidad y resultado; la idempotencia del cargo no depende sólo de recordar que ya se envió una petición HTTP.
- Envíos publica por contrato conceptos liquidables con `OrigenTipo` (BaseEnvio o RecargoVisita), `OrigenId` (EnvioId o CargoId), operador/comercio, fecha, monto y moneda. Persiste el instante de cierre al pasar Terminado de falso a verdadero; no lo deduce del estado global de compatibilidad. Conservar inmutables los hechos históricos.
- Depósito reserva cada concepto al generar un borrador, con unicidad `(OperadorId, OrigenTipo, OrigenId)` en las imputaciones de liquidación. Generación concurrente no puede incluir el mismo concepto dos veces, aunque los períodos se superpongan. Eliminar un borrador libera sus imputaciones; una emitida/pagada no se elimina ni se recalcula.
- Una liquidación puede contener sólo recargos. El mismo envío puede aportar una base y varios cargos distintos: se reemplaza la restricción actual «una línea por EnvioId» por «una línea por concepto de origen». TotalBruto suma bases, TotalAjustes suma recargos y TotalNeto suma ambos, sin contar un cargo en dos totales.

Criterios: cierre 2 entregados/1 devuelto, cierre mixto con extravío, sólo cancelados/extraviados, revisita fallida, cargos anteriores al cierre, períodos superpuestos, dos generaciones concurrentes, borrador eliminado y reenvío sin duplicación. Son pruebas objetivo, no resultados de la suite anterior.

## D9. Borrador de visita y evidencia al confirmar

Al recargar o reabrir una visita sin confirmar, la PWA recupera el borrador cifrado con los resultados y motivos ya guardados por bulto. La firma, la foto, el nombre y documento de quien recibe y las observaciones se mantienen en memoria y deben volver a capturarse tras recargar o salir. La interfaz advierte al abandonar una captura en curso. Si cambia el conjunto de bultos entregados, se invalida la firma y se pide una nueva.

Al confirmar, la PWA guarda la operación completa y su evidencia de forma cifrada en una única transacción local. La visita confirmada puede reabrirse sin conexión y queda disponible para sincronizar cuando se recupere la conectividad.

Lucas confirmó este alcance el 15/11/2026. La recuperación completa de una captura sin confirmar, propuesta el 09/11, quedó fuera del alcance. El RF 32 y la sección 6.11 de la letra exigen captura offline, almacenamiento cifrado y sincronización de la operación registrada. El guardado de esos campos mientras se edita el formulario queda a criterio del equipo.

## Estado del cierre

D1 a D9 quedan definidos para el alcance del ADR-0007. La sección 4.6 del ADR reúne las verificaciones de la implementación.
