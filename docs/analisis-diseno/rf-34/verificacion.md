# RF 34 · Verificación y prueba de aceptación

Fecha: 10/11/2026, America/Montevideo. Base: `04ee4a4` más el árbol de trabajo de RF 34. Los resultados automatizados corresponden a binarios aislados compilados en esta tarea. Posteriormente se actualizaron los ambientes local y LAN; sus verificaciones se registran por separado en [despliegue-2026-11-10.md](despliegue-2026-11-10.md).

## Resultado automatizado

| Comprobación | Resultado |
| --- | --- |
| Compilación de `Logistica.slnx` | Correcta; 0 advertencias y 0 errores. |
| Unitarias, suite completa | 498/498 aprobadas. |
| Arquitectura, suite completa | 56/56 aprobadas. Se conserva la regla de acciones entre módulos por ModuleApi. |
| Integración de RF 34 y regresiones seleccionadas | 225/225 aprobadas; 0 omitidas. No es toda la suite de integración de la solución. |

TRX locales, ignorados por Git: `TestResults/rf34/unitarias-final.trx`, `arquitectura.trx` y `aceptacion-automatizada.trx`. Las corridas anteriores de diagnóstico no son el resultado final.

La integración verifica PostgreSQL real: competencia por el mismo bulto, índice único, subconjuntos del mismo envío en rutas distintas, agregar otro bulto a la misma ruta, capacidad del manifiesto, revisiones obsoletas, selección vacía, idempotencia de crear/agregar/quitar/conciliar/cerrar, historial al quitar y reagregar, carga parcial, pertenencia de visita, retorno, faltante bloqueado tras cierre, permisos, antiforgery, aislamiento por operador y actualización de datos representativos. Se ejecutan además regresiones de planificación, ordenamiento, despacho, carga, hoja, visitas, sincronización, rendición, devolución, reprogramación pública, caché y avisos.

`InvalidacionSeguimientoTests.RF34_invalida_por_asignacion_y_retorno_individual_con_API_Worker_RabbitMQ_y_Valkey_separados` verifica el recorrido distribuido real: reservar 2/3 bultos y retornar uno actualiza el detalle público antes del TTL, aunque no cambie el resumen del envío. Los hechos internos nuevos no forman parte del contrato de avisos externos.

Para repetir las pruebas centrales desde la raíz del repositorio, con Docker disponible:

```powershell
dotnet build Logistica.slnx --artifacts-path TestResults/rf34/artifacts -m:1 -p:NuGetAudit=false
dotnet test tests/Logistica.UnitTests/Logistica.UnitTests.csproj --artifacts-path TestResults/rf34/artifacts --no-build
dotnet test tests/Logistica.ArchitectureTests/Logistica.ArchitectureTests.csproj --artifacts-path TestResults/rf34/artifacts --no-build
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --artifacts-path TestResults/rf34/artifacts --no-build --filter "FullyQualifiedName~Rf34|FullyQualifiedName~ConfirmarRendicionTests|FullyQualifiedName~RF34_invalida"
```

El último comando ejecuta el subconjunto central, no reproduce por sí solo el total de 225. Las pruebas crean contenedores/bases aislados; no requieren migrar la base del usuario.

## Qué exige cada fuente

| Fuente | Comprobación observable |
| --- | --- |
| Letra original, RF 13/14 | Ruta con repartidor/vehículo; restricciones y capacidad sobre los bultos seleccionados. |
| Letra original, RF 15 | Dos despachadores no pueden reservar el mismo bulto simultáneamente, adaptado a la granularidad introducida por el cambio. |
| Letra original, RF 18/19/22/24 | Hoja, escaneo, rendición y sincronización conservan su pertenencia y evidencia. |
| Cambio del 29/10, §2.1, RF 31/32/33 | Resultado por bulto, visita offline, evidencia de entregados y política/versionado del operador. |
| Cambio del 29/10, RF 34 | El pendiente físicamente disponible puede incorporarse a una nueva hoja; los entregados no vuelven a planificarse. |
| Acuerdo §4.2 y ADR-0007 D1/D2 | Una sola reserva abierta; no localizado bloqueado; reprogramado en vehículo bloqueado; liberación sólo con confirmación física y transacción. |
| Cambio del 29/10, §2.3 | Demostración offline integrada con backoffice, público y aviso al comercio. La aceptación RF 32 existente no se extiende automáticamente al nuevo recorrido RF 34. |

## Preparación de la prueba manual

1. Usar una base de prueba con las tres migraciones RF 34 y el build nuevo de API/PWA. Para una actualización de datos existentes, seguir D6 y el apartado de migraciones del [README](README.md). No basta con recargar la API anterior que estaba ejecutándose.
2. Usar un operador cuya versión de reglas admita entrega parcial y nueva visita. Registrar su plazo entre intentos y la política de recargo. Preparar un motivo reprogramable de su catálogo.
3. Crear un envío de tres bultos distinguibles, registrar la recepción física de los tres y conservar sus códigos. Usar repartidor con usuario vinculado y un vehículo con capacidad conocida.
4. Separar las sesiones de Despacho, Depósito y Repartidor. Para la prueba concurrente, disponer de otros recursos para que un conflicto de vehículo/repartidor no oculte el de bulto.

## Prueba A · Pendiente tras una entrega parcial

1. **Despacho:** crear y despachar R1 con los tres bultos. Comprobar peso/volumen y la hoja.
2. **Repartidor:** cargar los tres y comenzar la ruta. Sin conectividad, registrar una visita que entregue 1 y 2 y deje 3 con el motivo reprogramable. La firma debe corresponder a 1 y 2.
3. Recuperar conexión y sincronizar. Deben quedar 2/3 entregados y el tercero reprogramado. Reenviar la operación no debe duplicar resultados, intentos, evidencia ni recargo.
4. **Despacho:** intentar planificar el tercero antes de su retorno. Debe seguir bloqueado: `Reprogramado` no acredita disponibilidad física.
5. **Depósito:** confirmar la rendición escaneando el tercero que volvió. Debe cerrarse su reserva y conservarse `Reprogramado`, con el mismo intento fallido.
6. **Despacho:** elegir fecha/franja que cumplan el plazo y crear R2 con el tercero. La capacidad de R2 debe contar sólo ese bulto; los otros dos no deben aparecer como pendientes ni cargables.
7. **Repartidor:** abrir R2. Debe aparecer únicamente el tercero. Entregarlo genera evidencia nueva; las pruebas de la visita anterior deben conservarse. Al terminar, el progreso debe ser 3/3.

Resultado decisivo de RF 34: el pendiente se planifica en otra hoja después del retorno confirmado, manteniendo resultados/historial y sin una segunda reserva activa.

## Prueba B · Bulto no cargado y ubicación incierta

1. Crear otro envío de tres bultos, recibirlos y despachar una ruta.
2. Cargar sólo 1 y 2; declarar 3 como no localizado. El tercero no debe recibir intento fallido en domicilio ni recargo de carga.
3. Entregar 1 y 2 y cerrar administrativamente la ruta sin confirmar que 3 esté en depósito. El tercero debe continuar bloqueado para otra planificación.
4. En `/backoffice/custodia`, consultar el código del tercero. Registrar no localizado debe conservar el bloqueo y la reserva.
5. Cuando se encuentre físicamente, volver a consultar y confirmar presencia en depósito con las revisiones actuales. Sólo entonces debe quedar disponible para otra ruta, sin sumar intentos.
6. Planificar ese único bulto y comprobar ambas hojas. La ruta anterior no debe poder entregarlo por observar su nuevo estado en la ruta posterior.

## Prueba C · Dos despachadores y reenvíos

1. Abrir dos sesiones de Despacho y seleccionar el mismo bulto disponible, usando repartidores/vehículos distintos.
2. Prevalidar ambas propuestas y confirmar casi simultáneamente. Debe confirmar una; la otra debe recibir conflicto y exigir actualizar la selección.
3. Comprobar que el bulto pertenece a una sola ruta. El índice debe impedir dos reservas abiertas incluso con solicitudes simultáneas.
4. Repetir una solicitud con el mismo `OperacionId` y contenido debe devolver su resultado original. Cambiar el contenido reutilizando esa identidad debe rechazarse. Las pruebas automatizadas cubren también este caso y el cierre de rendición repetido.

## Evidencia y estado de aceptación

Guardar build/commit, versión de reglas, dispositivo/navegador, códigos de los tres bultos, R1/R2, OperacionId, pasos y resultados esperados/observados. No publicar firmas, fotografías ni documentos personales en la evidencia compartida.

La implementación y la aceptación automatizada de RF 34 están completas en este árbol de trabajo. Se ensayó la actualización sobre copias de ambas bases reales y luego se migraron y desplegaron los ambientes local y LAN. Permanece pendiente la aceptación manual de este recorrido con el build publicado. No se declaran completadas la devolución final individual al comercio (D3), liquidación (D8) o recuperación de captura (D9).
