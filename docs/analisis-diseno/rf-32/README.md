# RF 32 · Visita por bulto en la PWA sin conexión

Fecha: 08/11/2026. Responsable del trabajo: Lucas Ottonello. Implementación asistida por Codex.

Revisión del 09/11/2026: [análisis de alcance y faltantes antes de nuevos cambios](analisis-de-cierre.md), con cobertura observada, contradicciones documentales, límites de recuperación y criterios de aceptación conjunta. Esta revisión no agrega funcionalidad.

Continuación autorizada del 09/11/2026: [especificación técnica consolidada](especificacion-tecnica.md) y [registro de verificación](verificacion-cierre.md). La revisión anterior conservaba la captura sin confirmar en pantalla y advertía antes de salir. Al cerrar ADR-0007, Lucas confirmó posteriormente persistir toda la captura cifrada (firma/foto/textos) y recuperarla al reabrir; D9 del [cierre técnico](../cierre-tecnico-entrega-parcial.md) es la decisión vigente. La implementación y pruebas de advertencia descritas aquí no acreditan todavía esa recuperación completa.

Base funcional: [decisiones de entrega parcial, secciones 4.1 y 4.2](../decisiones-entrega-parcial.md), [ADR-0004, 2.11](../ADR-0004-resolucion-de-conflictos-de-sincronizacion.md) y [ADR-0007](../ADR-0007-adaptacion-a-entrega-parcial.md). La letra docente de cambio, 2.1, contempla que un bulto no se haya cargado en el vehículo. Las decisiones de foto compartida y custodia fueron confirmadas por Lucas; no se presentan como citas literales del docente.

**Ensayo físico del 10/11/2026:** [resultado en Android](ensayo-android-2026-11-10.md): tres cargados, dos entregados offline, tercero con motivo, reapertura sin red, sincronización a 2/3 y reenvío YaAplicada sin nuevos efectos. Se detallan el límite de ubicación, la preparación de rendición y los pendientes. No acredita D9 ni recepción externa de avisos.

## Recorrido implementado

**Cierre de aceptación del 10/11/2026:** [ensayos integrados aprobados](aceptacion-integrada-2026-11-10.md). Se verificaron conflicto con diagnóstico por bulto y evidencia conservada, entrega independiente offline, resultado en Backoffice/público, recepción HTTPS de avisos v1/v2 y reenvío sin duplicados. Se completa la aceptación del alcance original de RF 32. D9 sigue registrada como decisión interna posterior pendiente; no se declara implementada ni se extiende este cierre al resto de RF 31 a 36.

Después de confirmar la carga y pulsar **Comenzar ruta**, desde Hoja de ruta cada envío abre Registrar visita. El inicio puede quedar pendiente de sincronizar y cierra nuevas cargas en ese dispositivo. El servidor exige EnCurso para registrar las visitas. Se escanean los entregados por cámara o ingresando el código del lector. Cada otro bulto de la visita requiere un motivo individual del catálogo versionado del envío. No hay un motivo implícito para un paquete sin escanear. Los no cargados y ya resueltos se muestran fuera del alcance.

Antes de firmar se enumeran los bultos entregados. La firma se invalida al cambiar ese conjunto, incluso si luego se vuelve a la selección original. Si se entrega alguno, se exige firma; los datos del receptor y foto respetan las reglas del envío. Una visita sin entregas no solicita firma.

Una única foto de presencia en el lugar se comparte entre los resultados exitosos y fallidos de esa visita. Si se exige por algún motivo o por las reglas de entrega, se debe aportar. No se genera una foto separada de rechazo. Una visita posterior usa su propia prueba y no sobrescribe la anterior; cada IntentoEntrega vincula la evidencia con sus BultoIds.

La confirmación genera sólo `RegistrarVisita`, nunca nuevas `RegistrarEntrega`/`RegistrarIntentoFallido`. Se muestra por separado pendiente de sincronizar, aplicada o rechazada. Un rechazo indica que no se aplicó ningún resultado y enumera el diagnóstico por bulto. Desde la hoja se puede reabrir ese resultado.

## Contratos y compatibilidad

- La hoja HTTP pasa a schema 3: estado operativo en BultoHojaResponse y AdmiteEntregaParcial en ReglasEntregaHojaResponse, tomados de Envíos y de la versión de reglas de cada envío.
- El lector local acepta esquemas 1 y 2 existentes, sin borrar datos. En ellos, la ausencia de la nueva regla se interpreta conservadoramente como parcial deshabilitada y se conserva la interpretación previa por envío.
- El esquema 3 rechaza hojas sin estados o sin la regla booleana: no guarda una copia incompleta sobre la anterior.
- El payload de visita ya existente conserva código/motivo por bulto y una firma/foto por visita. No necesita dividir la foto en dos campos.
- CargaRequest agrega PorBulto y Faltantes (código y motivo). Las operaciones antiguas sin PorBulto conservan su semántica por envío completo. La PWA schema 3 genera PorBulto=true.
- Los contratos entre módulos sólo exponen datos; la PWA sigue referenciando Http.Contracts, no proyectos internos.

## Guardado y sincronización

En hoja-ruta.js, abrirVisita/resultadoVisita guardan un borrador cifrado separado por ruta y envío. Se reutilizan IndexedDB, AES-GCM, identidad local y token del espacio. No se crea otra base ni se migra borrando la anterior.

Al confirmar, borrador confirmado y operación se escriben en una única transacción de IndexedDB. La revisión del borrador impide que dos pestañas creen dos confirmaciones de la misma visita. Se comprueba también que la rendición no se haya confirmado en paralelo. La firma y foto se incorporan a la operación cifrada antes de informar guardado correcto. Mientras se completa el formulario, los resultados se conservan; la firma, foto y textos aún no confirmados permanecen en la pantalla y se deben volver a capturar si se la abandona. En la revisión anterior del 09/11 se mantuvo ese comportamiento con advertencia antes de salir o recargar; D9 lo sustituye como objetivo por captura completa persistida y cifrada, pendiente de implementar/verificar; una visita completamente guardada deja de advertir. El navegador puede omitir la advertencia en un cierre forzado.

La cola conserva OperacionId, dispositivo, secuencia, fecha y ubicación. Una pérdida de red o de respuesta reenvía el mismo payload. Aplicada/YaAplicada se convierten en recibos; Conflicto/Invalida conservan la operación y su evidencia. Detalle y diagnóstico por bulto se guardan dentro del contenido cifrado, no como códigos personales en metadatos sin cifrar. Los rechazos no se convierten automáticamente en nuevas visitas.

El lector de cámara se limita a CODE-128, el formato emitido por las etiquetas de la aplicación. Antes de confirmar una carga se puede quitar una lectura errónea de la lista de sobrantes; se conservan las demás lecturas y se comprueba la revisión del borrador. Esta edición no permite alterar una confirmación ni su payload. El avance sin confirmar pertenece al dispositivo: iniciar sesión con el mismo usuario en otra PC no transfiere ese borrador.

## Carga parcial y rendición

La carga selecciona bultos AsignadoARuta individualmente. Cuando el envío admite parcial y se continúa con faltantes, el repartidor declara sus motivos: NoDisponible, NoLocalizado, SinEspacio u Otro. Son motivos de carga, distintos del catálogo de motivos de no entrega en domicilio. La declaración no acredita que el bulto esté físicamente en depósito.

Si el envío no admite parcial y está incompleto, se muestra una alerta por envío y los bultos leídos indican «Escaneado · no sale». No se ofrecen motivos ni casilla para aceptar su carga incompleta. Si no hay ningún envío habilitado, la confirmación queda bloqueada también en la cola local y en el servidor. Los demás envíos completos pueden continuar mediante «Confirmar envíos completos», sin pedir motivos por el bloqueado. El servidor mantiene compatibilidad con clientes anteriores que ya hubieran enviado esos motivos.

Al sincronizar, ConfirmarCargaHandler toma el bloqueo de planificación del operador. Para los envíos que admiten parcial, el contrato RegistrarCargaPorBultosAsync mueve sólo los escaneados. Si no admiten parcial, no sale un conjunto incompleto de ese envío; los demás completos pueden continuar. Se conservan las discrepancias y el manifiesto de faltantes de las cargas nuevas en Contenido de la operación aplicada. No se consume un intento ni se cobra por registrar un faltante de carga.

La PWA puede registrar la visita sin red después de confirmar localmente esa carga. Sólo incluye los cargados autorizados. El servidor vuelve a validar con la misma versión de reglas; entregar todos los cargados no permite eludir la prohibición de parcial si falta otro pendiente.

La rendición local proyecta RegistrarVisita por bulto: entregados como EntregadoEnCalle, intentados fallidos como IntentoFallido y los que no salieron como NoCargado. Su preparación en el servidor consulta el estado individual. En cargas nuevas se distingue escanear un bulto de haberlo puesto efectivamente en tránsito; se mantiene la interpretación histórica CargaParcial para operaciones antiguas.

Entregar todos los cargados cuando quedó otro fuera no cierra el envío: conserva EntregadoParcialmente y Terminado=false. No se publica EnvioEntregado.v1 hasta que todos los bultos estén entregados.

## Límite con RF 34 y con la tarjeta de servidor

La pantalla y la cola RF 32 quedan implementadas, con adaptaciones de integración de carga, hoja y rendición. Se reutiliza el procesador transaccional de visitas e idempotencia existente; no se declara completado el resto de RF 31 a 36.

**La conciliación de depósito y la liberación/reasignación de reservas por bulto de 4.2 siguen pendientes en Planificación/RF 34.** La reserva actual sigue siendo por envío. No se libera por el solo hecho de no escanear y no se inventa custodia en depósito. El manifiesto conserva el motivo, pero no se agregó una pantalla de confirmación de custodia ni se afirma que el tercero pueda asignarse ya a otra ruta activa. Tampoco se implementan aquí los avisos v2, la invalidación por todo cambio de bulto ni la devolución individual, que tienen sus propias tareas.

## Archivos principales

| Pieza | Archivos |
| --- | --- |
| Pantalla | src/Logistica.Repartidor.Pwa/Pages/Entrega.razor, Carga.razor, HojaDeRuta.razor |
| Servicios del cliente | Services/EntregaService.cs, CargaService.cs |
| Persistencia y evidencia local | wwwroot/js/hoja-ruta.js, evidencia.js; lector camara-carga.js reutilizado |
| Hoja | Http.Contracts/Repartidor/HojaDeRutaResponse.cs; IEnviosHojaRuta, EnviosHojaRuta y DescargarHojaDeRutaHandler |
| Integración de carga | SincronizacionRequest, ConfirmarCargaHandler, IEnviosModuleApi, EnviosModuleApi y Envio |
| Rendición | PrepararRendicion, RendicionRepository y EvidenciaRuta |

No hay una migración nueva de base: estados y reglas existían, y el manifiesto se conserva en la columna Contenido existente. La reserva por bulto requerirá su propio diseño/migración en RF 34.

## Prueba manual

1. Publicar una nueva versión de reglas que admita parcial; crear después un envío de tres bultos. Un envío anterior conserva su versión y puede seguir sin admitirla.
2. Recibir sus bultos, preparar y despachar la ruta. Entrar con su repartidor y descargar la hoja actualizada.
3. Para probar reapertura completa sin red, usar el build publicado y esperar a que se instale el service worker. Development no cachea el shell. Mantener origen y puerto; no cerrar sesión ni borrar IndexedDB.
4. Confirmar carga de los tres, o de dos declarando el faltante. Si la regla no admite parcial, ese envío incompleto no sale.
5. Pulsar **Comenzar ruta** y comprobar que ya no se puede agregar carga. Sin conexión, abrir el envío, escanear los entregados y elegir motivos individuales del resto de la visita. Comprobar que el no cargado está fuera de la visita.
6. Confirmar sin firma: debe rechazar localmente si se entrega alguno. Firmar, cambiar los entregados y comprobar que exige una firma nueva.
7. Adjuntar una foto si se exige y confirmar. Debe informar guardado pendiente. Recargar con la API sin conexión y comprobar que conserva el mismo resultado local.
8. Recuperar conexión y sincronizar. Verificar los estados, BultoIds de la evidencia y progreso en Backoffice. Reenviar la misma operación no agrega otro resultado ni recargo.
9. En otra prueba, cambiar un bulto en el servidor antes de sincronizar: la visita completa se rechaza, se conserva evidencia y se muestra el motivo por bulto. Otro envío independiente continúa.
10. Abrir rendición: los entregados no deben solicitarse como devolución física y el bulto que no salió no debe aparecer como intento fallido.

## Verificación automatizada

- `npm test` en scripts/tests incluye repartidor-visita.test.mjs: resultado mixto, foto única, borrador persistente, código ajeno/repetido, parcial prohibida, evidencia requerida, visita sin entregas, confirmación concurrente, rechazo detallado, rendición y carga de dos de tres.
- CargaParcialBultosTests verifica guardas y resumen del dominio.
- Rf32PwaTests usa PostgreSQL y sesión real: carga parcial, visita, foto compartida, regla descargada, estados y ausencia de falso aviso completo. Una segunda visita usa nueva foto; el reenvío no duplica el recargo.
- Se prueban además las regresiones de visita, carga, hoja y rendición, y las reglas de arquitectura.
- Chrome headless abre la PWA compilada con viewport móvil y API simulada: firma obligatoria e invalidada al cambiar entregados, foto real comprimida, guardado con API caída y reapertura con el mismo OperacionId. Esta prueba no equivale a haber ensayado la cámara física ni el circuito completo contra el servidor desplegado.

La compilación se ejecuta con `--artifacts-path TestResults/rf32-build` porque la API de desarrollo del usuario mantiene bloqueados sus binarios normales. No se detuvo esa API ni se cambió su base.

Resultados finales del 08/11/2026: compilación de la solución sin errores ni advertencias; 63 pruebas JavaScript, 455 unitarias, 56 de arquitectura y 76 de integración dirigidas aprobadas. Las pruebas de integración incluyen los dos escenarios nuevos de Rf32PwaTests y las regresiones de visita, carga, hoja, rendición y recargo. El ensayo de navegador compilado también pasó; no se ejecutó la suite completa de integración ni se probó cámara física en esta tarea.

### Corrección del borrador de visita reportada por Lucas

Se corrigió `anterior.bultos is undefined` al entrar en entrega con un borrador de carga sin confirmar. La clave local anterior diferenciaba sólo carga y rendición: las visitas colisionaban con la carga y entre envíos de la misma ruta. Ahora carga y rendición conservan sus claves históricas y cada visita usa una clave propia con el envío.

La recuperación detecta una visita antigua almacenada sobre carga, la vuelve a cifrar bajo su ID correcto y reconstruye la carga desde su operación confirmada cuando existe, en una transacción y sin tocar la cola. Si no existe operación de carga, preserva la visita y solicita revisar la carga; no inventa lecturas que se hubieran sobrescrito antes de esta corrección. No requiere borrar IndexedDB, claves ni operaciones.

La suite JavaScript pasa a 67 pruebas: agrega carga sin confirmar, dos visitas de envíos distintos en la misma parada, recuperación de la colisión con operación confirmada e idempotencia de la recuperación sin operación de carga.

### Corrección de envíos no cargados en rutas finalizadas (ENV-000072)

El cierre de CU-55 finalizaba la ruta pero sólo reintegraba envíos que habían viajado sin intento (T8). Un envío que no salió por falta de otros bultos conservaba `AsignadoARuta` y quedaba excluido de la planificación.

Ahora depósito puede marcar **«Verifiqué que todos los bultos de los envíos que no salieron están en depósito. Liberarlos para otra ruta»** al confirmar la rendición. En la misma transacción se cierra la ruta y se aplica T4 al envío completo que no salió; Envíos mueve sus bultos a `EnDeposito`, recalcula el resumen y registra la desasignación mediante su contrato y Outbox. No se genera visita, intento ni recargo. La ausencia de escaneo por sí sola no acredita ubicación: si no se marca la confirmación, se permite cerrar la ruta pero esos envíos permanecen bloqueados.

Para una ruta cerrada antes del arreglo, entrar con permisos de depósito a **Rendiciones → Confirmadas → Ver detalle**, verificar físicamente todos los bultos y usar **Liberar envíos no cargados**. La acción requiere la casilla de confirmación. Sólo aparece si hay envíos completos recuperables; consultar la página no modifica datos. Repetir el POST es inocuo y no libera un envío reasignado a otra ruta activa.

El historial se consulta desde Depósito, con 20 rendiciones por página y las más recientemente confirmadas primero, mostrando fecha de cierre, chofer y vehículo. Se quitó el enlace desde Rutas porque esa pantalla pertenece a Despacho: no es un acceso utilizable por el operario de depósito. Se mantienen los permisos de cada sección y el aislamiento por operador. Depósito consulta los cierres mediante el contrato de Ejecución (ADR-0001), sin acceder a su DbContext.

El servidor exige que todas las líneas del envío sean `NoCargado` (también admite lecturas `CargaParcial` del cliente antiguo si fueron recibidas al rendir), que representen exactamente todos sus bultos, que envío y bultos sigan `AsignadoARuta` y que no exista otra ruta activa. Usa el bloqueo de planificación por operador y la unidad de trabajo compartida (ADR-0001/0002/0003). Los envíos que viajaron, los subconjuntos de un envío y la conciliación individual continúan fuera de esta recuperación: no completa RF 34.

Verificación: pruebas de integración de cierre nuevo y antiguo, carga legacy recibida, ausencia de confirmación física, reenvío, reasignación posterior e aislamiento entre operadores. No se modificó la base local del usuario; para recuperar ENV-000072 hay que ejecutar la acción con la aplicación actualizada.

Resultado del arreglo: compilación de la solución con 0 errores y 0 advertencias; 55 pruebas de integración dirigidas aprobadas (`ConfirmarRendicionTests`, `ConfirmarCargaTests`, `IniciarRendicionTests` y `Rf32PwaTests`).

Después de corregir el acceso desde Depósito: compilación con 0 errores y 0 advertencias, 59 pruebas de integración dirigidas y 56 de arquitectura aprobadas. El recorrido de recuperación se prueba con un usuario de depósito cuyo acceso a Rutas responde 403, verificando además paginación, nombres de recursos de rutas cerradas y aislamiento del historial entre operadores.

### Inicio explícito y revisión de carga (08/11/2026)

Se separó la carga de la salida con **Comenzar ruta** y se corrigió el borrador con una revisión anterior de la hoja. El inicio se conserva sin conexión y va antes de las visitas en la cola; bloquea cargas posteriores. Las confirmaciones de carga pendientes mantienen identidad y payload al actualizar la hoja. [Flujo, compatibilidad y verificación](../cu-51/README.md#inicio-de-recorrido-separado-de-la-carga--ajuste-del-08112026): 72 pruebas JavaScript, 458 unitarias, 56 de arquitectura, 99 de integración dirigidas y comprobación de la PWA compilada en Chrome con API simulada.

### Integración de main y la rendición por bulto (09/11/2026)

Se integran la consulta `ConsultarParaRendicionAsync` con todos los bultos y estados, la identificación de los bultos de cada ruta y los avisos v1/v2 de main. La preparación de rendición, comparación de códigos y recuperación de no cargados utilizan esa consulta separada. Se conservan `BultoParaCarga.Estado`, `AdmiteEntregaParcial` y la interpretación de cargas nuevas: un bulto escaneado que finalmente no salió no exige recepción física al rendir.

La hoja HTTP se construye con el mismo manifiesto de la rendición: omite entregados de rutas anteriores y mantiene los asignados pendientes de cargar. La PWA conserva su validación estricta de que ambos conjuntos coincidan. No se incorpora todavía el filtro de la rama de Cristian en las consultas de planificación, carga y hoja; su integración debe contemplar la carga que continúa antes de Comenzar ruta.

El contrato nuevo `ReintegrarNoCargadosAsync` se invoca después de la confirmación explícita de ubicación y la comprobación de que no existe otra ruta activa. Se elimina la segunda liberación automática al cerrar, que habría eludido la casilla. En una revisita sólo se liberan los bultos asignados de esa ruta; los entregados anteriormente se preservan.

Verificación del merge: solución Release compilada sin errores ni advertencias; 143 pruebas de integración dirigidas, 489 unitarias, 56 de arquitectura y 72 de JavaScript aprobadas. Incluye la hoja HTTP de la revisita alineada con su rendición, recuperación con y sin confirmación física, conservación del entregado anterior, carga nueva bloqueada sin devolución ficticia y las pruebas incorporadas de avisos/devoluciones. El primer intento de integración no pudo iniciar por Docker detenido; se inició el motor y la ejecución posterior pasó. No se ejecutó en esta integración la suite completa de CI.

### Integración de consumidores de Cristian (PR #90, 09/11/2026)

Se incorpora `BultosQueAcompanan` para planificación/capacidad. La consulta de carga conserva el estado y `AdmiteEntregaParcial` y usa `BultosParaCarga`: excluye entregados anteriores y mantiene los todavía asignados cuando el envío ya quedó EnTransito por un primer lote. El comienzo explícito de ruta sigue cerrando nuevas cargas; esta selección no lo elude.

`IEnviosHojaRuta` sigue proporcionando los datos y estados completos como contexto. El filtro de pertenencia se aplica en el manifiesto HTTP de Ejecución, donde se conoce la ruta y sus escaneos: así no desaparecen los bultos entregados en la visita actual ni los que todavía se pueden cargar. La prueba de Cristian conserva las verificaciones de capacidad y carga del único pendiente; la selección final de la hoja HTTP se comprueba en `IniciarRendicionTests` y `Rf32PwaTests`.

La regresión de RF 32 ahora incluye confirmar un primer lote, actualizar la hoja, cargar el resto, comenzar, entregar parcialmente y volver por el último bulto. Verifica que la primera hoja conserve los resultados de esta visita, la segunda muestre únicamente el pendiente y el reenvío no duplique el recargo.

La integración detectó además una excepción al entregar el último reprogramado cuando otro bulto ya estaba EnDevolucion: esa visita no aporta un motivo fallido y se intentaba leer el primero de una lista vacía. Se evita iniciar una devolución desde un motivo inexistente y se comprueban los estados y avisos resultantes. El circuito de devolución individual sigue pendiente (punto 2 de la decisión).

Verificación final: compilación Release sin errores ni advertencias; 388 pruebas de integración de Envíos, Planificación, Ejecución y Depósito aprobadas, además de 490 unitarias, 56 de arquitectura y 72 de JavaScript. No se ejecutó la suite completa de CI en este merge.
