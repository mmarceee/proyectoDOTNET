# CU-43 · Anular un despacho anterior sin iniciar

Acuerdo e implementación: 20/10/2026. Responsable: Lucas.

Una ruta Despachada de un día anterior sin carga confirmada no podía cargarse por CU-51, modificarse por CU-40/41 ni rendirse por CU-54/55. El aviso «Pendiente de resolución por despacho» identificaba la situación, pero faltaba una acción. Esta ampliación permite volver a Planificada sin borrar la ruta ni registrar una carga o rendición ficticia.

## Condiciones y actor

Sólo el perfil Despachador del operador. La fecha de la ruta debe ser anterior al día operativo del operador, calculado con su zona horaria. La ruta debe seguir Despachada, conservar su instante de despacho y tener la revisión que vio el usuario.

Ejecución comprueba que no hay rendición, escaneos de carga (incluidos sobrantes) ni operaciones aplicadas de esa ruta. Una carga confirmada con cero envíos completos también bloquea la anulación, aunque la ruta continúe Despachada. Todos los envíos deben existir y seguir AsignadoARuta. EnCurso y Finalizada no admiten esta acción.

Despacho debe coordinar con el repartidor y confirmar expresamente que no hay carga física ni carga pendiente de sincronizar. Si hay una operación local pendiente, se sincroniza primero y se revisa su resultado. El servidor no puede inspeccionar colas de dispositivos sin conexión: la confirmación es una comprobación operativa del despachador, no una detección automática de la PWA.

## Flujo

1. Despacho abre la ruta anterior y despliega «Anular despacho sin iniciar».
2. Ingresa un motivo obligatorio de hasta 1000 caracteres y confirma la coordinación con el repartidor.
3. El servidor toma el mismo bloqueo de planificación por operador que CU-40/41/43 y CU-51 y vuelve a comprobar revisión, estado, fecha, actividad y envíos.
4. La ruta pasa de Despachada a Planificada, incrementa su revisión una vez y deja de mostrar un despacho vigente. Conserva envíos, visitas, fecha y recursos elegidos.
5. Se registra AnulacionDespacho: operador, ruta, fecha de ruta, instante del despacho original, revisión anterior/nueva, motivo, responsable e instante UTC. La evidencia anterior no se borra.
6. Se guarda DespachoAnulado.v1 en el Outbox de Planificación, en la misma transacción. Un error revierte estado, auditoría y mensaje.
7. Despacho puede cambiar fecha y recursos por CU-40 o quitar envíos por CU-41. Un nuevo despacho exige fecha vigente y las validaciones completas de CU-43.

Los envíos siguen AsignadoARuta y RF 15 impide asignarlos a otra ruta hasta retirarlos por CU-41. La ruta Planificada mantiene su reserva para la fecha original mientras tenga envíos; deja de bloquear recursos para días posteriores como una ruta despachada abierta. Cambiar fecha comprueba y actualiza las reservas; quitar el último envío las libera. No aplica T4 hasta retirar un envío, no aplica T8 y no finaliza la ruta.

## Sincronización y concurrencia

La exclusión por operador serializa anulación y carga. Si la carga confirma primero, la anulación falla por revisión/estado/actividad. Si la anulación confirma primero, la carga antigua no se aplica: Planificada no autoriza carga. Si la ruta vuelve a despacharse, su nueva revisión y fecha impiden usar la autorización anterior. El rechazo conserva el contenido y queda como conflicto para revisión según ADR-0004, sin borrar la cola del dispositivo.

Actualizar «Mis rutas» deja de ofrecer la ruta mientras está Planificada. Una copia local o aviso anterior puede seguir visible sin conexión; no autoriza operaciones en el servidor. El evento queda durable en Outbox; el publicador actual sólo envía tipos con suscripciones, por lo que DespachoAnulado.v1 permanece pendiente hasta incorporar un consumidor. La aplicación de esta anulación no depende de ese consumidor. Esta ampliación no agrega avisos push ni elimina avisos históricos.

## Contratos y persistencia

- POST `/api/planificacion/rutas/{id}/despacho/anulacion`: `{ revisionEsperada, motivo, confirmoSinCargaPendiente }`. Devuelve RutaResponse; 400 por datos/fecha/confirmación inválidos, 409 por revisión/estado/actividad/envíos incompatibles, 404 para ruta inexistente en el operador y 401/403 por acceso. Con cookie exige antiforgery.
- GET `/api/planificacion/rutas/{id}/anulaciones-despacho?pagina=1`: historial de 20 registros por página, protegido como las demás consultas de rutas.
- `IEjecucionModuleApi.TieneActividadRegistradaAsync`: consulta al dueño de la evidencia dentro de la transacción compartida. Planificación no consulta tablas de Ejecución.
- Tabla `planificacion.AnulacionesDespacho`: filtro Tenant, FK compuesta `(OperadorId, RutaId)` e índice único `(RutaId, RevisionRuta)`. La migración AnulacionDespachoCU43 sólo agrega esta tabla y sus índices; no cambia datos previos.

## Verificación

Pruebas de integración: reapertura y conservación de envíos, reservas, continuación por CU-41, estados/fechas prohibidos, motivo/confirmación/revisión, doble anulación concurrente, carga aplicada sin envíos completos, carga tardía conservada como conflicto, formulario y antiforgery, responsable, permisos y otro operador, y rollback de estado/auditoría/Outbox. Se usa PostgreSQL temporal, sin modificar rutas de desarrollo.

Resultado del 20/10/2026: las 17 pruebas de AnularDespachoTests aprobaron. La ejecución final dirigida, incluyendo las 8 de CacheDistribuidaTests, aprobó 25/25. La suite general previa aprobó 154 pruebas unitarias, 56 de arquitectura y 355/356 de integración; la única falla fue el umbral temporal de caché con Valkey inaccesible (6,1 segundos frente a un máximo de 5), que aprobó al repetir su clase sin la carga de la suite completa. Se verificó la migración en bases temporales y la compilación Release sin errores. Reiniciar la API con MigrateOnStartup aplica la nueva tabla; no se ejecutó una actualización manual sobre la base de desarrollo.
