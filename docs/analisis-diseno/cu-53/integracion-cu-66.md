# CU-53 → CU-66 · Integración para Cristian

Ejecución es dueño del historial de posiciones. Seguimiento implementa el tablero, sus consultas HTTP y SignalR. CU-53 no crea el hub ni el mapa. Los contratos están en `Logistica.Modules.Ejecucion.Contracts`; no hay que acceder a las tablas ni a las entidades internas de Ejecución.

## Obtener la posición inicial o recuperar una conexión

```csharp
var posiciones = await ejecucion.ConsultarUltimasPosicionesAsync(vehiculosIds, ct);
```

Es un método de `IEjecucionModuleApi`. Admite hasta 500 IDs por llamada y devuelve una posición por vehículo, dentro de la retención configurada. El filtro global usa al operador de la sesión; vehículos ajenos o sin posición vigente se omiten. La consulta conserva la última posición conocida aunque la ruta haya finalizado: el estado operativo lo determina CU-66 con Planificación y la rendición, no la existencia de un punto GPS.

`PosicionVehiculoResult` contiene: `Id`, `OperadorId`, `VehiculoId`, `RutaId`, `Latitud`, `Longitud`, `CapturadaEn` y `RecibidaEn`. Mostrar la hora de captura y marcar como antigua una ubicación que no se haya renovado según el umbral definido por CU-66. El silencio del GPS no demuestra por sí solo falta de conexión: puede faltar permiso, señal GPS o visibilidad de la PWA.

## Recibir actualizaciones

Implementar `IConsumidorPosicionReportada` en Seguimiento y registrarlo mediante DI:

```csharp
services.AddScoped<IConsumidorPosicionReportada, ActualizarPosicionTablero>();
```

El método `ConsumirAsync(PosicionReportada evento, CancellationToken ct)` recibe `evento.Posicion` y `evento.SoloHistorial`. Se invoca en memoria, después del commit de la transacción de CU-52. No usa RabbitMQ ni Outbox. Un error de un consumidor queda registrado y no revierte una posición ya persistida; tampoco se vuelve a publicar al reenviar el mismo OperacionId. Es una notificación efímera: consultar el estado persistido al conectar o reconectar.

Si `SoloHistorial` es true, la ruta ya estaba rendida o finalizada al recibir la captura: no reactivar el vehículo en el mapa. Aunque sea false, no reabrir una ruta que el tablero ya conoce como rendida: el cierre puede haberse producido después de procesar el reporte.

Para comparar puntos usar `CapturadaEn` y, en caso de empate, `Id` como desempate determinista. Una posición recibida después puede haberse capturado antes. Por ejemplo, recibir a las 15:00 una captura de las 12:00 no reemplaza una captura de las 14:59. No usar `RecibidaEn` como orden geográfico.

El consumidor de CU-66 debe enviar al grupo SignalR del `OperadorId` del evento. La pertenencia de cada conexión a ese grupo debe proceder de su sesión autorizada. Para evitar perder una actualización entre la carga inicial y la suscripción, conectar primero y reconciliar después con la consulta de últimas posiciones, comparando siempre por captura.

## Aviso durable de conflictos

ADR-0004 §2.5 exige `ConflictoSincronizacionRegistrado` por Outbox. El contrato está en Ejecución.Contracts/Events, con el nombre versionado `ConflictoSincronizacionRegistrado.v1`, el OperacionId, la ruta, el envío opcional, el repartidor, el resultado y el motivo. El sobre usa el operador y la correlación del contexto original mediante `AgregarAlOutbox`, como exige ADR-0003. Se registra una sola vez por rechazo nuevo; retransmitirlo no duplica el aviso. CU-66 debe declarar su suscripción/consumidor con Inbox y resolver el operador desde el sobre. El contenido de la operación rechazada permanece en Ejecución hasta su revisión; no se transmite su evidencia en este aviso.

## Verificación pendiente compartida

Las pruebas de integración de CU-53 están en `ReportarPosicionTests`: comprueban persistencia, idempotencia, evento posterior al commit mediante otra conexión, recepción de historial tras rendir, aislamiento y depuración entre operadores, evidencia conservada y aviso de rechazo en Outbox. Están pendientes de ejecución por indisponibilidad del motor de Docker en el entorno local del 21/10/2026. CU-66 debe agregar las pruebas del hub, los grupos por operador, la reconexión y el descarte de actualizaciones antiguas en el tablero. Los pendientes transversales detectados en CU-52 constan en la [auditoría](revision-arquitectura.md).
