# CU-65 · Consultar el historial de avisos y reenviar los fallidos

## Alcance y relación con CU-63 y CU-64

El comercio consulta los avisos destinados a su sistema receptor con el operador seleccionado. Puede ver el hecho
que originó cada aviso, sus intentos, códigos HTTP y errores, y solicitar un reenvío individual o por lote cuando
se agotaron los reintentos automáticos. La configuración del receptor y la firma pertenecen a CU-63; la entrega HTTP,
los reintentos automáticos y la cola de fallidos pertenecen a CU-64.

Se reutilizan `EntregaAviso`, `IntentoAviso`, `SuscripcionAviso`, `AvisoPendiente.v1` y el outbox de Seguimiento.
No se necesitan tablas ni migraciones nuevas. El receptor HTTPS público para la demostración de CU-64 sigue siendo
un pendiente de integración; consultar el historial y solicitar reenvíos no crea ese receptor.

## Consulta y presentación

- Portal: **Integraciones → Avisos automáticos → Historial**, en `/portal/integraciones/avisos/historial`.
- Listado de 20 registros por página, ordenado por fecha del evento descendente y por identificador descendente
  para resolver empates. La selección masiva comprende los avisos reenviables de la página actual.
- Filtros por estado del aviso y fecha del evento desde/hasta, ambos días incluidos. Administración proporciona
  la zona horaria del operador mediante su contrato de módulo. Se consultan límites UTC con final exclusivo del
  día siguiente y se presentan las fechas y horas del operador, incluyendo las de los intentos.
- El detalle distingue el estado del envío del estado del aviso y muestra URL del receptor, identificador e intentos
  ordenados por número, con fecha, resultado, código HTTP y error. Si no hubo respuesta HTTP se informa así.
- Las respuestas son los códigos HTTP y errores conservados por CU-64; no existe un cuerpo de respuesta HTTP
  persistido para mostrar. Los secretos de firma no forman parte de estos contratos ni consultas.
- El historial se lee en una transacción consistente. Listado, detalle y reenvío respetan los filtros de operador
  y comercio. Una entrega inexistente o ajena se responde con el mismo `404`.

## Qué avisos se pueden reenviar

| Estado | Reenvío manual | Motivo |
| --- | --- | --- |
| `Pendiente` | No | Ya espera procesamiento. |
| `Fallida` | No | CU-64 todavía gestiona los reintentos automáticos. |
| `Exitosa` | No | El receptor ya aceptó el aviso. |
| `EnColaDeFallidos` | Sí, con suscripción activa | Se agotaron los reintentos automáticos. |

Desactivar una suscripción impide solicitar reenvíos, aunque el aviso figure en la cola de fallidos. El Worker
también vuelve a comprobar la suscripción antes de enviar, como ya hace CU-64.

## Reenvío y transacción

1. El usuario selecciona uno o varios avisos y confirma la solicitud en el Portal.
2. La API exige sesión de comercio, operador seleccionado y token antifalsificación. Valida entre 1 y 50
   identificadores no vacíos; elimina duplicados dentro de ese límite.
3. En una unidad de trabajo, bloquea las filas de las entregas y suscripciones solicitadas con `FOR UPDATE`,
   incluyendo operador y comercio en la consulta. Adquiere los bloqueos en orden estable por identificador.
4. Valida todas las entregas y suscripciones antes de modificar nada. Si alguna es ajena, falta, está inactiva
   o ya no admite reenvío, rechaza el lote completo.
5. Llama a `EntregaAviso.Reintentar()` para pasar inmediatamente a `Pendiente` y registra un `AvisoPendiente`
   por entrega mediante el `IIntegrationEventPublisher` de Seguimiento. Guarda estado y outbox en la misma
   transacción. Una falla al confirmar revierte ambos.
6. Responde `202 Accepted`: la solicitud quedó encolada. El usuario puede actualizar para conocer su resultado.
   CU-64 procesa el mensaje y registra el nuevo intento; la respuesta `202` no afirma una entrega HTTP exitosa.

El cambio a `Pendiente` durante la solicitud evita que dos peticiones manuales concurrentes creen dos mensajes
para el mismo aviso. El mensaje de transporte es nuevo, pero su contenido conserva el `EntregaId`; el receptor
recibe el mismo identificador del aviso. No se borran los intentos anteriores ni se reinicia su numeración.
El sistema mantiene la semántica de entrega al menos una vez de CU-64: el receptor debe seguir deduplicando por id.

El consumidor de CU-64 ya admite procesar una entrega en `Pendiente`. También conserva su llamada defensiva a
`Reintentar()` para mensajes recuperados desde CU-09 que todavía estén en `EnColaDeFallidos`.

## API

| Método y ruta | Resultado |
| --- | --- |
| `GET /api/avisos/entregas?estado=EnColaDeFallidos&desde=2026-10-01&hasta=2026-10-21&pagina=1` | Listado paginado y cantidad total. Parámetros opcionales. |
| `GET /api/avisos/entregas/{id}` | Detalle e intentos, o `404`. |
| `POST /api/avisos/entregas/{id}/reenvio` | `202` para la solicitud individual, sin cuerpo de petición. |
| `POST /api/avisos/entregas/reenvios` | `202` para el lote; cuerpo `{"entregaIds":["<guid>","<guid>"]}`. |

Se reutiliza `GET /api/avisos/suscripciones/antifalsificacion` de CU-63 y se envía el token en
`RequestVerificationToken`. Los endpoints comparten la política de comercio autenticado y `Cache-Control: no-store`.
Una validación inválida devuelve `400`; sin sesión, `401`; sin los permisos o selección requeridos, `403`.
Ante errores de conexión al confirmar, el Portal pide actualizar el historial antes de volver a solicitarlo.

## Verificación

`HistorialAvisosTests`, sobre PostgreSQL en Docker, cubre filtros y bordes de fecha en la zona del operador,
paginación y orden, detalle sin secretos, intentos conservados, reenvío individual y masivo, solicitudes concurrentes,
rechazo de estados no reenviables y suscripciones inactivas, aislamiento de operador/comercio, lotes sin aceptación
parcial, rollback de estado y outbox después de guardar, autenticación y antifalsificación.

La entrega posterior y la estabilidad del identificador del receptor también están verificadas por las pruebas
de CU-64 (`EntregarAvisosTests`).

Verificación ejecutada el 21/10/2026: 248 pruebas unitarias, 56 de arquitectura y las 64 pruebas de integración
de Seguimiento, incluidas las 16 de CU-65, correctas. Portal compilado en Release sin advertencias. Revisión manual
en navegador con una base temporal: listado, detalle de respuesta HTTP y reenvío confirmado que pasa a *Pendiente*
conservando el intento anterior.

## Historial de versiones

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | --- | --- |
| 1.0 | 21/10/2026 | Implementación de consulta del historial y solicitudes de reenvío, integrada con CU-64. | Lucas Ottonello |
