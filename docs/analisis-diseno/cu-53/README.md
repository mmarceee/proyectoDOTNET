# CU-53 · Reportar la posición del vehículo

Decisiones acordadas con Lucas el 21/10/2026. La implementación comprende la captura en la PWA, la sincronización, la persistencia en Ejecución y los contratos para CU-66. Cristian implementa el tablero y SignalR en Seguimiento.

## Comportamiento

- Con una ruta EnCurso, se intenta obtener ubicación al iniciar el seguimiento y cada 30 segundos mientras la PWA está visible. La ubicación del teléfono representa al vehículo asignado.
- La carga completa de al menos un envío, confirmada localmente en CU-51, habilita la captura aunque todavía no haya conexión para sincronizar. Una carga parcial sin ningún envío completo no inicia la ruta.
- El servicio vive en el layout común; cambiar de pantalla no lo detiene. Elige la ruta operativa entre las hojas guardadas, independientemente de cuál se esté mirando. Ante más de una candidata informa que se deben actualizar las rutas.
- Cada posición se guarda cifrada en IndexedDB como `ReportarPosicion`, con identificador, dispositivo, secuencia, hora de captura y correlación. Reutiliza CU-52, su autenticación, sus lotes de hasta 20, el orden y la idempotencia. La protección transaccional local impide dos registros dentro de 30 segundos entre pestañas, también sin Web Locks.
- Sin red, el dispositivo puede obtener ubicación y guardar capturas. Despacho sólo conoce la última posición sincronizada. La PWA necesita estar activa y visible; no se garantiza captura con pantalla apagada o en segundo plano. Véase la [especificación de geolocalización](https://www.w3.org/TR/geolocation/).
- Confirmar la declaración de rendición detiene la captura, incluso sin conexión. El borrador sin confirmar permite continuar. La captura no se reanuda automáticamente si la declaración resulta rechazada; se requiere revisar la jornada. CU-55 mantiene su cierre definitivo de ruta.
- Sin permiso, sin GPS o ante un error de almacenamiento se muestra un aviso y las demás operaciones siguen disponibles. Las capturas fallidas no generan posiciones inventadas.
- El reintento de envío se ejecuta también cuando falta permiso GPS o ya no hay ruta capturable. Ante fallos del servidor aplica espera creciente de 30 segundos hasta cinco minutos. Los disparadores de una misma pestaña comparten el envío en curso y, cuando existe Web Locks, también se serializan entre pestañas. Tras aplicar operaciones se vuelve a consultar la hoja de ruta.

## Servidor

`ReportarPosicion` entra por `POST /api/repartidor/sincronizacion`. No se crea otro endpoint de escritura. Se verifica el repartidor activo de la sesión, el operador, la ruta asignada y las coordenadas. El vehículo se obtiene de Planificación mediante `ConsultarParaPosicionAsync`; no se recibe del cliente.

Se guarda `ejecucion.posiciones_vehiculo`, de sólo inserción, y su `OperacionDispositivo` en la misma transacción. La posición usa el OperacionId como Id. Reenviar una operación aplicada responde `YaAplicada` sin insertar ni notificar otra vez. Una captura de una ruta no iniciada se rechaza.

Se conservan `CapturadaEn` y `RecibidaEn` en UTC. Se aplica la corrección de reloj de ADR-0004 si el dispositivo está adelantado más de dos minutos. La captura no puede ser anterior al despacho ni igual o posterior al inicio de rendición. La ruta debe estar EnCurso; se admite Finalizada cuando existe una rendición y la captura es anterior a ella. Esto permite sincronizar historial atrasado después del cierre sin reactivar el seguimiento. El modelo de Ruta todavía no tiene un instante de inicio: el límite inferior comprobable es el despacho; se exige además el estado operativo o la rendición que acredita el cierre.

Motivos específicos: `PosicionInvalida`, `PosicionFueraDeRetencion`, `RutaNoAsignada`, `RutaNoEnCurso` y `PosicionFueraDeJornada`. Se reutilizan los resultados y la revisión de rechazos de CU-52.

La regla temporal está en `Domain/Posiciones/JornadaSeguimiento`, no en el handler. El handler coordina las consultas y traduce el resultado del dominio. `Application/Mapping/PosicionMapper` adapta la entidad al contrato público sin acoplar la API del módulo a un handler de otra feature.

Los rechazos nuevos de CU-52 registran `ConflictoSincronizacionRegistrado.v1` en el Outbox de Ejecución, en la misma transacción que el resultado. Los reenvíos no generan avisos duplicados. Es distinto de `PosicionReportada`, que sólo se emite en memoria después del commit de una posición válida. La interfaz y revisión completa de rechazos GPS sigue pendiente: véase la auditoría enlazada al final.

## Retención y métricas

`Ejecucion:Posiciones:Retencion:Dias` vale 30 por defecto, configurable entre 1 y 3650 días. Variable de entorno: `Ejecucion__Posiciones__Retencion__Dias`.

La consulta excluye posiciones anteriores al plazo y el servidor rechaza capturas nuevas fuera de él. Cada hora el mantenimiento consulta los IDs de operadores activos a Administración, mediante su ModuleApi (Operador no tiene filtro Tenant, ADR-0002 §2.4). Para cada ID abre un ámbito nuevo, fija `InquilinoTrabajo` antes de resolver repositorios y elimina posiciones vencidas mediante `ExecuteDelete` con el filtro Tenant intacto (ADR-0002 §4). No ejecuta SQL global ni modifica la pertenencia de filas. Un fallo de un operador no impide procesar los restantes. La depuración física sólo recorre operadores activos; los puntos vencidos se excluyen de las consultas para cualquier operador.

La retención de posiciones no borra evidencia de operaciones rechazadas: ADR-0004 §2.5 y §6 exige conservar los conflictos hasta su revisión. Esta política es distinta de los 30 días del historial GPS aceptado. Las posiciones sincronizadas con éxito se eliminan de la cola local sin duplicar el GPS en recibos permanentes.

La PWA informa `Pendientes` en los lotes, incluidos los que transportan posiciones. El medidor `Logistica.Ejecucion` publica el histograma `logistica.sincronizacion.pendientes`: muestras de operaciones pendientes informadas antes de enviar cada lote. No representa un total global ni conoce dispositivos desconectados. La correlación existente de CU-52 incluye los reportes GPS.

## Integración y verificación

Véase [contrato y guía de integración para Cristian](integracion-cu-66.md).

Verificación local del 21/10/2026:

- 51 pruebas JavaScript pasan: incluye captura, visibilidad, permisos, cifrado, sincronización, cambio de sesión, corte al rendir, reintento independiente del GPS, espera creciente y envío compartido entre disparadores de la pestaña.
- 232 pruebas unitarias .NET pasan, incluidas reglas temporales en el dominio y el contexto inmutable de mantenimiento.
- 56 pruebas de arquitectura pasan; la API, los contratos y la PWA compilan.
- La migración `20261021201504_PosicionesVehiculo` está generada con EF Core, con su snapshot y restricciones geográficas.
- Las 14 pruebas nuevas de integración compilan, pero no se pudieron ejecutar: la inicialización de Testcontainers falla por falta de acceso al motor de Docker. Incluyen aviso Outbox idempotente, conservación de evidencia y depuración con dos operadores. Deben pasar antes de considerar verificada la persistencia real, la consulta de última posición, el aislamiento y la emisión posterior al commit.
- Pendiente prueba en teléfono: permiso de ubicación, GPS sin datos móviles, bloqueo de pantalla, reconexión y rendición sin red. No se aplicó la migración a una base de desarrollo ni se publicó la aplicación.

La aprobación de las pruebas estructurales no certifica cumplimiento completo del ADR-0004: véase [auditoría de arquitectura y documentación](revision-arquitectura.md) para los pendientes detectados en la sincronización compartida.

## Prueba manual del reporte de posición

CU-53 registra una posición cada **30 segundos**, no cada 30 minutos. No genera un informe periódico para descargar. El indicador de la PWA informa el estado; los puntos se comprueban en la sincronización y en la persistencia. El mapa corresponde a CU-66.

1. Ingresar a `/repartidor/` con el repartidor asignado a una ruta. Descargar su hoja y confirmar la carga de al menos un envío completo para que la ruta esté EnCurso. No confirmar todavía la declaración de rendición.
2. Permitir ubicación en el navegador y mantener la PWA abierta y visible. En desarrollo puede usarse localhost en la computadora; al entrar desde un teléfono se necesita un origen HTTPS seguro. La ubicación simulada del navegador sirve para comprobar el circuito sin desplazarse; mantener las mismas coordenadas no impide registrar capturas con horas distintas.
3. Abrir las herramientas del navegador (F12), pestaña Red/Network, filtrar por `sincronizacion` y observar durante 60–90 segundos. Abrir las solicitudes `POST /api/repartidor/sincronizacion`: en el cuerpo, buscar operaciones con `tipo: "ReportarPosicion"`, `rutaId`, `latitud`, `longitud` y `ocurridoEn`. Los lotes pueden incluir otras operaciones; no existe un POST separado para GPS.
4. En la respuesta, relacionar cada `operacionId` con su `resultado`: debe ser `Aplicada` o, si es un reenvío, `YaAplicada`. Un HTTP 200 por sí solo no acredita aceptación: revisar `motivo` y `detalle` si el resultado es `Conflicto` o `Invalida`. La captura inicial y los disparadores de reconexión/visibilidad, la espera del GPS y los reintentos hacen que el envío no sea un reloj exacto de 30 segundos.
5. Para comprobar lo que llegó al servidor, ejecutar esta consulta de sólo lectura en PostgreSQL, reemplazando ambos IDs por los del operador y la ruta probados. Ejecutarla otra vez después de 30–60 segundos: deben aparecer nuevas filas y horas de captura, aunque el vehículo esté quieto.

```sql
SELECT "Id", "VehiculoId", "Latitud", "Longitud", "CapturadaEn", "RecibidaEn"
FROM ejecucion.posiciones_vehiculo
WHERE "OperadorId" = '<OPERADOR_ID>'::uuid
  AND "RutaId" = '<RUTA_ID>'::uuid
ORDER BY "CapturadaEn" DESC, "Id" DESC
LIMIT 20;
```

6. Probar sin red manteniendo la página abierta y visible: la PWA debe indicar que conserva posiciones pendientes y la base no recibe puntos nuevos. Al reconectar, comprobar que se envían las capturas atrasadas; `CapturadaEn` conserva su momento original y `RecibidaEn` refleja cuándo llegaron. Para recargar o abrir la aplicación completamente sin red hace falta la PWA publicada: el service worker de desarrollo no guarda toda la aplicación.
7. Confirmar la declaración de rendición y esperar al menos 60 segundos con la PWA visible: no deben generarse capturas posteriores a esa confirmación. Todavía pueden sincronizarse puntos anteriores que estaban pendientes. Guardar sólo el borrador no detiene el seguimiento.

Si no aparecen puntos: comprobar permiso de ubicación, visibilidad, ruta EnCurso, hoja descargada y ausencia de una rendición ya confirmada. El indicador distingue falta de permiso, ubicación no disponible y problemas de sincronización. La cola local está cifrada: no se espera leer coordenadas directamente de IndexedDB sin pasar por el código de la aplicación.

## Comandos de pruebas automatizadas

Desde la raíz:

```powershell
npm --prefix scripts/tests test
dotnet test tests/Logistica.UnitTests/Logistica.UnitTests.csproj --no-restore
dotnet test tests/Logistica.ArchitectureTests/Logistica.ArchitectureTests.csproj --no-restore
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ReportarPosicionTests
```
