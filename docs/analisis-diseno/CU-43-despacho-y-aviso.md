# CU-43: despacho y aviso al repartidor

El Backoffice valida las restricciones y muestra una confirmación. Al confirmar vuelve a validarlas, verifica la revisión y guarda en una misma transacción la ruta Despachada, su fecha, la evidencia de validación y `RutaDespachada.v1` en el Outbox de Planificación. No inicia el reparto ni cambia los envíos a EnTransito (CU-51).

El contrato está en `Logistica.Modules.Planificacion.Contracts/Events/RutaDespachada.cs`. Lleva RutaId, RepartidorId, Fecha, Revision y ResponsableId. MessageId, operador, fecha del evento y traza pertenecen al sobre del Outbox, según ADR-0003.

El aviso acordado es una notificación persistida en el servidor, consultable dentro de la PWA al abrirla o recuperar conexión: «Tenés una nueva ruta disponible», con acceso a la ruta. Push queda como mejora posterior. Sin conexión no se reciben avisos nuevos. Descargar una revisión permite considerar atendidos los avisos de esa ruta hasta esa revisión.

Pendiente de integración: publicador de la API hacia RabbitMQ, consumidor del Worker con inbox y reintentos, persistencia del aviso y consulta autenticada exclusivamente para el repartidor y operador de la sesión. No se expone una consulta anónima ni se confía en un RepartidorId enviado por el cliente.

Lucas tiene CU-43 y la presentación de la PWA. Trello y ADR-0003 asignan mensajería a Ezequiel para el 29/10; asignarle el consumidor específico del aviso es una propuesta pendiente de acuerdo. CU-43 corresponde al 22/10: hasta conectar el publicador, los avisos permanecen en Outbox. El login real depende de Identity; mientras tanto se conserva exclusivamente la opción de desarrollo existente de Planificación.
