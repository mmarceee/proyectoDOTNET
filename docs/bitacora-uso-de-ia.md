# **Bitácora de uso de asistentes de IA**

| Estado | En curso: se agrega una entrada por cada componente trabajado con asistencia de IA |
| :---- | :---- |
| **Fecha** | 9 de octubre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Exigida por** | Letra del laboratorio, secciones 2 y 9.5, y entregables finales (sección 8) |

La letra pide declarar, **por componente relevante del sistema**, si se usó asistencia de IA, con qué finalidad, qué se aceptó sin modificaciones, qué se modificó y por qué, y qué se descartó (sección 9.5). Declararlo no tiene consecuencias negativas; omitirlo o falsearlo sí es una falta. Lo que se evalúa es la comprensión: cada responsable tiene que poder explicar y defender su componente, lo haya escrito o no con ayuda.

# **1\. Cómo se completa**

- **Una entrada por componente**, no por sesión. Si un componente se trabaja en varias sesiones, se agregan filas a su entrada.
- **La completa el responsable del componente**, con lo que realmente pasó. Si la IA escribió el código, se dice; si se reescribió a mano, también.
- **"Aceptado sin modificaciones"** es lo que quedó tal como lo propuso la IA. **"Modificado"** es lo que se cambió después, propuesto por la IA o corregido por el integrante, y por qué. **"Descartado"** son las propuestas o alternativas que no se usaron, y por qué.
- **Verificación:** cómo se comprobó que funciona (pruebas, revisión, demo).
- Las entradas de los componentes anteriores a esta bitácora (hito del 8/10) las completa cada responsable.

# **2\. Herramientas usadas**

| Herramienta | Quién la usa | Para qué |
| :---- | :---- | :---- |
| Claude Code (modelo Claude Opus 5.5), en la aplicación de escritorio | Ezequiel Marcenal | Revisión de código, explicación de conceptos, planificación de tareas y, cuando se pide expresamente, escritura de código, pruebas y documentación |

*(Cada integrante agrega las herramientas que usa.)*

# **3\. Entradas por componente**

## **3.1 Aislamiento por inquilino: filtro global "Tenant" (BuildingBlocks)**

| Campo | Detalle |
| :---- | :---- |
| **Responsables** | Ezequiel Marcenal y Cristian Reyes (tarjeta transversal del 15/10) |
| **Fecha** | 09/10/2026 |
| **Archivos** | `ModuleDbContext.cs`; `AdministracionDbContext.cs`, `DepositoDbContext.cs` y `EnviosDbContext.cs` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Entender cómo armar un filtro global genérico en EF Core 10 que lea el inquilino de cada request, y revisar el código escrito. |
| **Cómo se trabajó** | La IA explicó el diseño y dio los fragmentos de código. Ezequiel los escribió en los archivos. La IA revisó el resultado y marcó los errores, que Ezequiel corrigió. |
| **Aceptado sin modificaciones** | La estructura propuesta: las propiedades `OperadorIdActual` y `ComercioIdActual` en el contexto, el método genérico con `MakeGenericMethod`, el filtro con nombre `"Tenant"` y el orden del `if` (primero `IComercioOwned`). |
| **Modificado** | La lambda del filtro del comercio la escribió Ezequiel. La primera versión exigía `ComercioIdActual != null`, lo que dejaba al personal del operador sin ver ningún envío. Se corrigió a `(ComercioIdActual == null || e.ComercioId == ComercioIdActual)`, con paréntesis por la precedencia de `&&` sobre `||`. También se corrigieron `=` por `==` en las comparaciones y los tres DbContext de los módulos, que recibían el inquilino pero no se lo pasaban a la base. |
| **Descartado** | Leer el inquilino en una variable dentro de `OnModelCreating`: quedaría fijo con el primer request, porque EF construye el modelo una sola vez. Armar el árbol de expresión a mano con la API `Expression`: menos legible que el método genérico. Dos filtros separados para operador y comercio: `IgnoreQueryFilters(["Tenant"])` sacaría sólo uno. |
| **Verificación** | Pruebas en Docker (`scripts/test-en-docker.sh`). Al activar el filtro fallaron dos pruebas de CU-14, como se había anticipado (el contexto ya filtraba por el operador demo). Se corrigieron en la entrada 3.2. |

## **3.2 Aislamiento por inquilino: retiro de los filtros manuales**

| Campo | Detalle |
| :---- | :---- |
| **Responsables** | Ezequiel Marcenal y Cristian Reyes |
| **Fecha** | 09/10/2026 |
| **Archivos** | `EnvioRepository.cs`, `EnvioDetalleReader.cs`, `EnviosListadoReader.cs`, `ArchivoEvidenciaReader.cs`, `RecepcionRepository.cs`; pruebas `UnidadDeTrabajoTests`, `ConsultarDetalleEnvioTests`, `DetalleEnvioAmpliadoTests` y `DetalleEnvioPaginaTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Quitar los filtros de inquilino escritos a mano, ahora que los aplica el filtro global, y adaptar las pruebas para que el inquilino se le dé al DbContext. |
| **Cómo se trabajó** | A pedido de Ezequiel, la IA escribió los cambios. Antes explicó qué condiciones sacar y cuáles dejar. |
| **Aceptado sin modificaciones** | Todos los cambios, tal como los escribió la IA. |
| **Modificado** | — |
| **Descartado** | Dejar los filtros manuales junto al global: duplican la regla y obligan a cada reader a recibir `ICurrentTenant`. Seguir pasándole el inquilino al reader en las pruebas: con el filtro global, el que manda es el inquilino del contexto. |
| **Decisiones a poder explicar** | Las comparaciones contra la **sesión** se quitan; las comparaciones **entre filas** se quedan, porque son reglas de la consulta (por ejemplo, en `ArchivoEvidenciaReader`, que el archivo sea de ese envío). En ese reader, `e.OperadorId == operadorId` pasó a `e.OperadorId == a.OperadorId`. En las pruebas, el contexto con otro inquilino se arma con `ActivatorUtilities.CreateInstance`. |
| **Verificación** | Pruebas en Docker: 113 pruebas, todas pasan. |

## **3.3 Aislamiento por inquilino: `TenantSaveChangesInterceptor` e `InquilinoFijo`**

| Campo | Detalle |
| :---- | :---- |
| **Responsables** | Ezequiel Marcenal y Cristian Reyes |
| **Fecha** | 09/10/2026 |
| **Archivos** | `TenantSaveChangesInterceptor.cs` y `InquilinoFijo.cs` (nuevos), `ModuleDbContext.cs` (registro); pruebas `DetalleEnvioAmpliadoTests` y `DetalleEnvioPaginaTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar la protección de las escrituras de la sección 2.3 del ADR-0002. |
| **Cómo se trabajó** | La IA leyó el ADR-0002, propuso las reglas y, a pedido de Ezequiel, escribió el código y lo explicó línea por línea. |
| **Aceptado sin modificaciones** | El interceptor con sus reglas (sin inquilino se rechaza; un alta sin marcador lo toma de la sesión; no se puede cambiar el inquilino; no se puede escribir lo de otro inquilino), `InquilinoFijo` y el registro de una única instancia estática en `ModuleDbContext.OnConfiguring`. |
| **Modificado** | — |
| **Descartado** | Validar dentro de la sobrescritura de `SaveChangesAsync` de `ModuleDbContext`: el ADR nombra un interceptor. Una instancia de interceptor por contexto: EF armaría un proveedor de servicios interno por contexto. Validar en el interceptor que el comercio tenga relación activa con el operador: es una regla de negocio del caso de uso, y BuildingBlocks no puede conocer `RelacionComercial` (ADR-0002, sección 2.3). |
| **Decisiones a poder explicar** | Por qué se sobrescriben `SavingChanges` y `SavingChangesAsync`; por qué `ChangeTracker.Entries()` ve los bultos agregados por navegación (llama a `DetectChanges`); cómo se asigna una propiedad con setter privado (`entry.Property(...).CurrentValue`); qué no cubre (`ExecuteUpdate`, `ExecuteDelete` y SQL crudo). |
| **Verificación** | Pruebas en Docker: 113 pruebas, todas pasan. Dos pruebas que guardaban envíos de otro operador con el contexto del operador demo se adaptaron para usar un contexto del operador del envío. |

## **3.4 Aislamiento por inquilino: pruebas de aislamiento**

| Campo | Detalle |
| :---- | :---- |
| **Responsables** | Ezequiel Marcenal y Cristian Reyes |
| **Fecha** | 09/10/2026 |
| **Archivos** | `tests/Logistica.IntegrationTests/Tenancy/AislamientoTests.cs` (nuevo); `InternalsVisibleTo` en los `.csproj` de Depósito y Administración |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cumplir la prueba de aislamiento que exige la letra (§6.5) y la sección 4 del ADR-0002. |
| **Cómo se trabajó** | La IA propuso la lista de casos y, a pedido de Ezequiel, escribió las 11 pruebas y explicó qué demuestra cada una. |
| **Aceptado sin modificaciones** | Las 11 pruebas: lecturas con dos operadores y dos comercios sobre los DbContext de Envíos, Depósito y Administración; una prueba por regla del interceptor; y la prueba estructural de que toda entidad `IOperadorOwned` tiene el filtro `"Tenant"`. |
| **Modificado** | — |
| **Descartado** | Probar sólo el DbContext de Envíos: el ADR pide cubrir los de todos los módulos. Una prueba genérica por reflexión sobre todas las entidades: más difícil de leer y de defender que pruebas explícitas por módulo. Repetir el 404 de CU-14, que ya cubre `DetalleEnvioPaginaTests`. |
| **Verificación** | Pruebas en Docker: 124 pruebas (30 unitarias, 42 de arquitectura y 52 de integración), todas pasan. |
| **Pendiente** | La parte (a) de la prueba estructural del ADR-0002 (toda entidad con `OperadorId` implementa `IOperadorOwned`). |

## **3.5 Documentación: guía del aislamiento por inquilino**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 09/10/2026 |
| **Archivos** | `docs/guia-aislamiento-por-inquilino.md` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Dejar por escrito cómo funciona y cómo se usa el aislamiento, para el equipo (en especial para Cristian, que sigue con el seed e Identity) y para preparar la defensa. |
| **Cómo se trabajó** | A pedido de Ezequiel, la IA redactó la guía a partir del código y del ADR-0002. |
| **Aceptado sin modificaciones** | La estructura y el contenido. |
| **Modificado** | La propia IA corrigió una afirmación de la primera versión: decía que la `TenantMismatchException` ya se traducía a 404, cuando eso todavía no está implementado. |
| **Descartado** | — |
| **Verificación** | Revisión contra el código y el ADR-0002. |

## **3.6 Reporte de cobertura en el pipeline (opcional 7.2)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 12/10/2026 |
| **Archivos** | `coverage.runsettings` (nuevo), `.config/dotnet-tools.json`, `.github/workflows/ci.yml`, `scripts/test-en-docker.sh`, `.gitignore`, `README.md` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cumplir la condición del opcional de cobertura: "reporte publicado por el pipeline", medido sobre las capas de dominio y aplicación. |
| **Cómo se trabajó** | La IA propuso el enfoque y escribió la configuración. Ezequiel corrió el script en Docker y verificó el reporte. |
| **Aceptado sin modificaciones** | Medir con el colector coverlet (ya referenciado en los proyectos de prueba), filtrado a los namespaces `Domain` y `Application` de los módulos; unir los tres proyectos de prueba con ReportGenerator como herramienta local; publicar el resumen en la página de la ejecución y el reporte HTML como el artefacto `reporte-cobertura`; generar el mismo reporte en `coverage/` desde `scripts/test-en-docker.sh`. |
| **Modificado** | — |
| **Descartado** | Medir toda la solución: incluiría Infrastructure, Presentation y migraciones, y el número no diría nada sobre lo que pide el opcional. Usar la acción de GitHub de ReportGenerator: la herramienta local da el mismo reporte en el CI y en la máquina de cada uno. Un umbral que haga fallar el pipeline: se agrega cuando se acerque el 12/11. |
| **Verificación** | Ejecución de `scripts/test-en-docker.sh` del 12/10: 4 ensamblados y 38 clases medidas; cobertura de líneas del 91,5 % (388 de 424) y de ramas del 74,5 % (258 de 346). El reporte en el CI se verifica en el primer PR. |

## **3.7 Tabla outbox por módulo (ADR-0003)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal (acordado con Lucas Ottonello, que había creado el outbox de CU-40) |
| **Fecha** | 12/10/2026 |
| **Archivos** | `OutboxMessage.cs` y `ModuleDbContext.cs` (BuildingBlocks); `EnviosDbContext.cs`, `IEnvioRepository.cs`, `EnvioRepository.cs` y `EnviosModuleApi.cs` (Envíos); `Events/EnvioAsignadoARuta.cs` (nuevo, Envios.Contracts); migraciones `OutboxPorModulo` de Administración, Depósito, Envíos y Planificación; pruebas `OutboxTests` (nueva), `AislamientoPlanificacionTests` y `ArmarRutaTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Alinear el outbox de CU-40 con los ADR: sin filtro "Tenant" (ADR-0002, 2.4), una tabla por módulo registrada desde `ModuleDbContext` (ADR-0003, 2.1) y las columnas del sobre (ADR-0003, 2.6). |
| **Cómo se trabajó** | La IA detectó el conflicto al revisar CU-40 y redactó la propuesta para el equipo. Lucas la aprobó. Después, la IA escribió el código, generó las migraciones en Docker y corrió las pruebas. |
| **Aceptado sin modificaciones** | `OutboxMessage` sin `IOperadorOwned`, con `OperadorId`, `ComercioId`, `TraceParent`, `Tipo`, `OcurridoEn`, `Contenido` y `PublicadoEn`; `ModuleDbContext.AgregarAlOutbox` como único punto de escritura, que toma el inquilino de la sesión y falla cerrado; índice parcial de pendientes; evento `EnvioAsignadoARuta` en Contracts; `RegistrarAsignacion` sin el parámetro `operadorId`. |
| **Modificado** | La prueba del outbox de `AislamientoPlanificacionTests`, que verificaba el filtro, se reemplazó por `OutboxTests`, como pidió Lucas: el publicador lee los pendientes sin inquilino, los metadatos son los de la sesión, sin operador no se puede registrar, el evento se revierte con el cambio y cada módulo tiene su tabla. En `ArmarRutaTests`, la consulta que buscaba el `MessageId` dentro del contenido ahora verifica el tipo y que esté pendiente. |
| **Descartado** | Mantener el filtro en el outbox: el publicador, sin inquilino, no vería nada, y para leer necesitaría `IgnoreQueryFilters`, que el ADR-0002 prohíbe. Recibir el operador por parámetro: el sobre tiene que salir de la sesión, nunca del llamador. Implementar ahora `IIntegrationEventPublisher`, el publicador y la tabla inbox: corresponden al 29/10. Cambiar `Contenido` a `jsonb`: la conversión de la columna existente complica la migración sin beneficio actual. |
| **Verificación** | Pruebas en Docker: 178 pruebas (40 unitarias, 54 de arquitectura y 84 de integración), todas pasan, incluida la de CU-40 que comprueba que la asignación y el evento se revierten juntos. |

## **3.8 ADR-0003 versión 1.0**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 12/10/2026 |
| **Archivos** | `docs/analisis-diseno/ADR-0003-outbox-y-mensajeria.md`; CU-09 en `docs/analisis-diseno/casos-de-uso.md` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Completar el ADR para su aprobación: resolver los huecos de la versión 0.1 y alinearlo con el ADR-0002, con CU-09 y con lo ya implementado. |
| **Cómo se trabajó** | A pedido de Ezequiel, la IA identificó los huecos y redactó la versión 1.0 a partir de la letra (RNF 6.8), los ADR-0001 y 0002, CU-09 y el código. |
| **Aceptado sin modificaciones** | El outbox sin filtro y con el inquilino tomado de la sesión; `IIntegrationEventPublisher` declarada en cada módulo; semántica *al menos una vez* sin orden garantizado; inbox con clave (`MessageId`, `Consumidor`); reintentos con colas de espera con TTL; cola `logistica.fallidos` con un registrador que la vuelca en la tabla `mensajes_fallidos`, con filtro de inquilino, para CU-09; el sobre en las propiedades y cabeceras de AMQP; retención, observabilidad y tabla de verificación con su estado. |
| **Modificado** | — |
| **Descartado** | Las alternativas 3.5 a 3.10 del ADR, cada una con su motivo. |
| **Verificación** | Revisión de la coherencia con el ADR-0002 (secciones 2.4, 2.5 y 4), CU-09 y el código del outbox. El equipo lo aprobó el 12/10, incluidas las tres decisiones nuevas (la tabla `mensajes_fallidos` con su registrador, `IIntegrationEventPublisher` por módulo y el sobre en cabeceras AMQP). |

## **3.9 Respuesta 404 ante una escritura de otro inquilino**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 12/10/2026 |
| **Archivos** | `src/Logistica.Api/Errores/TenantMismatchExceptionHandler.cs` (nuevo), `Program.cs`; prueba `Tenancy/EscrituraDeOtroInquilinoTests` (nueva) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cumplir la sección 2.9 del ADR-0002: una escritura sobre datos de otro inquilino responde igual que un recurso inexistente, y queda registrada para auditoría. Hasta ahora terminaba en un 500. |
| **Cómo se trabajó** | A pedido de Ezequiel, la IA escribió el manejador siguiendo el patrón de `DomainExceptionHandler` y una prueba de integración, y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | Un `IExceptionHandler` que responde 404 **sin cuerpo**, igual que los `NotFound()` de los endpoints, y registra un *warning* con método, ruta, inquilino de la sesión y la excepción; el inquilino se pide al request porque los manejadores son singleton. En la prueba, un repositorio que entrega una recepción de otro operador, para que la rechace el interceptor real. |
| **Modificado** | — |
| **Descartado** | Responder 404 con ProblemDetails: el cuerpo distinto delataría que el recurso existe. Responder 403: lo prohíbe el ADR. Lanzar la excepción a mano desde un endpoint de prueba: no probaría que la excepción del interceptor llega al manejador. |
| **Verificación** | Pruebas en Docker: 179 pruebas (40 unitarias, 54 de arquitectura y 85 de integración), todas pasan. La prueba nueva compara la respuesta con la de un envío inexistente y verifica que no quedaron ni la recepción ni la transición T2. |

## **3.10 ADR-0004 · Resolución de conflictos de sincronización**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 12/10/2026 |
| **Archivos** | `docs/analisis-diseno/ADR-0004-resolucion-de-conflictos-de-sincronizacion.md` (nuevo); CU-52 en `casos-de-uso.md` (v0.6); `propuesta-stack-y-entorno-desarrollo.md` (v1.3); nota de RF 24 en `modelo-de-dominio.md` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Redactar el ADR obligatorio de la letra (sección 8.2) sobre la sincronización de la PWA, que necesitan CU-17, CU-18 y CU-52 y la cola local de la PWA. |
| **Cómo se trabajó** | La IA analizó la letra, los casos de uso y la tabla de transiciones, e identificó qué cambios puede tener un envío en el servidor mientras el repartidor está sin conexión. Planteó tres decisiones con una recomendación; Ezequiel eligió, discutió el caso del extravío y decidió conservar T16 desde *EnTransito* por los robos. Después la IA redactó el ADR. |
| **Aceptado sin modificaciones** | Sincronizar operaciones y no estados; idempotencia por `OperacionId` registrada en la misma transacción que el efecto; el servidor decide con la máquina de estados (elegido por Ezequiel); los archivos dentro de la operación (elegido por Ezequiel); una transacción por operación; conservar las operaciones rechazadas sin aplicarlas; la hora del dispositivo con corrección del reloj adelantado; las reglas para la PWA, el transporte, la correlación y las métricas. |
| **Modificado** | La IA proponía como conflicto principal un extravío declarado por error mientras el envío estaba en reparto. Ezequiel objetó que un paquete no se extravía en la camioneta, pero que T16 debe conservar *EnTransito* para los robos. El caso guía del ADR pasó a ser el robo, con una entrega anterior sin sincronizar. |
| **Descartado** | La última escritura gana, el dispositivo gana, la revisión manual de todo, sincronizar estados, descartar las operaciones rechazadas, el lote atómico, subir los archivos aparte, depender sólo de la Background Sync API, y CRDT o event sourcing (alternativas 3.1 a 3.9 del ADR). Se evaluó sacar *EnTransito* de T16 y se descartó (decisión de Ezequiel). |
| **Verificación** | Revisión contra la letra (RF 24, 6.11, 6.12, 9.3), CU-17, CU-18, CU-21, CU-41, CU-43, CU-50 a CU-52 y la tabla de transiciones. El equipo lo aprobó el 15/10 (versión 1.0). |

## **3.11 Actualización de seguridad de los paquetes de .NET 10.0.13**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 14/10/2026 |
| **Archivos** | `Directory.Packages.props`, `.config/dotnet-tools.json` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Destrabar la compilación. El 13/10 se publicaron CVE-2026-102870 y CVE-2026-96230 (ejecución remota de código en ASP.NET Core) contra `Microsoft.AspNetCore.Components.WebAssembly.Server` hasta 10.0.12. Con `TreatWarningsAsErrors`, la auditoría de NuGet (NU1903) hacía fallar la restauración. |
| **Cómo se trabajó** | La IA consultó los avisos en GitHub, verificó que existiera la versión 10.0.13 de cada paquete, los actualizó, actualizó la imagen del SDK y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | Subir a 10.0.13 los ocho paquetes fijados en 10.0.12 y `dotnet-ef`, para que todos queden en la misma versión de parche. |
| **Modificado** | — |
| **Descartado** | Subir sólo el paquete afectado, porque dejaba versiones de parche mezcladas. Silenciar la advertencia NU1903, porque es una vulnerabilidad real. |
| **Verificación** | Imagen `sdk:10.0` con ASP.NET Core 10.0.13; 212 pruebas (46 unitarias, 54 de arquitectura y 112 de integración), todas pasan. |

## **3.12 Dominio de CU-17 y CU-18 en `Envio`**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 15/10/2026 |
| **Archivos** | `ReglasEntrega.cs` y `MotivoNoEntrega.cs` (nuevos), `Envio.cs` (`RegistrarEntrega`, `RegistrarIntentoFallido`, `ReintentoDesde`); migración `ReintentoDesde` de Envíos; prueba `EntregaEIntentosTests` (nueva); `modelo-de-dominio.md` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar en el agregado las reglas de la entrega (T6) y del intento fallido (T7 con T9 o T10), sin depender de las reglas operativas (CU-06) ni de la sincronización (CU-52), que todavía no existen. |
| **Cómo se trabajó** | La IA propuso el diseño y los pasos para que Ezequiel los escribiera; Ezequiel pidió que lo escribiera la IA. La IA escribió el código, la migración y las pruebas, y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | Las reglas entran como parámetro (`ReglasEntrega`, `MotivoNoEntrega`), copias inmutables de la versión de reglas; validar todo antes de cambiar nada; número de intento asignado por el servidor; origen *AppRepartidor* para T6 y T7, y *Sistema* para T9 y T10; `ReintentoDesde` igual a la hora del intento más el plazo; el envío no crea la `Devolucion` (otro agregado), la crea quien llama. |
| **Modificado** | Después, acordado con Lucas: `ReintentoDesde` se agregó al contrato `EnvioPlanificable` para que Planificación lo haga cumplir al armar y al despachar rutas. La prueba de integración de ese contrato mostró que Npgsql sólo acepta `DateTimeOffset` en UTC y la hora del dispositivo trae su desfase local: se normalizan a UTC `CreadoEn`, `EventoEnvio.OcurridoEn`, `IntentoEntrega.FechaHora`, `PruebaEntrega.CapturadaEn`, `Devolucion.IniciadaEn` y `ReintentoDesde` (ADR-0004 v1.1, sección 2.7). |
| **Descartado** | Que el envío consulte las reglas a Administración (el dominio no depende de otros módulos); que `Envio` cree la `Devolucion`; guardar la reprogramación sólo en el texto del evento, porque el plazo no se podría hacer cumplir; un conversor global de `DateTimeOffset` en `ModuleDbContext`, porque puede afectar la traducción de las consultas y oculta la regla. |
| **Verificación** | 15 pruebas unitarias nuevas y 1 de integración (`EnvioPlanificableTests`); 263 pruebas en total (61 unitarias, 54 de arquitectura y 148 de integración), todas pasan. Cobertura: 93,3 % de líneas y 79,2 % de ramas. |

## **3.13 Política de CU-30 en la pantalla del Backoffice**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 15/10/2026 |
| **Archivos** | `Deposito/Presentation/Features/RecibirBulto/Index.cshtml.cs`; prueba `Deposito/RecepcionPaginaAutorizacionTests` (nueva) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Al revisar las políticas por perfil, la IA detectó que la API de CU-30 exigía *OperarioDeposito* pero la pantalla del Backoffice no: el middleware deja entrar a todo el personal, así que un despachador podía recibir bultos desde la pantalla. |
| **Cómo se trabajó** | La IA agregó la misma política a la página y una prueba con sesiones reales, y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | `[Authorize(Policy = Politicas.OperarioDeposito)]` en el `PageModel`; la prueba: el operario entra y el despachador y el administrador son redirigidos a acceso denegado. |
| **Modificado** | — |
| **Descartado** | Restringir la ruta en el middleware de acceso: las políticas por caso de uso viven en cada endpoint y página, como en el resto. |
| **Verificación** | 289 pruebas (65 unitarias, 54 de arquitectura y 170 de integración), todas pasan. |

# **4\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 09/10/2026 | Versión inicial: criterios y entradas del aislamiento por inquilino (hito del 15/10). | Ezequiel Marcenal |
| 0.2 | 12/10/2026 | Entrada del reporte de cobertura en el pipeline. | Ezequiel Marcenal |
| 0.3 | 12/10/2026 | Entrada de la tabla outbox por módulo. | Ezequiel Marcenal |
| 0.4 | 12/10/2026 | Entrada del ADR-0003 versión 1.0. | Ezequiel Marcenal |
| 0.5 | 12/10/2026 | Entrada de la respuesta 404 ante una escritura de otro inquilino. | Ezequiel Marcenal |
| 0.6 | 12/10/2026 | Entrada del ADR-0004. | Ezequiel Marcenal |
| 0.7 | 14/10/2026 | Entrada de la actualización de seguridad a .NET 10.0.13. | Ezequiel Marcenal |
| 0.8 | 15/10/2026 | Entrada del dominio de CU-17 y CU-18. | Ezequiel Marcenal |
| 0.9 | 15/10/2026 | Entrada de la política de CU-30 en la pantalla del Backoffice. | Ezequiel Marcenal |
