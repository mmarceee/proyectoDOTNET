# CU-54 · Iniciar la rendición

Implementado sobre PR #36 de Ezequiel, conservando su caché e instrumentación de trazas. [Diseño aceptado](diseno-cu-54.md).

## Flujo

El inicio Mis rutas presenta tarjetas por fecha y matrícula. Cada tarjeta abre su propia hoja, carga o rendición. Las cargas nuevas se limitan al día de la ruta; una Despachada anterior sin confirmación local queda pendiente de resolución por despacho. Las confirmaciones de carga ya guardadas se pueden consultar y sincronizar otro día. Rendir ruta aparece para EnCurso, incluso anterior; las futuras esperan su fecha y las finalizadas no ofrecen acciones operativas. El menú conserva Mis rutas, usuario y cierre de sesión. Las tarjetas leen el estado actual de Planificación, sin depender de la entrega del aviso interno de CU-43.

Desde la carga también se ofrece Rendir ruta cuando ya está en curso o una confirmación local incluye un envío completo. Así se puede continuar hasta la declaración sin red, aunque el servidor todavía informe Despachada. La preparación cifrada y la validación al sincronizar siguen comprobando las dependencias y el estado real.

La rendición presenta todos los bultos agrupados por envío. Para los no intentados, intentos fallidos de esta ruta y lecturas de una carga parcial, el repartidor declara expresamente si los devuelve; si no vuelven exige observación por bulto. Los nunca cargados se distinguen de los faltantes al regreso. Los entregados y otros resueltos son informativos y no se declaran como devolución.

La pantalla muestra el resumen y exige confirmar que termina la jornada de la ruta. Guardar genera IniciarRendicion en la misma cola cifrada de CU-51/52, con UUIDv7, secuencia y traceparent W3C propio. Primero indica pendiente de sincronizar y ofrece sincronizar desde la tarjeta. Después de Aplicada/YaAplicada informa que terminó el recorrido, con acceso a Ver declaración. Si hay bultos que debían volver, depósito debe verificar su recepción. Si no hay ninguno, la PWA aclara que sólo queda la confirmación administrativa: CU-55 presenta únicamente «Confirmar cierre de ruta», sin campo de escaneo ni comparación. Esto incluye el caso en que todos los bultos se entregaron. El cierre definitivo y la liberación de recursos siguen correspondiendo a CU-55; no se registran recepciones ficticias para poder cerrar.

El borrador se conserva por lectura/modificación y usa una clave diferente de la carga. Tiene control de revisión para varias pestañas. El alta de la operación y la confirmación del borrador son atómicas. Cambiar de cuenta no expone declaraciones de otra persona.

## Servidor

IniciarRendicion llega por POST /api/repartidor/sincronizacion. El coordinador toma el bloqueo de planificación por operador y abre una transacción antes de resolver idempotencia y ejecutar cada operación. Las operaciones existentes se suman a esa transacción. Esto serializa iniciar rendición frente a carga, entrega o intento, conservando el reintento por concurrencia optimista de Ezequiel.

Se comprueban vínculo activo, asignación, ruta EnCurso, manifiesto completo y sin bultos repetidos/ajenos, situación de cada bulto, observaciones y operaciones anteriores. La declaración no usa una revisión de ruta rígida que rechazaría la carga anterior de su propia cola: compara la clasificación actual, calculada por el servidor después de procesar sus dependencias.

Una transacción guarda Rendicion, LineaRendicion y OperacionDispositivo. Los índices impiden otra rendición de la misma ruta. Un reenvío conserva el resultado; una operación distinta no crea otra rendición. Recibida y EstadoFinal quedan nulos. CU-54 no aplica T8, no declara extravíos, no finaliza la ruta ni libera recursos; no genera un evento de transición de envío.

Una ruta con rendición registrada rechaza nuevas cargas, entregas e intentos por sincronización. La PWA también bloquea nuevas cargas desde la confirmación local. La consulta GET /api/repartidor/rutas/{rutaId}/rendicion exige repartidor, comprueba pertenencia y vínculo y responde no-store. La hoja descargada incorpora la rendición iniciada para recuperarla después de volver a ingresar.

## Datos y sincronización

La migración 20261019175011_Rendiciones crea ejecucion.rendiciones y ejecucion.lineas_rendicion sin modificar rutas o envíos existentes. Ejecución es dueño del agregado y la persistencia. Las consultas de otros módulos pasan por IEjecucionModuleApi, implementada internal en Application, con resultados en Contracts/Results. Envíos y Planificación se consultan por sus API.

La hoja pasa a schemaVersion 2 e incluye la base de rendición. Las copias de versión 1 siguen siendo legibles, pero para iniciar CU-54 necesitan una descarga nueva. IndexedDB pasa a versión 3, preservando hojas, claves, borradores y pendientes. Agrega recibos cifrados de operaciones aceptadas para reconstruir el resultado local si falla la actualización de la hoja después del ack. Estos recibos no son operaciones pendientes ni se reenvían. Su política de retención queda como mantenimiento posterior de CU-52.

La preparación local proyecta cargas, entregas e intentos anteriores de la cola sobre la hoja. La sincronización drena sucesivos lotes de hasta 20, en orden. La rendición lleva las dependencias de su ruta. El servidor vuelve a comprobarlas y busca rechazos no revisados de carga/entrega/intento incluso de lotes anteriores. No inicia la rendición sobre una operación rechazada. Revisar esos rechazos sigue correspondiendo al flujo de conflictos de CU-66; CU-54 no los da por resueltos automáticamente.

## Contrato para CU-55

IEjecucionModuleApi permite consultar una rendición por ruta y listar pendientes paginadas, siempre filtradas por operador. Devuelve ruta, repartidor, fecha, observaciones y líneas con envío, bulto, situación, declaración y observación; recepción y resultado final siguen pendientes.

CU-55 debe ampliar la API del dueño para registrar recepción/cierre y coordinar T8 con IEnviosModuleApi y la finalización con IPlanificacionModuleApi en la transacción compartida. Debe tomar el mismo bloqueo por operador antes de cerrar para competir correctamente con operaciones móviles. No debe escribir directamente en las tablas de Ejecución desde otro módulo. Su pantalla, escaneo, incidencias y cierre no forman parte de CU-54.

## Verificación

Las pruebas de integración usan PostgreSQL real y cookies de Identity. Cubren declaración con faltante observado, sin devoluciones, intento fallido frente a intento histórico, carga parcial, carga previa del mismo lote, rechazo de otro lote, pertenencia, manifiesto incompleto/ajeno, duplicaciones concurrentes, recuperación por consulta, reservas sin liberar y rollback antes del commit.

Las pruebas JavaScript cubren borrador cifrado, declaración explícita, observaciones, operación inmutable, corte y reenvío conservando correlación, varios lotes, competencia entre pestañas, abortar una escritura, compatibilidad de copias anteriores y separación de cuentas. También se ejecuta la regresión previa de CU-50/51.

- Regresión .NET: 98 pruebas unitarias y 56 de arquitectura correctas; integración, 300 correctas de 301 en la ejecución completa. La restante, de caché con Valkey inaccesible incorporada en PR #36, superó su límite de cinco segundos (6,02 s) mientras se construía la imagen y pasó al repetirla sola. No se cambió su umbral ni su implementación.
- Después de los últimos ajustes de recuperación y consulta, las 49 pruebas dirigidas de rendición/carga/sincronización/servicio pasaron. La preparación consulta sólo metadatos de operaciones anteriores, sin cargar firmas, fotos ni payloads rechazados.
- JavaScript: 31 pruebas correctas, incluidas las regresiones existentes. Se comprobaron operaciones de entrega/intento pendientes antes de rendir y un corte después de aplicar el primer lote de 20: el reintento conservó exactamente las dos operaciones restantes.
- Navegador publicado en 127.0.0.1:8080: usuario local de pruebas repartidor-cu51@envios-del-este.uy, vehículo CU51019 y ruta 01a1541f-d5c3-73f5-ab43-b1c17e86ab88. Se guardaron tres devoluciones y un bulto no devuelto con observación. API detenida: recarga del borrador, confirmación y otra recarga conservaron la operación pendiente. Al restablecer la API se sincronizó correctamente.
- PostgreSQL local: rendición 01a15558-e048-77c2-838b-57cfad9f638f, cuatro líneas, tres declaradas y las cuatro sin recepción ni estado final. La ruta permaneció EnCurso, revisión 5 y ReservaActiva=true. Estos son datos demo locales; no forman parte del seed automático ni se cerraron rutas anteriores.

### Ampliación Mis rutas · 19/10/2026

- Pruebas nuevas de consulta por ID y tarjetas: ruta anterior abierta, fecha operativa, estado actual sin aviso, orden de abiertas/finalizadas, paginación inválida, bloqueo de futuras/planificadas/finalizadas, otros roles y aislamiento entre repartidores y operadores.
- Servicio de tarjetas: falta de red, errores HTTP sin degradar a modo offline, invalidación local y respuesta tardía después de cambiar de cuenta. Las 24 pruebas dirigidas de descarga/selección/servicio pasaron.
- JavaScript: 33 pruebas correctas. Se agregó alternancia entre dos rutas, conservación de carga pendiente y de su operación, invalidación de descargas en curso al cambiar selección, copias cifradas sin duplicar y separación entre cuentas.
- Regresión: 105 pruebas dirigidas de ejecución y planificación, 98 unitarias y 56 de arquitectura correctas.
- Navegador publicado: repartidor@envios-del-este.uy ve la ruta pendiente del 17/10 y abre su carga (ESTE001, ENV-000009), sin modificar el envío. Al cambiar a repartidor-cu51@envios-del-este.uy, la tarjeta del 19/10 muestra Pendiente de depósito y Ver declaración, sin ofrecer nuevas cargas. La declaración registrada se recupera por su ID.
