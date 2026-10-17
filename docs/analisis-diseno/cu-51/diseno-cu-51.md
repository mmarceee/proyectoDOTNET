# CU-51 · Escanear la carga del vehículo

Propuesta de implementación para el 18/10/2026. Preparada el 17/10/2026; pendiente de revisión de Lucas. Este documento no acredita una implementación.

## Base documental y estado real

- CU-51 y T5: AsignadoARuta → EnTransito al confirmar la carga, sólo para envíos con todos sus bultos escaneados.
- ADR-0001: Ejecución coordina; cada módulo modifica sus propias tablas mediante Contracts.
- ADR-0002: operador y repartidor se obtienen de la sesión; aislamiento también en IndexedDB.
- ADR-0003: cambios y eventos se confirman junto al registro idempotente de la operación.
- ADR-0004: operaciones cifradas, ordenadas y persistentes; el servidor decide. Una transacción por operación.
- ADR-0005: PWA WebAssembly, uso sin red sobre una hoja descargada.
- CU-50 ya proporciona los códigos de los bultos y la copia cifrada.
- En esta rama CU-52 admite RegistrarEntrega y RegistrarIntentoFallido. ConfirmarCarga devuelve TipoNoSoportado; no existen aún su payload, EscaneoCarga persistido ni el contrato de aplicación de T5.
- El DTO ya tiene CorrelacionId, pero en esta rama no se encontró la continuación de esa traza por operación. Revisar los cambios de Ezequiel antes de implementar para evitar duplicarlos.

## Experiencia del repartidor

1. Desde Mi ruta, abrir Cargar vehículo para la ruta propia descargada.
2. Mostrar avance de bultos y envíos completos, pendientes y sobrantes.
3. Leer códigos con cámara o introducir el mismo código manualmente. Ambos caminos pasan por la misma validación local.
4. Esperado: pertenece a la hoja y cuenta una vez. Repetido: advertir, sin duplicar el escaneo ni el avance. Sobrante: advertir y pedir separarlo; no cuenta como cargado de esta ruta.
5. Persistir cada lectura como borrador cifrado. Cerrar o recargar no borra el avance; las pestañas no pueden sobrescribirse sin control de revisión.
6. Antes de confirmar, listar los envíos incompletos y sus bultos faltantes. Exigir confirmación expresa para continuar con faltantes, como define A3.
7. Al confirmar, guardar una operación inmutable en la cola cifrada. Sólo después indicar Carga guardada, pendiente de sincronizar.
8. Con conexión, intentar enviarla inmediatamente; también al abrir la app y recuperar conectividad. Reintentos serializados, sin depender de Background Sync.
9. Sólo la respuesta Aplicada/YaAplicada y la actualización desde el servidor permiten mostrar la carga como confirmada en servidor. Sin red se muestra claramente la confirmación local pendiente.

No exigir ubicación para confirmar carga: la obligación de RF 20 corresponde a entregas e intentos. Cámara denegada o no disponible no bloquea la entrada manual. En un teléfono real la cámara requiere un origen seguro (HTTPS); localhost del teléfono no es la computadora.

## Operación y evidencia

Extender OperacionDispositivoRequest con un payload Carga, manteniendo compatibles Entrega e IntentoFallido. Tipo = ConfirmarCarga, RutaId obligatorio y EnvioId = null porque la operación es de ruta.

Payload propuesto: RevisionRuta descargada, identificador de confirmación y lecturas únicas con CodigoBulto y EscaneadoEn. Un BultoId local puede ayudar a comprobar consistencia, pero el servidor resuelve el código y comprueba su pertenencia; no confía en IDs ni resultados enviados por el cliente. No enviar ni persistir faltantes como evidencia: se calculan por diferencia.

EscaneoCarga pertenece a Ejecución según el modelo. Guardar código leído, fecha, operación, operador y ruta, resultado Esperado/Sobrante y el vínculo al bulto cuando sea conocido dentro del operador. El modelo actual exige BultoId: ampliar el modelo y la persistencia para permitir un sobrante desconocido con código y BultoId nulo. No inventar un GUID ni consultar bultos de otro operador para completar esa referencia.

## Procesamiento en el servidor

1. Resolver idempotencia por OperacionId antes de repetir efectos.
2. Verificar operador, vínculo activo, repartidor asignado y ruta Despachada/EnCurso. Comparar la revisión de la hoja y rechazar una base incompatible para actualizar y revisar, sin reconstruir automáticamente la operación confirmada.
3. Validar formato y tamaño del payload, tiempos y códigos únicos. Clasificar esperados y sobrantes contra los datos del servidor; calcular faltantes y envíos completos.
4. En una transacción compartida: registrar escaneos, aplicar T5 a los envíos completos, guardar sus eventos EnvioEnTransito en outbox, actualizar la ruta a EnCurso cuando comience la ejecución y guardar la operación Aplicada.
5. Los incompletos permanecen AsignadoARuta. Una carga con faltantes confirmados por el usuario es un resultado válido, no un conflicto.
6. Una incompatibilidad de estado/pertenencia/revisión rechaza la operación completa; no deja algunos efectos confirmados y otros sin registrar. Se conserva el payload rechazado para CU-52.
7. Conservar los avisos de discrepancias de forma durable mediante un evento de integración. Su visualización en el tablero corresponde a CU-66; no darla por implementada sólo por publicar el evento.

Planificación expone la consulta de carga y la transición de ruta por Contracts; Envíos expone la transición T5 y es dueño de sus eventos. No modificar estados desde la PWA ni desde SQL ajeno al módulo.

Para posteriores confirmaciones de carga de la misma ruta, la hoja debe actualizarse antes de abrir un nuevo borrador. Reenviar la misma confirmación usa siempre el mismo OperacionId y no repite T5. Un envío ya cargado no se retrocede ni vuelve a cargar; los envíos aún AsignadoARuta pueden completarse en una nueva operación sobre la base actualizada. Verificar esta combinación con la revisión de ruta y los bloqueos de transacción.

## Persistencia y parte necesaria de CU-52

- Reutilizar el cifrado y la separación usuario + operador + repartidor de CU-50. La migración de IndexedDB conserva hojas y claves existentes.
- Separar borradores, operaciones y confirmaciones de servidor. La descarga nunca elimina operaciones pendientes.
- DispositivoId identifica la instalación; Secuencia se reserva de manera atómica entre pestañas. OperacionId es UUIDv7 generado al confirmar, no UUIDv4 de crypto.randomUUID().
- Cifrar el payload antes de guardarlo. Confirmación del borrador y alta de operación deben ser atómicas; fallar por cuota/cifrado no muestra éxito.
- Conservar los mismos datos, identidad, fecha, secuencia y correlación en todos los reintentos.
- Cola con Pendiente/Enviando/Aplicada/Rechazada; recuperar Enviando tras un cierre inesperado. Eliminar pendientes sólo tras Aplicada/YaAplicada. Mostrar y conservar rechazos hasta que se marquen vistos.
- 401/403 requiere nueva autenticación, preservando la cola cifrada del propietario. No transferir operaciones a otra cuenta.
- Enviar por POST /api/repartidor/sincronizacion, en orden y hasta 20 por lote. Actualizar la hoja después de aplicar; avisar y preservar la cola si falla esa descarga.

Esto implementa la parte de CU-52 imprescindible para demostrar CU-51 de extremo a extremo. No acredita todos los tipos de operación ni las métricas/tablero completos de CU-52/CU-66.

## Correlación acordada con Ezequiel

Al confirmar cada operación, generar y guardar con ella:

    CorrelacionId = 00-{traceId de 32 hex}-{spanId de 16 hex}-01

Usar bytes aleatorios criptográficos, hexadecimal minúsculo y rechazar IDs completamente en cero. No reutilizar OperacionId como traceparent ni generar una correlación nueva al sincronizar. Varias operaciones de un lote conservan correlaciones independientes.

El sufijo 01 activa sampled según W3C; la exportación efectiva depende de la instrumentación y el muestreo del servidor. Generar el encabezado no exporta por sí mismo un span del navegador.

Al procesar cada operación, el servidor inicia su actividad con el contexto guardado, y los eventos/outbox y Worker continúan esa traza. Si falta o es inválido, mantener compatibilidad con el comportamiento del servidor; el cliente nuevo siempre debe emitirlo válido. Revisar el PR de Ezequiel antes de cambiar esta instrumentación.

Referencia normativa: https://www.w3.org/TR/trace-context/

## Criterios de aceptación

- Cámara y entrada manual reconocen el mismo código; repetidos no cuentan dos veces y sobrantes no completan envíos.
- Una hoja con un envío de dos bultos y otro de uno permite comprobar carga completa y parcial.
- Reiniciar con la API detenida conserva borrador, confirmación pendiente y traceparent.
- Confirmar con faltantes advierte; sólo pasan a EnTransito los envíos completos después de sincronizar.
- Cortar la respuesta tras el commit y reenviar no duplica escaneos, transiciones ni outbox.
- Probar escrituras simultáneas desde dos pestañas, cuota agotada y migración de IndexedDB desde CU-50.
- Otro operador/repartidor, un vínculo desactivado y una ruta finalizada no pueden aplicar la carga.
- Probar un sobrante conocido del mismo operador y un código desconocido sin revelar datos de otro operador.
- Las correlaciones tienen formato W3C y sampled=01, son diferentes entre operaciones y estables en reintentos. Comprobar continuidad real hasta el consumidor cuando se integre la instrumentación de Ezequiel.
- Reabrir sin red sobre la versión publicada de la PWA, manteniendo el mismo origen; la versión de desarrollo no cachea el shell.

## Fuera de alcance

Entrega/firma/foto de CU-17, intento fallido de CU-18, posiciones de CU-53, rendición de CU-54/55, finalización de rutas y tablero completo de CU-66. CU-51 no libera al repartidor ni al vehículo.
