# Revisión del cambio de requerimientos y sus aclaraciones

| Dato | Valor |
| --- | --- |
| Equipo | Equipo 1: Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| Fecha de revisión | 31/10/2026 |
| Base de código revisada | Revisión inicial: `b119c3e`. Actualizada tras integrar `main`: `96b341e` (incluye `main` en `7bfa6e2`). |
| Estado | Análisis para conversar con el equipo; propuestas pendientes de acuerdo |
| Alcance del trabajo realizado | Lectura del documento, inspección del código y ejecución de pruebas existentes. Sin cambios funcionales. |

## 1. Conclusión para el equipo

La solución tiene una base de caché compatible con la aclaración: usa Valkey cuando está configurado, separa las claves por operador, publica métricas por caso de uso y permite consultar la fuente cuando falla el caché. **Tras integrar `main`, se ejecutaron 33 pruebas de integración: 33 aprobadas, 0 fallidas y 0 omitidas.** Incluyen las 25 iniciales y 8 de recepción y SignalR.

Esto no permite declarar cumplimiento integral demostrado. La prueba de distribución usa dos proveedores de servicios conectados a un Valkey real, no dos procesos completos de la API. Las pruebas funcionales de tarifario, reglas y seguimiento usan PostgreSQL real, pero caché en memoria. Falta unir esas evidencias en una demostración completa y verificar la visualización de métricas en el tablero.

La ausencia de una cadena de Valkey sigue habilitando memoria local sin distinguir el entorno. También quedan riesgos de datos viejos por concurrencia o invalidación fallida que ya reconoce el ADR de caché. **La falta del evento de recepción detectada inicialmente quedó resuelta en el código integrado de `main`: ahora se registra `EnvioRecibidoEnDeposito` en el Outbox y se incluye entre los eventos de invalidación.**

No se encontró una implementación de banderas de funcionalidad. **No es un incumplimiento obligatorio si el equipo no elige ese opcional.** El plan actual selecciona otros opcionales por 9 puntos y deja las banderas como reemplazo posible.

Para el nuevo alcance funcional, al ser tres integrantes, corresponde implementar solamente **entrega parcial de bultos, RF 31 a 36**. El retiro en domicilio, RF 37 a 41, no les corresponde.

## 2. Documentos de referencia y alcance de esta revisión

- Documento docente: *Cambios de Requerimientos Planificados.pdf*, leído completo desde la copia local aportada por Lucas. Secciones 2.1, 2.3, 3.1 y 3.2.
- [Letra original](Laboratorio__NET_2026.pdf), vigente en todo lo no modificado por el documento nuevo.
- [ADR-0006: estrategia de caché](ADR-0006-estrategia-de-cache.md).
- [Plan de casos de uso por hito](plan-casos-de-uso-por-hito.md).
- [Plan de trabajo y responsabilidades](plan-de-trabajo-y-responsabilidades.md).

La portada del documento nuevo fecha la presentación del cambio el **30/10/2026**, aunque se había anunciado para el 29/10. El cambio debe demostrarse el **12/11**; la entrega sigue siendo el **15/11 a las 23:59** y la defensa el **19/11**.

Los requisitos docentes se usan como criterios de evaluación del proyecto. Las recomendaciones de este informe son propuestas del análisis, no nuevas exigencias del docente ni acuerdos ya aprobados por el equipo.

## 3. Contraste de la aclaración de caché con el código y la ejecución

En esta tabla, «verificado» se limita al escenario efectivamente ejecutado. «Pendiente» significa que esta revisión no produjo evidencia suficiente; no implica por sí solo que el código falle.

| Criterio del documento | Implementación encontrada | Evidencia ejecutada y conclusión |
| --- | --- | --- |
| Caché distribuido, también para equipos de tres | `AddStackExchangeRedisCache` con Valkey cuando existe cadena de conexión; Compose la configura para API y Worker. No hay un nivel local adicional en ese camino. | Aprobó la prueba con dos proveedores independientes sobre Valkey real. **Verificado a nivel de infraestructura; pendiente la demostración con dos APIs completas.** |
| Dos casos con distintos perfiles de acceso | Seguimiento: expiración absoluta de 1 minuto. Tarifario: puntero vigente de 10 minutos y versiones de 24 horas. Las reglas usan también caché versionado. | Aprobaron las pruebas de seguimiento, tarifario y reglas. **Implementado; las pruebas de negocio ejecutadas usan caché en memoria.** |
| Claves separadas por operador | La pieza común construye `{nombre}:{OperadorId}:{clave}`, con prefijo del proveedor `logistica:`. Sin operador lanza `TenantMismatchException`. | Aprobaron aislamiento por operador y rechazo sin tenant. La prueba de aislamiento de la pieza común usa memoria compartida; las funcionales usan PostgreSQL. **Verificado en esos escenarios.** |
| Invalidar seguimiento al procesar eventos | El Worker registra un consumidor por tipo de evento; fija el tenant del mensaje antes de resolver el consumidor. Este elimina la clave del envío. | Aprobó una prueba que invoca directamente el consumidor. **Verificado el consumidor; esta prueba no recorre Outbox, RabbitMQ y Worker como procesos separados.** |
| Invalidación de recepción y devolución | Tras integrar `main`, `RecibirEnDepositoAsync` registra `EnvioRecibidoEnDeposito`; la confirmación de devolución registra `EnvioDevuelto`. Ambos están en `InvalidarSeguimiento.Tipos`. | La prueba de recepción comprueba que el evento se guarda una sola vez en el Outbox al completar la recepción. **La omisión detectada en la revisión inicial está corregida en código.** Sigue pendiente observar el recorrido completo hasta la invalidación por el Worker. |
| Tarifas versionadas e invalidación al publicar | Se guarda la publicación y después se elimina el puntero `vigente`. Las versiones publicadas no admiten editar reglas. El puntero incluye `ValidoHasta` para próximas vigencias. | Aprobó el recorrido de $150/v1 a $175/v2 y el caso de publicación futura que mantiene la versión anterior. **Verificado con memoria; falta verificar el cruce del instante de vigencia y el recorrido distribuido completo.** |
| Métricas por caso de uso en el tablero | Contadores `logistica.cache.aciertos`, `fallos` y `errores`, etiqueta `cache`. API y Worker registran el medidor y exportan por OTLP cuando está configurado. | Un `MeterListener` verificó valores de contadores. **Instrumentación comprobada; exportación y consulta en Aspire no verificadas en esta ejecución.** |
| Continuar contra la base si cae el caché | La pieza común captura errores de caché y ejecuta la función fuente. No revierte publicaciones si falla invalidar. | Aprobaron la caché simulada caída y Valkey inaccesible en `127.0.0.1:1`. **Verificado el fallback de infraestructura; no se detuvo un Valkey activo durante un flujo HTTP contra PostgreSQL.** |
| Cambio de tarifa visible inmediatamente en otra API | Valkey compartido y borrado del puntero permiten el escenario secuencial. No hay caché local en ese camino. | Se probaron por separado distribución e invalidación funcional. **No se ejecutó el escenario exacto con dos APIs y una publicación HTTP.** |
| Decisión fundamentada de expiración e invalidación | El ADR-0006 describe perfiles, claves, TTL, fallback, métricas y carreras conocidas. | **Documentado.** Debe actualizarse con el alcance preciso de esta ejecución y los pendientes, sin presentar pruebas de memoria como demostración distribuida completa. |

### 3.1 Archivos principales que respaldan el contraste

- [Registro de proveedores](../../src/BuildingBlocks/Logistica.BuildingBlocks.Infrastructure/Caching/CachingExtensions.cs).
- [Lectura, escritura, claves y fallback](../../src/BuildingBlocks/Logistica.BuildingBlocks.Infrastructure/Caching/CacheDistribuida.cs).
- [Métricas](../../src/BuildingBlocks/Logistica.BuildingBlocks.Infrastructure/Caching/MetricasCache.cs).
- [Configuración local compartida](../../docker-compose.yml), [API](../../src/Logistica.Api/Program.cs) y [Worker](../../src/Logistica.Worker/Program.cs).
- [Consulta pública cacheada](../../src/Modules/Seguimiento/Logistica.Modules.Seguimiento/Application/Features/ConsultarSeguimiento/ConsultarSeguimientoHandler.cs).
- [Consumidor de invalidación](../../src/Modules/Seguimiento/Logistica.Modules.Seguimiento/Infrastructure/Messaging/InvalidarSeguimiento.cs).
- [Registro de consumidores](../../src/Modules/Seguimiento/Logistica.Modules.Seguimiento/SeguimientoModule.cs) y [tenant del mensaje](../../src/BuildingBlocks/Logistica.BuildingBlocks.Infrastructure/Messaging/ConsumidorRabbitMq.cs).
- [Tarifario vigente](../../src/Modules/Administracion/Logistica.Modules.Administracion/Application/Features/ConfigurarTarifario/TarifarioVigente.cs) y [publicación](../../src/Modules/Administracion/Logistica.Modules.Administracion/Application/Features/ConfigurarTarifario/PublicarTarifarioHandler.cs).
- [Recepción en depósito](../../src/Modules/Envios/Logistica.Modules.Envios/Application/EnviosModuleApi.cs), método `RecibirEnDepositoAsync`.

### 3.2 Brechas y límites que no conviene ocultar

**Memoria local si falta configuración.** `CachingExtensions` llama a `AddDistributedMemoryCache()` cuando la cadena es vacía. El nombre de la abstracción no convierte esa memoria en un almacén compartido. Es útil para pruebas, pero el código no limita ese comportamiento a Development/Testing. Propuesta: hacer explícita la excepción de pruebas/desarrollo y detectar la falta de configuración en producción. Distinguir esto de un Valkey configurado pero temporalmente caído: en ese caso debe seguir operando contra la base.

**Recepción con evento: hallazgo inicial corregido por `main`.** `RecibirEnDepositoAsync` ahora publica el evento en el Outbox y `InvalidarSeguimiento` lo reconoce. La prueba `Al_recibir_todos_los_bultos_el_envio_pasa_a_EnDeposito` comprueba cero eventos tras el primer bulto y uno al completar la recepción. La prueba histórica `CacheSeguimientoTests` sigue modificando el estado directamente e invocando el consumidor con un evento simulado; no sustituye la prueba completa del recorrido de recepción por RabbitMQ. Al implementar estados por bulto, revisar cada nuevo evento visible. El ADR-0006 todavía contiene afirmaciones antiguas de que la recepción no publica eventos y debe alinearse con este código.

**Carrera entre lectura y publicación.** Una lectura puede obtener el puntero viejo de la base, otra solicitud publicar e invalidar, y la primera guardar después el puntero viejo. En ese caso el TTL puede mantenerlo hasta 10 minutos, salvo que `ValidoHasta` fuerce una recarga antes. Es un riesgo deducido del código y reconocido en el ADR; **no se reprodujo en esta revisión**. La aclaración de actualización inmediata justifica decidir cómo prevenirlo o demostrar que el escenario exigido queda cubierto. Usar Valkey no elimina por sí solo esta carrera.

**Invalidación durante una caída.** Si publicar confirma en PostgreSQL pero borrar la clave falla, al recuperarse Valkey puede conservar el puntero anterior hasta su expiración. El fallback protege la disponibilidad; no garantiza por sí solo frescura inmediata al recuperarse.

**Tiempo de fallback.** El proveedor configura `ConnectTimeout=2000 ms`, `AsyncTimeout=1000 ms` y `FailFast`. La prueba aprobada exige menos de 2 segundos para la segunda lectura con Valkey inaccesible, después de realizar una primera conexión. No demuestra que la primera lectura ni una solicitud completa tarden como máximo 1 segundo. Hay que precisar esa afirmación en el ADR.

**Telemetría durante fallos.** Una excepción al leer suma `errores`, no `fallos`. La tasa `aciertos / (aciertos + fallos)` representa las consultas exitosas al caché y debe observarse junto con errores para no interpretar una caída como buen rendimiento.

## 4. Pruebas de la revisión inicial (`b119c3e`)

Entorno: Windows, .NET SDK `10.0.401` y Docker Desktop Engine `29.9.2`. Docker no estaba disponible al comienzo; se inició Docker Desktop y se ejecutaron las pruebas con acceso al motor local. Los primeros intentos restringidos no constituyen resultados de pruebas. La ejecución final compiló correctamente y produjo reportes TRX.

Los contenedores PostgreSQL y Valkey fueron creados por Testcontainers para las pruebas. No se detuvieron servicios de la aplicación ni se modificaron sus datos de negocio.

| Clase | Pruebas | Resultado | Qué respalda |
| --- | ---: | --- | --- |
| `CacheDistribuidaTests` | 8 | 8 aprobadas | Aciertos/fallos/errores, aislamiento, tenant obligatorio, invalidación, no almacenar null, dos proveedores con Valkey real y fallback ante caché simulada caída o Valkey inaccesible. |
| `CacheSeguimientoTests` | 1 | 1 aprobada | Guardar la vista, conservarla mientras no se invalida y actualizarla al invocar el consumidor. PostgreSQL real y caché en memoria. |
| `ConfigurarTarifarioTests` | 4 | 4 aprobadas | Autorización, separación por operador, publicación y precio nuevo, publicación futura. PostgreSQL real y caché en memoria. |
| `ConfigurarReglasTests` | 5 | 5 aprobadas | Autorización, separación por operador, reglas nuevas, conservación de reglas del envío y uso por planificación. PostgreSQL real y caché en memoria. |
| `SeguimientoPublicoTests` | 7 | 7 aprobadas | Respuesta pública, privacidad, tokens, aislamiento y límite de consultas de una API. No demuestra un límite global entre instancias. |
| **Total** | **25** | **25 aprobadas** | Alcance limitado a estas clases; no se ejecutó la suite completa. |

El [factory de integración](../../tests/Logistica.IntegrationTests/PostgresApiFactory.cs) fija `ConnectionStrings:Valkey` vacío. Por eso las pruebas funcionales no validan distribución aunque usen la API real hospedada en memoria.

Comandos para reproducir desde la raíz del repositorio, con Docker disponible:

```powershell
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~CacheDistribuidaTests|FullyQualifiedName~CacheSeguimientoTests|FullyQualifiedName~ConfigurarTarifarioTests|FullyQualifiedName~ConfigurarReglasTests" --logger "trx;LogFileName=revision-aclaraciones.trx" --results-directory TestResults/revision-aclaraciones --verbosity quiet

dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --no-build --no-restore --filter "FullyQualifiedName~SeguimientoPublicoTests" --logger "trx;LogFileName=seguimiento-publico.trx" --results-directory TestResults/revision-aclaraciones --verbosity quiet
```

En un clon nuevo, ejecutar primero `dotnet restore Logistica.slnx` si aún no se restauraron las dependencias.

Evidencia local, ignorada por Git: `TestResults/revision-aclaraciones/revision-aclaraciones.trx` y `TestResults/revision-aclaraciones/seguimiento-publico.trx`. Los reportes corresponden a esta ejecución; para compartir resultados reproducibles, volver a correr los comandos o adjuntar los TRX.

## 5. Aclaración de banderas de funcionalidad

Se buscaron registros de `FeatureManagement`, feature flags y banderas en `src`, `tests` e `infra`. No se encontró una implementación. Las reglas operativas versionadas existentes no equivalen a este opcional.

| Condición para obtener los puntos | Estado encontrado |
| --- | --- |
| Dos funcionalidades reales activables por operador desde administración | No se encontró implementación. |
| Activación por administrador de plataforma | No se encontró gestión de banderas con ese alcance. No confundir con el administrador del operador. |
| Respeto en API, aplicaciones web, Worker y aplicación del repartidor donde intervengan | No se encontró implementación ni pruebas de propagación por capas. |
| Propagación entre instancias en un tiempo acotado y conocido, sin reinicio | No se encontró implementación ni evidencia. |
| Auditoría de quién cambió cada bandera y cuándo | No se encontró auditoría de banderas. |

**Recomendación:** mantener las banderas fuera del compromiso actual, salvo que el equipo decida sustituir un opcional. El plan selecciona API pública, cobertura, resiliencia y pruebas de extremo a extremo, por 9 puntos. Esta revisión no verifica que esos cuatro opcionales estén terminados. Si se cae cualquiera, el total elegido queda por debajo de los 8 puntos mínimos; las banderas no deben contarse hasta cumplir todas las condiciones.

El RF 33 exige configurar la admisión de entregas parciales como regla operativa. Eso sigue siendo obligatorio aunque no se implementen banderas.

## 6. Estado compartido adicional mencionado por la aclaración

La exención de balanceo para equipos de tres no elimina el caché distribuido. El documento también advierte sobre SignalR, protección de cookies, tareas programadas y limitación de tasa entre instancias.

En esta revisión estática se encontró un limitador de ventana fija de ASP.NET Core por IP en `Program.cs`: su contador es local a cada instancia. No se encontró una configuración explícita de claves de Data Protection compartidas. El servicio `DepurarPosiciones` usa un temporizador local; en el archivo revisado no hay coordinación distribuida de su ejecución. Esto no prueba por sí solo un error de negocio, ya que una limpieza repetida puede tolerarse, pero tampoco demuestra ejecución única.

**Actualización de `main`: sí existe ahora SignalR con backplane Valkey.** `SeguimientoModule` registra `AddSignalR()` y, cuando existe cadena de Valkey, `AddStackExchangeRedis()` con prefijo `logistica-tablero`. `TableroHub` autoriza Administrador/Despachador y toma la sala del operador desde la sesión. API y Worker registran el mismo módulo. Las pruebas `TableroEnVivoTests` usan un cliente SignalR real contra TestServer para comprobar sala por operador, posiciones y autorización; como el factory deja Valkey vacío, no demuestran comunicación entre dos procesos por el backplane. La observación inicial de que no se encontró backplane queda reemplazada por esta actualización.

Estos puntos deben revisarse si se despliegan varias instancias. **No se presentan como nuevos requisitos de balanceo para el equipo de tres**, ni se declara auditado todo el subsistema de tareas o seguridad. Tampoco se verificó un despliegue productivo: `infra/terraform` contiene únicamente `.gitkeep` en el checkout revisado.

## 7. Cambio funcional obligatorio: entrega parcial

| RF | Alcance docente | Impacto principal en la solución |
| --- | --- | --- |
| 31 | Estados por bulto; estado del envío derivado; «entregado parcialmente»; transiciones declarativas verificables. | Dominio de Envíos, tablas de transición, consultas, persistencia y migración de datos existentes. |
| 32 | Escanear bultos entregados; motivos individuales para no entregados; evidencia vinculada a entregados; funcionamiento sin conexión y conflictos por bulto. | PWA, almacenamiento local, contratos HTTP/internos, sincronización, evidencia e idempotencia. |
| 33 | Configurar y versionar si se admite parcial y si pendientes se revisitan con/sin recargo o se devuelven. | Administración, reglas de Envíos, datos descargados por la PWA y trazabilidad de recargos. |
| 34 | Asignar pendientes a una nueva hoja de ruta. | Planificación, reserva/asignación por bulto, carga del vehículo y cierre de visita. |
| 35 | Mostrar avance público sin identificadores internos. | Contratos y vista de seguimiento, eventos de invalidación y compatibilidad de datos cacheados. |
| 36 | Eventos de bulto en avisos con contrato versionado, sin afectar consumidores actuales. | Eventos, suscripciones, preparación de avisos, serialización, firma y pruebas de compatibilidad. |

La implementación actual tiene bultos sin estado, resultados de entrega por envío y un contrato de sincronización que no especifica resultados por bulto. Agregar únicamente el valor «entregado parcialmente» al enum del envío no cubre el cambio.

Ejemplo de aceptación: un envío sale con tres bultos; se escanean y entregan dos sin conexión; al tercero se le asigna un motivo del catálogo. Al sincronizar, se conservan los dos entregados y su evidencia, el envío muestra entrega parcial, el público ve «2 de 3», el comercio recibe el aviso de la versión correspondiente y solo el pendiente sigue la política de nueva visita o devolución. Reenviar la misma operación no duplica entregas, avisos ni recargos.

### 7.1 Decisiones que deben acordarse antes de implementar

1. Matriz completa para derivar estados del envío, incluyendo mezclas de entregados, pendientes, devueltos y extraviados. El documento no enumera todas las combinaciones.
2. Asociación entre visita, intento, bulto y prueba de entrega. Una firma puede respaldar varios bultos, pero debe quedar explícito cuáles.
3. Resolución de conflictos: operaciones sobre bultos distintos, repetidos o ya resueltos; aplicación atómica o parcial de un lote; preservación de evidencia rechazada.
4. Versión de reglas aplicable a los envíos existentes y nuevas visitas; cálculo e idempotencia de recargos.
5. Migración de datos históricos y tratamiento de operaciones que ya estén pendientes en dispositivos con el formato anterior.
6. Selección de versión del contrato de avisos y comportamiento del contrato anterior ante eventos y estados nuevos. No basta con agregar un campo `version` y cambiar el resto del mensaje.

Estas son decisiones propuestas de diseño; la letra no impone una representación técnica concreta para resolverlas.

## 8. Orden de trabajo propuesto y reparto entre los tres

| Prioridad | Trabajo | Responsable propuesto | Resultado revisable |
| --- | --- | --- | --- |
| 1 | Cerrar demostración de caché y riesgos de configuración/frescura | Ezequiel y Cristian | Valkey obligatorio en el entorno evaluado, evidencia de dos APIs, caída/recuperación y métricas visibles. |
| 2 | Acordar estados, reglas y contratos del cambio | Los tres; Lucas coordina el ADR | Matriz de estados, ejemplos de conflictos y contratos definidos. |
| 3 | Dominio por bulto, estado derivado y reglas versionadas | Cristian; Ezequiel acompaña persistencia | Reglas verificables y migraciones coherentes. |
| 4 | PWA, escaneo, offline, sincronización y planificación de pendientes | Lucas | Recorrido parcial completo y reenvío idempotente. |
| 5 | Eventos de bulto y avisos compatibles | Ezequiel; Lucas revisa contratos | Receptor antiguo compatible y receptor nuevo con eventos de bulto. |
| 6 | Progreso público y backoffice | Cristian | Estado derivado y cantidades visibles; sin identificadores internos públicos. |
| 7 | Demostración integrada, documentación y pruebas finales | Los tres | Escenario del 12/11 ensayado y ADR dedicado al cambio. |

El reparto es una propuesta basada en las áreas actuales, no una asignación ya aprobada. Lucas concentra varios frentes; el equipo debe ajustar según disponibilidad y evitar que Ezequiel absorba a la vez toda la integración y el despliegue.

## 9. Evidencia pendiente para cerrar la aclaración

- [ ] Ejecutar dos APIs completas con PostgreSQL y Valkey compartidos: precalentar la tarifa en ambas, publicar desde una y consultar inmediatamente desde la otra. Usar un tarifario/base de prueba.
- [ ] Verificar en Aspire los aciertos, fallos y errores separados por `seguimiento`, `tarifario` y `reglas`.
- [ ] Interrumpir un Valkey de prueba que estaba activo, consultar seguimiento y calcular tarifa contra la base, recuperarlo y comprobar frescura. No detener servicios compartidos sin coordinar con el equipo.
- [ ] Probar el instante en que comienza una versión programada con un reloj controlado.
- [ ] Resolver o acotar explícitamente la carrera de repoblación de caché y el dato viejo tras una invalidación fallida.
- [x] Incorporar el evento de recepción y registrarlo como evento que invalida seguimiento (resuelto por los cambios integrados desde `main`).
- [ ] Verificar el recorrido de recepción e invalidación completo a través de Outbox, RabbitMQ y Worker.
- [ ] Revisar la configuración productiva para impedir una sustitución accidental por memoria local.
- [ ] Actualizar el ADR-0006 con evidencias, límites reales de latencia y resultados de estos escenarios.
- [ ] Confirmar que las banderas continúan fuera del conjunto de opcionales elegido o planificarlas con todas las condiciones de la aclaración.

El ADR dedicado al cambio funcional es obligatorio y puede contarse dentro de los ocho exigidos. Debe registrar lo que efectivamente se modificó, qué resultó sencillo, qué no y por qué; este informe preparatorio no sustituye esa evaluación posterior.

## 10. Cambios integrados de `main` que afectan este análisis

Comparación: `git diff b119c3e 96b341e`. La revisión inicial quedó guardada en `a471053`; la integración posterior está en `96b341e`. Esta sección distingue las nuevas evidencias de los resultados históricos de la sección 4.

| Cambio recibido | Relación con lo conversado | Conclusión actual |
| --- | --- | --- |
| Evento `EnvioRecibidoEnDeposito`, publicación en T2 y suscripción de invalidación | Resuelve la omisión de recepción señalada inicialmente. | Corregido en código y con aserción de Outbox en la prueba de recepción; falta el recorrido completo con Worker. |
| CU-66: tablero SignalR con salas por operador y backplane Valkey (`ceffa63`) | Atiende el estado compartido del canal de tiempo real mencionado en la aclaración. | Implementado; el backplane entre procesos requiere demostración específica. |
| CU-61: reprogramación pública (`8e7aca9`) | Agrega una funcionalidad real que podría ponerse bajo bandera si se eligiera ese opcional. Invalida directamente el seguimiento después de reprogramar. | Mejora relacionada; no incorpora el sistema de banderas ni sus condiciones. |
| CU-31: confirmación de devolución al comercio (`90d8a22`) | Acepta marcar los bultos devueltos y registra una incidencia por faltantes; incorpora `EnvioDevuelto` en avisos e invalidación. | No equivale a RF 31–36: cierra la devolución del envío y no añade estado propio al bulto ni entrega parcial offline al destinatario. |
| CU-09: consulta y reintento de mensajes fallidos (`550538f`) | Amplía el soporte operativo de mensajería sobre el que se apoyarán los nuevos eventos. | No implementa contratos de avisos versionados para resultados por bulto. |
| ADR-0006 | Se ajustaron dos frases, pero aún dice que T2 no publica evento. | La documentación del ADR quedó desactualizada respecto del código integrado. |

No cambiaron en este intervalo la infraestructura de caché, el modelo `Bulto`, los contratos de sincronización del repartidor ni la PWA. Por eso siguen pendientes el fallback accidental a memoria en producción, las carreras de frescura y la implementación de la entrega parcial. Tampoco se encontró gestión de banderas, claves compartidas explícitas de Data Protection o un limitador distribuido.

### 10.1 Verificación ejecutada después de integrar `main`

Sobre `96b341e` se volvieron a ejecutar las cinco clases de la revisión inicial y se agregaron `RecibirBultoTests` (5 pruebas) y `TableroEnVivoTests` (3 pruebas). **Resultado: 33 aprobadas, 0 fallidas y 0 omitidas.** La prueba de recepción confirmó la escritura del evento en Outbox; las de SignalR confirmaron entrega en la sala correcta, recepción de posición y rechazo de perfiles no autorizados. Estas últimas usan un solo TestServer sin Valkey y no prueban el backplane entre procesos.

```powershell
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --filter "FullyQualifiedName~CacheDistribuidaTests|FullyQualifiedName~CacheSeguimientoTests|FullyQualifiedName~ConfigurarTarifarioTests|FullyQualifiedName~ConfigurarReglasTests|FullyQualifiedName~SeguimientoPublicoTests|FullyQualifiedName~RecibirBultoTests|FullyQualifiedName~TableroEnVivoTests" --logger "trx;LogFileName=revision-post-main.trx" --results-directory TestResults/revision-aclaraciones --verbosity quiet
```

Evidencia local: `TestResults/revision-aclaraciones/revision-post-main.trx`. No se ejecutaron en esta segunda revisión todas las pruebas de devoluciones, reprogramación pública o mensajes fallidos; sus cambios se inspeccionaron para determinar el impacto en el informe.
