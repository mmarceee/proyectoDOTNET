# Avance del CU-40

Fecha: 09/10/2026. Responsable: Lucas. Estado: implementado para desarrollo, pendiente de integración y cierre.

Lucas autorizó preparar CU-40 mientras Ezequiel termina el aislamiento por inquilino. Se verificó la implementación en origin/marce, commit ebea3bc, con 124 pruebas aprobadas; ver [revisión y ajustes de integración](revision-marce.md). Al resolver el merge iniciado por Lucas se incorporó el aislamiento al CU-40, conservando su participación transaccional y extendiendo el filtro a Outbox. El cierre sigue pendiente de sesión real y las comprobaciones restantes.

## Disponible

Se revisó y corrigió la organización del código según ADR-0001: casos en `Application/Features` y `Presentation/Features`, APIs intermodulares internas en Application y datos públicos en `Contracts/Results`. Ver [revisión de arquitectura](revision-arquitectura.md). Tras la reorganización, la compilación Release terminó sin advertencias ni errores y pasaron 170 pruebas (40 unitarias, 54 de arquitectura y 76 de integración).

| Área | Implementación |
| :--- | :--- |
| Dominio | Ruta y paradas; crear, agregar y modificar fecha/recursos en Planificada; Revision; ReservaActiva; evidencia histórica por confirmación. |
| Validación | Peso y volumen acumulados, medidas de recepción con respaldo declarado por campo, encaje individual con rotación, máximo de paradas vigente, fecha comprometida y franja aplicable al día. |
| Administración mínima | Repartidores, vehículos, zonas, versiones de franjas y máximo de paradas; persistencia, consultas y seed para el operador demo. |
| Envíos y Depósito | Campos de compromiso y medidas efectivamente recibidas; T3/T13, evento de estado y mensaje durable de asignación. |
| Concurrencia | Confirmación bajo bloqueo corto de la fila del operador; índice RF 15; reservas únicas por recurso/día; edición con RevisionEsperada; conflicto HTTP 409. |
| Atomicidad | Ruta, evidencia, estados/eventos de Envíos y Outbox comparten una transacción. Un fallo revierte el conjunto. |
| API | Recursos, candidatos paginados, prevalidación, creación, agregado, modificación, consulta de rutas e historial. |
| Backoffice | Listado por fecha, nueva ruta y detalle; filtros al cambiar y botón para limpiarlos, selección, prevalidación, confirmación, modificación y evidencia. Mantiene la selección al filtrar o limpiar; exige reconfirmación ante conflictos. |
| Persistencia | Migraciones PlanificacionCU40 en Administración, Depósito, Envíos y Planificación. |

El seed incluye dos repartidores, dos vehículos, una zona de Montevideo, franjas de mañana/tarde y un máximo de 20 paradas. La configuración se consulta desde Administración; Planificación no reemplaza las capacidades ni las reglas por constantes.

La fecha de una ruta nueva debe ser hoy o posterior, según la zona horaria del operador. El calendario de la pantalla limita las fechas y el servidor rechaza fechas pasadas al prevalidar y confirmar, incluso si se omite el control del navegador. Tampoco se permite mover la fecha de una ruta existente al pasado. Consultar rutas anteriores sigue permitido.

Verificación de esta regla: compilación sin errores ni advertencias y 175 pruebas aprobadas (40 unitarias, 54 de arquitectura y 81 de integración). Incluye rechazo por API y formulario, cambio de fecha sin persistir ante rechazo y límite del día local cuando UTC ya avanzó al siguiente día.

La franja comprometida se resuelve por fecha, siguiendo su cadena de reemplazos. Esto permite consumir el versionado de CU-04; no implementa su comando masivo, el control de 24 horas, T18/T19 ni los avisos. La selección de franja en el portal y los CRUD de Administración siguen siendo entregables de sus CU. Los envíos existentes sin franja no reciben una restricción inventada.

## Uso en desarrollo

Las pantallas están en `/backoffice/rutas`, `/backoffice/rutas/nueva` y `/backoffice/rutas/{id}`. La API vive en `/api/planificacion`.

Hasta contar con Identity, el acceso normal responde 401. Para probar localmente se requiere **ambiente Development** y habilitar explícitamente `Planificacion:HabilitarDesarrolloSinIdentity=true`. Los perfiles locales `http` y `https` de launchSettings.json habilitan esa opción para ejecutar desde Visual Studio o dotnet run. Si la aplicación ya estaba ejecutándose, hay que reiniciarla para tomar esas variables. Fuera de esos perfiles la opción está apagada por defecto y se ignora en Production. Las pantallas identifican el modo de desarrollo; no se crea un usuario ni un responsable ficticio.

Los perfiles locales también habilitan `Database:MigrateOnStartup=true`: al iniciar se aplican las migraciones pendientes de todos los módulos y luego se ejecuta el seed. Esto crea las tablas de Planificación y la configuración demo necesaria para probar rutas. PostgreSQL debe estar disponible antes de iniciar la API.

Con PostgreSQL configurado, se puede iniciar una instancia local con:

```powershell
dotnet run --project src/Logistica.Api -- --Database:MigrateOnStartup=true --Planificacion:HabilitarDesarrolloSinIdentity=true
```

Se utilizan las opciones locales de conexión ya existentes. No se cambió ni se reinició la instancia que Lucas tenía abierta. Las migraciones se comprobaron en bases descartables de pruebas.

Para reproducir las pruebas en Windows con Docker:

```powershell
docker run --rm --mount 'type=bind,source=C:\Users\lucas\Documents\GitHub\proyectoDOTNET,target=/repo,readonly' -v /var/run/docker.sock:/var/run/docker.sock --add-host host.docker.internal:host-gateway -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal mcr.microsoft.com/dotnet/sdk:10.0 bash /repo/scripts/cu40-en-docker.sh test
```

El script copia el checkout dentro del contenedor y compila allí, evitando los binarios bloqueados por la aplicación local. Testcontainers usa PostgreSQL 17.6 real.

## Integración pendiente

1. Integración realizada: ModuleDbContext, TenantSaveChangesInterceptor, InquilinoFijo y AislamientoTests de Ezequiel. PlanificacionDbContext recibe ICurrentTenant. Se conserva ParticiparEnTransaccionAsync, que CU-40 necesita antes del bloqueo SQL.
2. Guardas temporales retiradas de AdministracionModuleApi, RutaRepository, DepositoModuleApi y EnvioRepository. El filtro Tenant se aplica también a tipos IOperadorOwned que no heredan Entity, cubriendo OutboxMessage. Se agregaron pruebas A/B, sesión ausente y escrituras ajenas para rutas, paradas, evidencia y Outbox.
3. Adaptar el seed a InquilinoFijo. No sembrar filas de varios operadores con el contexto del request. Resolver la combinación de snapshots/migraciones del trabajo paralelo y comprobar creación desde cero y actualización desde la base anterior.
4. Ampliar las comprobaciones combinadas a recursos y operaciones HTTP con sesión real, además de las pruebas de aislamiento y concurrencia ya incorporadas. Revisar el SQL parametrizado del bloqueo: Operador es global y su ID se obtiene exclusivamente de ICurrentTenant.
5. Integrar Identity del trabajo correspondiente: operador resuelto desde claims, perfil Despachador, responsable desde NameIdentifier y middleware del host. El filtro de acceso actual ya exige autenticación/perfil fuera de desarrollo, pero no instala Identity. Añadir pruebas con usuarios de distintos operadores, dos despachadores del mismo operador y usuario de comercio. Mantener las comprobaciones antiforgery.
6. Conectar el publicador transversal del ADR-0003. EnvioAsignadoARuta.v1 ya queda en Outbox junto con la asignación; se conserva el mismo MessageId en fila y contenido. La publicación y el consumidor no se consideran implementados por guardar el mensaje.

Los métodos de dominio para quitar/despachar/iniciar/finalizar preparan las reservas para CU-41/43/51/55; no habilitan sus endpoints. Esos flujos deberán usar el mismo bloqueo del operador, revisar la versión de ruta y conservar su evidencia cuando corresponda. El cambio programado de franjas y sus notificaciones permanece en CU-04.

CU-40 se mantiene pendiente de cierre hasta integrar aislamiento y sesión real, ejecutar las pruebas de integración correspondientes y revisar los criterios de la [especificación técnica](especificacion-tecnica-cu-40.md).

## Verificación realizada

La solución combinada con marce compiló en Release con cero errores y cero advertencias. Suite completa después de resolver el merge: **155 pruebas aprobadas**, ninguna fallida ni omitida: 40 unitarias, 42 de arquitectura y 73 de integración.

La cobertura nueva comprueba límites exactos, sumas, encaje con rotación, fechas/franjas, historial independiente de las paradas, reservas y estados. En PostgreSQL verifica creación y T3/T13, recepción con respaldo parcial, capacidad acumulada, modificación fallida sin cambios, dos solicitudes por el mismo envío o recursos, dos agregados con igual revisión, revisión vencida, bloqueo por ruta EnCurso anterior y fallo inyectado después de guardar Envíos con reversión de eventos y Outbox. También comprueba el índice con SQL directo, prevalidación sin persistir, confirmación desde Razor, antiforgery, rechazo sin sesión y que la opción de desarrollo no habilita Production.

La suite incluye las pruebas transversales de Ezequiel y tres nuevas pruebas de aislamiento para Planificación y Outbox. Las pruebas HTTP con claims reales siguen pendientes de Identity. Estos resultados no certifican esa dependencia ni los flujos vecinos de CU-04/41/43/51/55.
