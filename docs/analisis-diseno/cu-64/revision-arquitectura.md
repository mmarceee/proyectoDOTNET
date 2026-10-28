# CU-64 · Revisión del último commit de main

Revisión solicitada por Lucas el 21/10/2026. Se actualizó `origin` con `git fetch` y se revisó `04fab20ff1910a145136c1f59ea7ef61f40f5285` (merge de PR #48), comparado con su primer padre. La comprobación ejecutable se hizo sobre su integración local en `lukovski`, que incluye también CU-53.

## Resultado

No se encontraron violaciones de las dependencias de ADR-0001 en los cambios incorporados. Pasan las 56 pruebas de arquitectura y 248 unitarias de la integración. No se afirma cumplimiento completo de todos los ADR: hay un desvío formal en índices de ADR-0002 y queda pendiente la ejecución local de las pruebas con PostgreSQL/RabbitMQ.

| Decisión | Evidencia revisada |
| --- | --- |
| ADR-0001 §2.1–2.4: módulos, capas y vertical slices | EntregaAviso, IntentoAviso, EnvioSeguido y ClasificadorAvisos están en Domain de Seguimiento. Los handlers están en Application/Features/EntregarAvisos, usan puertos y no EF/ASP.NET. Los consumidores, EF y HTTP están en Infrastructure. Los eventos públicos son records de Contracts. Seguimiento usa Events de Envíos.Contracts; no sus entidades ni DbContext. |
| ADR-0001: BuildingBlocks técnico | MensajeIntegracion.Intento y su lectura desde la cabecera son datos técnicos de transporte, sin conceptos de logística. No se agregó dependencia de BuildingBlocks hacia módulos. |
| ADR-0002 §2.1–2.5: tenant confiable y aislamiento | Las tres entidades nuevas implementan IOperadorOwned e IComercioOwned. Los repositorios mantienen los filtros globales. El consumidor genérico fija InquilinoMensaje antes de resolver el consumidor concreto/DbContext. El comercio inicial procede del sobre de EnvioAdmitido; después se recupera de la proyección aislada, no de un tenant enviado en el contenido de otro evento. No hay nuevos IgnoreQueryFilters ni SQL global. |
| ADR-0003 §2.1–2.3: persistencia y Outbox | CrearEnvio registra EnvioAdmitido junto al envío dentro de su unidad de trabajo. PrepararAvisos crea entregas y AvisoPendiente en el Outbox de Seguimiento; AgregarAlOutbox toma tenant y traza del contexto. |
| ADR-0003 §2.4–2.6: Inbox, orden y reintentos | Preparación confirma Inbox, entregas y Outbox en una transacción. La entrega registra Inbox cuando termina; ante fallo HTTP conserva el intento y relanza para reintentar. Mantiene el Id de entrega para deduplicación del receptor. El transporte aplica 5 s/30 s/2 min y cola de fallidos. La falta de alta previa se reintenta sin presumir orden entre colas. |
| ADR-0004 | El commit no cambia la sincronización de la PWA. Sus pendientes documentados en la revisión de CU-53 no se consideran resueltos por integrar CU-64. |
| ADR-0005 y ADR-0006 | No cambia el modo WebAssembly ni agrega cachés de negocio. La copia persistida EnvioSeguido es una proyección del consumidor, no una entrada Valkey. |

## Hallazgo pendiente

**[P2] Incluir OperadorId en las restricciones únicas nuevas, según ADR-0002 §2.1.**

En `Infrastructure/Persistence/EntregaAvisoConfiguration.cs` el índice único de entrega es `(EventoId, SuscripcionAvisoId)` y el de intento es `(EntregaAvisoId, Numero)`. El ADR establece que las restricciones únicas locales al operador se compongan con OperadorId. Los identificadores padre son globales y esto no demuestra una fuga de lectura/escritura, pero los índices no siguen literalmente la convención acordada. Para alinearlos corresponde ajustar la configuración y crear una migración incremental, conservando las migraciones ya publicadas. No se cambió el esquema como parte de resolver el conflicto de bitácora.

## Límites funcionales ya documentados por CU-64

- Los envíos anteriores a EnvioAdmitido no tienen proyección y sus eventos pueden terminar en fallidos. Falta una estrategia de puesta en marcha para esos datos si se necesita notificarlos.
- T2, T12, T14, T15 y T16 aún no generan los eventos de integración que CU-64 necesita para avisar todos los cambios.
- Actualización del 28/10: se incorporó el [receptor de demostración 200/503](../../../tools/Logistica.ReceptorDemo/README.md).
  Sigue pendiente conectar el túnel HTTPS y ejecutar la demostración completa con el Worker.

Estas limitaciones no se presentan como violaciones de dependencias; impiden interpretar el commit como cobertura completa de todas las notificaciones.

## Conflicto de integración

El único conflicto fue `docs/bitacora-uso-de-ia.md`: ambas ramas agregaron la sección 3.35 y la versión 0.20. Se conservaron íntegros CU-53 y CU-64, numerando CU-64 como 3.36 y su versión como 0.22. Se agrega la entrada de esta revisión sin reemplazar los resultados históricos de pruebas de cada responsable.

## Verificación local

- 248 pruebas unitarias .NET y 56 de arquitectura pasan sobre la integración.
- Se intentaron las seis pruebas de EntregarAvisosTests, también fuera del sandbox. Todas fallaron al inicializar PostgresApiFactory y RabbitMqFixture: DockerUnavailableException al conectar con el motor de Docker, antes de ejecutar las comprobaciones funcionales. El resultado TRX local quedó en TestResults/cu64-main-review (ignorado por Git).
- Los resultados de 695 pruebas y cobertura informados en el README original de CU-64 pertenecen a la ejecución histórica de su autor; no se atribuyen a esta revisión.
