# **ADR-0004 · Resolución de conflictos de sincronización**

| Estado | Aceptado — aprobado por el equipo el 15/10/2026 |
| :---- | :---- |
| **Fecha** | 12 de octubre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Política de resolución de conflictos de sincronización (letra, sección 8.2, decisión obligatoria) |
| **Relacionado con** | RF 24 y secciones 6.11, 6.12 y 9.3 de la letra; ADR-0001 (addendum 3); ADR-0002; ADR-0003; CU-17, CU-18, CU-21, CU-50 a CU-55; tabla de transiciones de estados |

**Este registro decide cómo la aplicación del repartidor sincroniza lo que registró sin conexión y qué pasa cuando eso choca con lo que cambió en el servidor mientras tanto: la PWA envía operaciones, no estados; cada operación tiene un identificador generado en el dispositivo que la hace idempotente; el servidor es la autoridad y decide con la máquina de estados del envío; y una operación rechazada no se pierde: queda registrada, con su evidencia y sin aplicarse, para que el despachador la revise.**

# **1\. Contexto y restricciones**

## **1.1 El problema**

El repartidor trabaja horas sin señal (letra, sección 3.1). En ese tiempo registra en la PWA cargas, entregas, intentos fallidos y posiciones, que llegan al servidor recién cuando recupera la conexión. Mientras tanto, el servidor puede haber cambiado: un despachador puede haber declarado extraviado un envío, o un operario puede haberlo reintegrado al depósito. Al sincronizar, lo registrado en el dispositivo y el estado del servidor pueden ser incompatibles, y la letra exige una política explícita para esos casos (RF 24).

Un ejemplo que sirve de guía para todo el registro:

> El repartidor entrega un envío a las 11:00, sin señal. A las 15:00 le roban la camioneta y avisa por teléfono. El despachador declara *Extraviados* (T16) los envíos de la ruta que el servidor ve *EnTransito*, incluido el que ya se había entregado. A las 18:00 el celular recupera la conexión y envía la entrega de las 11:00.

## **1.2 Cuándo puede cambiar un envío en el servidor mientras el repartidor está sin conexión**

La PWA trabaja sobre una ruta **despachada** (CU-50). Para un envío de esa ruta, la tabla de transiciones deja pocas vías de cambio en el servidor:

| Estado en el servidor | Cómo llegó ahí mientras el repartidor estaba sin conexión | Operaciones del dispositivo afectadas |
| :---- | :---- | :---- |
| *AsignadoARuta* | No cambió: la PWA confirmó la carga sin conexión (T5 pendiente) | Ninguna: la carga se aplica primero, en orden |
| *EnTransito* | No cambió | Ninguna |
| *Extraviado* | T16, por ejemplo después de un robo (CU-21) | Entrega e intento fallido |
| *EnDeposito* | T8: la rendición se hizo antes de que el celular sincronizara (CU-54 y CU-55) | Entrega e intento fallido |
| *Entregado*, *NoEntregado* y siguientes | Ya se aplicó otra operación sobre el mismo envío | La segunda operación de resultado |

No hay otras vías: la cancelación (T17) sólo vale para envíos *Admitidos*, las reprogramaciones del destinatario (T18 y T19) y las desasignaciones (T4, CU-41) sólo valen antes del despacho, y las reasignaciones urgentes de una ruta despachada quedan fuera del alcance mínimo (CU-43). Si se incorporan, son un caso más de la sección 2.4.

## **1.3 Restricciones**

•  La letra exige operación completa sin conectividad, con almacenamiento local cifrado y sincronización diferida con resolución de conflictos documentada (sección 6.11), y un identificador de correlación de extremo a extremo y la métrica de sincronizaciones pendientes (sección 6.12). Los escenarios 2 y 3 de la defensa operan sin conexión y provocan un conflicto (sección 9.3).  
•  La hora de cada prueba y de cada evento es la del dispositivo, no la de la sincronización (RF 20; CU-17, requerimiento 2).  
•  `Envio.Transicionar` es el único punto que cambia el estado de un envío y aplica la tabla de transiciones; el envío tiene concurrencia optimista (`xmin`).  
•  La pareja (envío, número de intento) es única (CU-18, requerimiento 2).  
•  El inquilino sale de la sesión del repartidor (ADR-0002) y los eventos de cada transición van por Outbox (ADR-0003).  
•  Las fotos y firmas se guardan en PostgreSQL, hasta 1 MB cada una, y la PWA comprime la foto antes de guardarla (CU-17, requerimiento 3).  
•  La PWA es Blazor WebAssembly con IndexedDB (Propuesta de stack); la programa Lucas Ottonello. El servidor de la sincronización es el módulo Ejecución (CU-52), que llama a Envíos y a Planificación por sus contratos (ADR-0001).

# **2\. Decisión**

## **2.1 Se sincronizan operaciones, no estados**

La PWA no envía "el envío X quedó *Entregado*", sino **la operación que hizo el repartidor**, con todos sus datos: "entregué el envío X a las 11:00, en esta posición, con esta firma". El servidor decide qué hacer con ella. Así el servidor conserva lo que pasó en la calle aunque no lo pueda aplicar, y las reglas siguen en un solo lugar.

| Tipo de operación | Caso de uso | Efecto en el servidor |
| :---- | :---- | :---- |
| `ConfirmarCarga` | CU-51 | T5 de los envíos de la ruta |
| `RegistrarEntrega` | CU-17 | T6, con su `IntentoEntrega` y su `PruebaEntrega` |
| `RegistrarIntentoFallido` | CU-18 | T7 y, en la misma transacción, T9 o T10 |
| `ReportarPosicion` | CU-53 | Una `PosicionVehiculo`; no cambia estados |
| `IniciarRendicion` | CU-54 | Una `Rendicion` |

Cada operación lleva:

| Campo | Uso |
| :---- | :---- |
| `OperacionId` | Guid v7 generado en el dispositivo al confirmar la operación. Base de la idempotencia |
| `DispositivoId` y `Secuencia` | Identificador de la instalación de la PWA y número correlativo de la operación en ese dispositivo. Dan el orden |
| `Tipo` | Uno de la tabla anterior |
| `RutaId` y `EnvioId` | La ruta descargada y, si corresponde, el envío |
| `OcurridoEn` | Hora del dispositivo al confirmar |
| `Latitud` y `Longitud` | Posición del dispositivo (obligatoria en entregas e intentos, RF 20) |
| `CorrelacionId` | `traceparent` generado en el dispositivo (sección 2.9) |
| `Datos` | Lo propio de cada tipo: firma, foto, nombre y documento del receptor, motivo, observaciones. La firma y la foto van en base64 |

## **2.2 Idempotencia: cada operación se aplica una sola vez**

El módulo Ejecución registra cada operación recibida en la tabla `operaciones_dispositivo` de su schema, con su `OperacionId` como clave, su resultado y su motivo. El registro se guarda **en la misma transacción que el efecto** de la operación.

•  Si llega una operación cuyo `OperacionId` ya está registrado (la PWA reenvió el lote porque se cortó la conexión antes de recibir la respuesta), el servidor **no la vuelve a procesar**: responde el resultado guardado. Para la PWA, el reenvío es indistinguible de la primera respuesta.  
•  La unicidad de (envío, número de intento) de CU-18 queda como segunda barrera en la base, por si dos procesamientos concurrentes llegaran a la vez.  
•  `operaciones_dispositivo` implementa `IOperadorOwned`: son datos del operador, y el filtro "Tenant" hace que cada despachador vea sólo los conflictos de su operador.

## **2.3 La política: el servidor decide, con la máquina de estados**

Cada operación se evalúa contra el estado **actual del servidor**, en el momento de procesarla:

1\.  **Pertenencia.** El repartidor autenticado es el asignado a la ruta de la operación, y el envío está en una parada de esa ruta (una parada puede tener varios envíos, modelo de dominio). Si no, la operación es un conflicto (`RutaNoAsignada`).

2\.  **Validez de los datos.** Las pruebas cumplen la versión de reglas **del envío** (la firma siempre; foto, nombre y documento según las reglas), el motivo existe en el catálogo de esa versión y cada archivo pesa hasta 1 MB. Si no, la operación es **inválida**. La PWA ya lo impide (CU-17 y CU-18, flujo A1), así que una operación inválida indica un error de la PWA, no un conflicto.

3\.  **Transición.** El efecto se aplica con `Envio.Transicionar`, que verifica la tabla de transiciones. Si el envío ya no está en un estado desde el que la transición es válida, la operación es un **conflicto** y no se aplica.

4\.  **Aplicación.** Si todo es válido, se aplica con la hora del dispositivo: el `EventoEnvio`, el `IntentoEntrega` y la `PruebaEntrega` llevan `OcurridoEn`, no la hora de la sincronización.

No se usa la hora para decidir quién gana. El servidor no aplica una operación sólo porque ocurrió antes: la tabla de transiciones es la única regla. Lo que pasó en la calle y no se pudo aplicar no se pierde (sección 2.5).

## **2.4 Resultados y conflictos**

Cada operación termina en uno de cuatro resultados:

| Resultado | Cuándo | Qué ve el repartidor | Qué queda en el servidor |
| :---- | :---- | :---- | :---- |
| **Aplicada** | La transición era válida | Nada: la operación desaparece de pendientes | El efecto y el registro de la operación |
| **Ya aplicada** | El `OperacionId` ya estaba registrado | Lo mismo que la primera vez | Nada nuevo |
| **Conflicto** | El estado del servidor es incompatible con la operación | La operación, con el motivo, hasta que la marca como vista | El registro con la operación completa, sin aplicar |
| **Inválida** | Los datos no cumplen las reglas del envío | Igual que un conflicto, con el dato que falta | El registro con la operación completa, sin aplicar |

Los conflictos de la sección 1.2, con su motivo:

| Situación | Motivo | Resultado |
| :---- | :---- | :---- |
| Entrega o intento de un envío *Extraviado* (robo) | `EnvioExtraviado` | Conflicto. El envío sigue *Extraviado* |
| Entrega o intento de un envío ya reintegrado al depósito (rendición antes de sincronizar) | `EnvioEnDeposito` | Conflicto. El envío sigue *EnDeposito* |
| Segunda operación de resultado sobre un envío ya resuelto | `EnvioYaResuelto` | Conflicto. Vale la primera que llegó al servidor |
| Operación sobre una ruta que no es del repartidor autenticado | `RutaNoAsignada` | Conflicto |
| Operación sobre el mismo envío después de una rechazada en el mismo lote | `DependeDeOperacionRechazada` | Conflicto (sección 2.6) |

La PWA evita la tercera situación desde su lado: un envío que ya tiene un resultado registrado en el dispositivo no admite otro (sección 2.8).

## **2.5 Qué se conserva de una operación rechazada**

Una operación en conflicto o inválida **se guarda completa en `operaciones_dispositivo`, con su evidencia, y no se aplica**: no cambia el estado del envío ni genera eventos del envío.

•  Se conserva porque el rechazo puede ser la única prueba de lo que pasó. En el ejemplo de la sección 1.1, la entrega de las 11:00 se rechaza porque el envío figura *Extraviado*, pero su firma y su hora prueban que se entregó antes del robo. Sin el registro, el comercio reclamaría un paquete que el destinatario recibió, y no quedaría prueba.  
•  Cada conflicto queda **pendiente de revisión** en el tablero del despachador (CU-66), que lo marca como revisado. La revisión no cambia el envío: corregir un estado del servidor que la operación demuestra equivocado es una decisión de negocio con su propio caso de uso. Hoy la tabla de transiciones no tiene salida desde *Extraviado* (estado terminal); si el equipo decide permitir esa corrección, es una transición nueva en la tabla, no una excepción de la sincronización (sección 6).  
•  Registrar el conflicto publica `ConflictoSincronizacionRegistrado` por Outbox, para que el tablero lo muestre en tiempo real (ADR-0003).

## **2.6 Orden y transacciones**

•  La PWA envía las operaciones en lotes, en orden de `Secuencia`, y el servidor las procesa **en ese orden**, una por una.  
•  **Cada operación es una transacción propia** (unidad de trabajo del ADR-0001, addendum 3): el registro en Ejecución, la transición en Envíos y las filas del outbox se confirman juntos. Si la conexión se corta a mitad de un lote, las operaciones ya procesadas quedan aplicadas; el reenvío las reconoce por su `OperacionId` y sigue con las demás.  
•  Un conflicto no detiene el lote: las operaciones sobre **otros** envíos se siguen procesando. Las posteriores sobre el **mismo** envío se rechazan como `DependeDeOperacionRechazada`, porque dependían de un estado que no se alcanzó.  
•  Si al aplicar una operación salta la concurrencia optimista (un despachador cambió el mismo envío en ese instante), se vuelve a leer el envío y se reevalúa la operación una vez. El segundo intento decide.

## **2.7 La hora**

•  Se registran las dos horas: `OcurridoEn`, del dispositivo, y `RecibidoEn`, del servidor. El historial del envío usa `OcurridoEn` (RF 20).  
•  La hora del dispositivo llega con su desfase local (por ejemplo, −03:00). El dominio la guarda como el mismo instante en UTC, porque PostgreSQL sólo admite `timestamp with time zone` en UTC; la hora local se reconstruye con la zona horaria del operador.  
•  Una `OcurridoEn` posterior a `RecibidoEn` en más de 2 minutos indica un reloj adelantado: la operación se aplica con `RecibidoEn` y queda marcada con la hora corregida. No se rechaza: un reloj mal configurado no debe perder una entrega.  
•  `RecibidoEn − OcurridoEn` es el atraso de la sincronización y alimenta las métricas (sección 2.9).

## **2.8 Lado del dispositivo (PWA)**

Lo implementa Lucas Ottonello en CU-50 a CU-52. Este registro fija lo que el servidor necesita:

•  **Cola local.** Cada operación confirmada se guarda en IndexedDB, cifrada (sección 6.11 de la letra), con estado *Pendiente*, *Enviando*, *Aplicada* o *Rechazada*. Se borra recién cuando el servidor responde *Aplicada* o *Ya aplicada*. Una rechazada se muestra al repartidor hasta que la marca como vista.  
•  **Cuándo sincroniza.** Al abrir la PWA, al recuperar la conexión (evento `online`) y cada 30 segundos mientras haya pendientes y conexión. Hay un solo envío en curso a la vez. Si falla la red, reintenta con espera creciente.  
•  **Lotes.** Hasta 20 operaciones y 5 MB por lote; la foto ya viaja comprimida.  
•  **Un envío, un resultado.** Si el envío ya tiene una entrega o un intento fallido registrados en el dispositivo, la PWA no permite registrar otro. Una parada con varios envíos (por ejemplo, apartamentos del mismo edificio) admite un resultado por cada envío.  
•  **Sesión vencida** (CU-52, A3). El servidor responde 401 sin procesar el lote; la PWA pide iniciar sesión y conserva los pendientes.  
•  **Después de sincronizar,** la PWA descarga los cambios de su ruta (CU-50, A3), así refleja los conflictos en las paradas.

## **2.9 Transporte, correlación y métricas**

•  **Transporte.** `POST /api/repartidor/sincronizacion`, autenticado con la sesión del repartidor (Identity). La respuesta trae el resultado de cada operación del lote, con su motivo.  
•  **Correlación.** Cada operación lleva un `traceparent` generado en el dispositivo. El servidor continúa esa traza al procesarla, y el outbox la guarda en cada evento (ADR-0003), así una entrega se sigue desde la PWA hasta el Worker (letra, sección 6.12).  
•  **Métricas.** La PWA informa su cantidad de operaciones pendientes en cada sincronización y en cada reporte de posición (CU-53), y el servidor la expone como la métrica de *sincronizaciones pendientes* del tablero técnico (letra, sección 6.12). También se miden las operaciones aplicadas, en conflicto e inválidas por motivo, y el atraso de la sincronización.

## **2.10 Dónde vive cada pieza**

| Pieza | Ubicación |
| :---- | :---- |
| Endpoint, handler `SincronizarOperaciones` y entidad `OperacionDispositivo` | Módulo Ejecución (Presentation, Application y Domain) |
| Tabla `operaciones_dispositivo` | Schema `ejecucion` |
| Transiciones T5, T6, T7, T9 y T10 | Módulo Envíos, por `IEnviosModuleApi` |
| Pertenencia del envío a la ruta y del repartidor a la ruta | Módulo Planificación, por su contrato |
| Cola local, cifrado y disparadores de la sincronización | `Logistica.Repartidor.Pwa` |
| DTO de la operación y de la respuesta | `Logistica.Http.Contracts`, compartidos con la PWA |

# **3\. Alternativas consideradas**

## **3.1 Gana la última escritura**

Se aplicaría la operación con la hora más reciente, del dispositivo o del servidor.

**Motivo de descarte:** depende del reloj del celular, que no es confiable, y pisaría sin aviso un cambio del despachador. En el ejemplo del robo, aplicaría la entrega de las 11:00 contra un extravío declarado a las 15:00 sólo por comparar horas, sin que nadie revise.

## **3.2 Gana el dispositivo**

Todo lo registrado por el repartidor se aplicaría, por ser un hecho de la calle.

**Motivo de descarte:** obligaría a agregar transiciones desde estados terminales (por ejemplo, *Extraviado* → *Entregado*) que se usarían sin ninguna revisión, y debilitaría la máquina de estados, que es el único punto de control del ciclo de vida del envío.

## **3.3 Revisión manual de todas las operaciones**

Un despachador aprobaría cada operación sincronizada.

**Motivo de descarte:** la gran mayoría de las operaciones no tiene conflicto. Revisarlas todas frenaría la operación diaria. La revisión manual queda sólo para los conflictos (sección 2.5).

## **3.4 Sincronizar estados en lugar de operaciones**

La PWA enviaría el estado final de cada envío.

**Motivo de descarte:** se pierde qué pasó (la prueba, el motivo, la hora y el orden), el servidor no puede validar las reglas y un estado no se puede rechazar sin perder la información que lo produjo.

## **3.5 Rechazar sin conservar la operación**

**Motivo de descarte:** en un conflicto como el del robo, la operación rechazada es la única prueba de la entrega. Conservarla no cuesta casi nada, porque el registro igual hace falta para la idempotencia.

## **3.6 Un lote atómico, todo o nada**

**Motivo de descarte:** un solo conflicto haría rechazar todas las demás entregas del día, y un corte a mitad del lote obligaría a repetirlo entero. Una transacción por operación deja avanzar lo que es válido.

## **3.7 Subir la firma y la foto aparte**

**Motivo de descarte:** habría archivos subidos cuya operación nunca llega, y habría que limpiarlos. Con los archivos dentro de la operación, llega completa o no llega. El costo es un tamaño mayor por la codificación base64, que se acota con el límite del lote.

## **3.8 Sólo la sincronización en segundo plano del service worker (Background Sync API)**

**Motivo de descarte:** no está disponible en todos los navegadores, y la sincronización es el escenario 2 de la defensa. Se usan disparadores que funcionan en todos (sección 2.8). La Background Sync API puede agregarse como mejora.

## **3.9 CRDT o event sourcing completo**

**Motivo de descarte:** resuelven la edición concurrente de los mismos datos por varios usuarios. Acá cada parada la resuelve un solo repartidor y el servidor tiene reglas de negocio que no se pueden fusionar automáticamente. Agregarían complejidad sin resolver un problema que tengamos.

# **4\. Consecuencias**

## **4.1 Positivas**

•  La política es determinista y explicable: decide la misma tabla de transiciones que en el resto del sistema.  
•  Reenviar un lote nunca duplica una entrega ni un intento.  
•  Ninguna operación del repartidor se pierde, aunque se rechace, y cada conflicto queda a la vista del despachador.  
•  El historial del envío conserva la hora real de lo que pasó en la calle.  
•  El conflicto del escenario 3 de la defensa se reproduce con un caso realista (sección 5).

## **4.2 Negativas y riesgos**

•  En un conflicto, el estado del servidor puede quedar distinto de lo que pasó en la calle hasta que el despachador lo revise. En el caso del robo, el envío queda *Extraviado* aunque se haya entregado, y hoy no hay una transición para corregirlo.  
•  La base64 agranda un tercio la firma y la foto en la red.  
•  La PWA tiene que mantener la cola local, el orden y los reintentos: es la parte más delicada del trabajo de Lucas para el 22/10.  
•  `operaciones_dispositivo` crece con cada operación; necesita una política de retención (sección 6).  
•  Depende de la autenticación del repartidor con Identity (Cristian) para saber quién envía cada lote.  
•  El modelo de dominio decía que RF 24 no agrega clases; este registro agrega `OperacionDispositivo` en Ejecución, y el modelo debe actualizarse.

# **5\. Verificación**

La decisión se considera verificada cuando existan pruebas automatizadas que demuestren que:

1\.  Reenviar un lote ya procesado responde los mismos resultados y no aplica nada de nuevo.

2\.  Si la conexión se corta a mitad del lote, las operaciones procesadas quedan aplicadas y el reenvío procesa sólo las demás.

3\.  Una entrega sobre un envío *Extraviado* termina en conflicto, el envío no cambia y la operación queda guardada con su firma.

4\.  Una entrega sobre un envío reintegrado al depósito termina en conflicto.

5\.  Una segunda operación de resultado sobre el mismo envío termina en conflicto y vale la primera.

6\.  Una operación posterior sobre el mismo envío que una rechazada se rechaza, y las de otros envíos se aplican.

7\.  Una entrega sin firma, o sin las pruebas que exige la versión de reglas del envío, es inválida.

8\.  El historial del envío usa la hora del dispositivo, y una hora del dispositivo adelantada se corrige y se marca.

9\.  Un repartidor no puede sincronizar operaciones de una ruta que no es suya, ni de otro operador.

**Demostración del escenario 3** (letra, sección 9.3): con la PWA sin conexión, el repartidor registra una entrega. Desde el Backoffice, el despachador declara extraviado ese envío (CU-21). Al recuperar la conexión, la PWA muestra la entrega rechazada con el motivo, el envío sigue *Extraviado* y el conflicto aparece en el tablero con la firma registrada.

# **6\. Decisiones a revisar**

| Tema | Valor inicial | Responsable |
| :---- | :---- | :---- |
| Corrección de un estado terminal cuando un conflicto demuestra que es incorrecto (por ejemplo, *Extraviado* → *Entregado*) | No se permite; el conflicto sólo se marca como revisado | Cristian (tabla de transiciones) |
| Reasignaciones urgentes de una ruta despachada | Fuera del alcance mínimo (CU-43); si se incorporan, son un conflicto más (`RutaNoAsignada`) | Lucas |
| Tamaño del lote e intervalo de la PWA | 20 operaciones, 5 MB, 30 s | Lucas, al medir |
| Tolerancia del reloj adelantado | 2 minutos | Ezequiel, al medir |
| Retención de `operaciones_dispositivo` | 30 días las aplicadas; los conflictos hasta revisarse | Ezequiel, antes del despliegue del 5/11 |

# **7\. Plan de implementación**

| Hito | Qué | Responsable |
| :---- | :---- | :---- |
| 22/10 | Endpoint y handler de sincronización, `operaciones_dispositivo`, política y resultados, CU-17 y CU-18 del lado del servidor | Ezequiel |
| 22/10 | Cola local cifrada, disparadores, lotes y pantalla de rechazos en la PWA | Lucas |
| 22/10 | Autenticación del repartidor | Cristian |
| 29/10 | Conflictos en el tablero en tiempo real (CU-66) | Cristian |

# **8\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 12/10/2026 | Propuesta inicial. Decisiones tomadas por el responsable antes de redactarla: el servidor decide con la máquina de estados, los archivos viajan dentro de la operación y T16 conserva *EnTransito* como origen (robo durante el reparto). | Ezequiel Marcenal |
| 0.2 | 14/10/2026 | Se adapta a las paradas con varios envíos (modelo de dominio, CU-40): la pertenencia y la regla de un resultado se verifican por envío, no por parada. | Ezequiel Marcenal |
| 1.0 | 15/10/2026 | Aprobado por el equipo (Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes). | Ezequiel Marcenal |
| 1.1 | 15/10/2026 | Sección 2.7: la hora del dispositivo se guarda como el mismo instante en UTC. | Ezequiel Marcenal |
