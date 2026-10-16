# CU-43 · Despachar una ruta

Actualización: 16/10/2026. Incluye el despacho desde Backoffice y el aviso interno persistente en la aplicación del repartidor. No incluye la descarga de la hoja de ruta ni almacenamiento offline (CU-50), push o correo.

## Comportamiento

1. El despachador abre una ruta Planificada con paradas, valida las restricciones y confirma el despacho. La confirmación revalida recursos, reglas vigentes y medidas actuales. Tanto la prevalidación como la confirmación exigen que el repartidor tenga un usuario vinculado del mismo operador, con cuenta activada, activa y perfil Repartidor. Administración comprueba las cuentas por lote mediante ServicioUsuarios y comunica su habilitación por Contracts, sin exponer datos de Identity a Planificación. Ante un vínculo ausente o una cuenta no habilitada, se informa el problema y se conserva la ruta Planificada, sin evidencia de despacho ni evento. Crear o modificar la planificación antes de habilitar la cuenta sigue permitido.
2. La ruta pasa a Despachada con fecha/hora y evidencia. La planificación queda bloqueada. Los envíos conservan AsignadoARuta: sólo confirmar carga en CU-51 aplica T5 y los pasa a EnTransito. T4 sólo permite quitar envíos antes del despacho.
3. El mismo commit guarda RutaDespachada.v1 en el outbox de Planificación. Un fallo revierte estado, evidencia y evento juntos.
4. El publicador de la API envía el evento a RabbitMQ con confirmación del broker. Sólo entonces marca PublicadoEn. Si el broker falla, el evento sigue pendiente.
5. El Worker ejecuta el consumidor de Seguimiento. Fija el operador desde las cabeceras del sobre y guarda el aviso y la inbox en una transacción. Reentregar el mismo MessageId no repite el efecto. Un índice único por operador, ruta y revisión evita también duplicar el aviso con otro MessageId.
6. El repartidor inicia sesión en /repartidor/ y ve sus avisos, ordenados del más reciente al más antiguo. Puede actualizar y paginar. Cada aviso se presenta como «Ruta del dd/MM/yyyy» y muestra el momento del despacho, sin mostrar el GUID técnico. La ruta no tiene nombre ni numeración comercial propios en el modelo actual. El aviso es un hecho histórico, no una consulta del estado actual de ejecución.

GET /api/repartidor/avisos?pagina=1 devuelve hasta 20 avisos. Exige sesión autenticada y política Repartidor incluso con respaldo de desarrollo. El operador y repartidor salen de la sesión, nunca de parámetros del cliente. La respuesta usa Cache-Control: no-store. No se cachean avisos offline ni se exponen datos de destinatarios.

## Mensajería y responsabilidades

Se mantiene ADR-0003: el publicador vive en la API; el Worker sólo accede al módulo Seguimiento y a los Contracts de los demás módulos. Los contextos conservan los filtros Tenant y el interceptor de escrituras. El publicador usa un ámbito sin inquilino y sólo consulta outboxes técnicos.

La cola durable es seguimiento.notificar-ruta-despachada, enlazada a RutaDespachada.v1 en logistica.events. La API declara la cola antes de publicar, por lo que el aviso no se pierde si el Worker todavía está apagado. Las publicaciones son persistentes y usan mandatory y publisher confirms. El ack del consumidor se emite después del commit. Los errores transitorios van a colas de espera de 5 s, 30 s y 2 min; los mensajes inválidos y los que agotan esos reintentos van a logistica.fallidos. Si falla la publicación de un reintento, el original sigue sin ack.

En esta entrega el publicador selecciona sólo los tipos con suscripción registrada. Los eventos de otros casos de uso permanecen pendientes; no se marcan como publicados sin consumidores. El registrador de fallidos, su administración en Backoffice (CU-09), la retención y las métricas generales siguen perteneciendo al hito de mensajería del ADR-0003.

Seguimiento tiene su propia migración: avisos_ruta, inbox_messages y outbox_messages en el schema seguimiento. No se modifican tablas del dominio de otros módulos.

## Prueba local

Desde la raíz, con .env configurado:

```powershell
docker compose up -d --build api worker
```

La API aplica la migración al iniciar con Database:MigrateOnStartup=true. API y Worker requieren ConnectionStrings:Postgres y ConnectionStrings:RabbitMQ (Compose ya proporciona ambas). Al depurar fuera de Docker, ejecutar también el Worker. Si la API no tiene conexión RabbitMQ configurada, no inicia el publicador y conserva los eventos pendientes.

1. Ingresar como despachador, crear una ruta con envíos recibidos en depósito y revisar sus paradas.
2. Validar y confirmar el despacho. Verificar estado Despachada y ausencia de opciones de edición.
3. En otra sesión del navegador, ingresar como el repartidor asignado en /repartidor/ingresar. Abrir Avisos de rutas y pulsar Actualizar si el mensaje aún se está procesando.
4. Verificar el aviso. Una cuenta de otro repartidor u operador no debe verlo.

## Verificación automatizada

```powershell
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~AvisosRutaTests|FullyQualifiedName~DespacharRutaTests|FullyQualifiedName~PlanificacionIdentityTests"
dotnet test tests/Logistica.ArchitectureTests/Logistica.ArchitectureTests.csproj -c Release
```

Las cinco pruebas de avisos usan PostgreSQL y RabbitMQ reales en contenedores aislados: despacho y publicación antes de iniciar el Worker, persistencia, reentrega tras reiniciar el consumidor, recuperación del outbox después de una publicación fallida, reintento transitorio, rechazo de mensajes incompletos, aislamiento de operador/repartidor y autorización. Las pruebas existentes cubren rollback del despacho, revalidación y concurrencia con el retiro de paradas.

Las pruebas de despacho incluyen cuentas pendientes de activación, desactivadas, con otro perfil, de otro operador y vínculos a usuarios inexistentes, antes y después de una prevalidación válida. Los rechazos conservan estado y revisión, sin evidencia ni evento de despacho.

Verificado también en Docker Compose con la API y el Worker publicados: inicio de sesión del repartidor, aviso de un despacho real pendiente y actualización de la bandeja. Si el navegador conserva la plantilla de una versión anterior, cerrar sus pestañas de la PWA y volver a abrirla permite activar el service worker actualizado.
