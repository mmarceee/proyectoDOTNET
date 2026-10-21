# **Bitácora de uso de asistentes de IA**

| Estado | En curso: se agrega una entrada por cada componente trabajado con asistencia de IA |
| :---- | :---- |
| **Fecha** | 21 de octubre de 2026 (creada el 09/10/2026) |
| **Autores** | Ezequiel Marcenal y Lucas Ottonello |
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
| Codex, aplicación de escritorio | Lucas Ottonello | Explicación y revisión de arquitectura; análisis de casos de uso; escritura y corrección de código, documentación, migraciones y pruebas; verificación en navegador y apoyo para integrar ramas. No se reconstruye el modelo concreto de cada sesión porque no está verificado. |

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
| **Aceptado sin modificaciones** | `[Authorize(Policy = Politicas.OperarioDeposito)]` en el `PageModel`; la prueba: el operario entra y el despachador y el administrador reciben 403 (al principio la prueba esperaba una redirección a acceso denegado; se ajustó cuando CU-07 cambió esa respuesta por un 403 directo). |
| **Modificado** | — |
| **Descartado** | Restringir la ruta en el middleware de acceso: las políticas por caso de uso viven en cada endpoint y página, como en el resto. |
| **Verificación** | 289 pruebas (65 unitarias, 54 de arquitectura y 170 de integración), todas pasan. |

## **3.14 Endpoint de sincronización (CU-52) y registro de CU-17 y CU-18 en Envíos**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal (con dos piezas en módulos de Lucas Ottonello y Cristian Reyes, acordadas para revisar en el PR) |
| **Fecha** | 15/10/2026 |
| **Archivos** | Ejecución: `OperacionDispositivo`, `SincronizarOperacionesHandler`, `ProcesarOperacionHandler`, `ProcesadorEnAmbitoPropio`, `EjecucionDbContext`, endpoint y migración `Inicial`. Envíos: `RegistrarEntregaAsync` y `RegistrarIntentoFallidoAsync` en `EnviosModuleApi`, `IReglasDeEntrega` y `ReglasDeEntregaProvisorias`, `IIntegrationEventPublisher`, eventos `EnvioEntregado` e `IntentoFallidoRegistrado`. Planificación: `IPlanificacionModuleApi` (nuevo). Administración: política `Repartidor`. `Http.Contracts/Repartidor`. Prueba `Ejecucion/SincronizacionTests` (nueva). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar el lado del servidor del ADR-0004: recibir las operaciones de la PWA y aplicarlas con la política acordada. |
| **Cómo se trabajó** | La IA propuso el diseño y preguntó dos decisiones; Ezequiel eligió que la IA hiciera también las dos piezas de los módulos de sus compañeros, y limitar la primera versión a la entrega y el intento fallido. La IA escribió el código, la migración y las pruebas, y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | Cada operación en su propio ámbito de DI (contextos y transacción nuevos), con un reintento ante concurrencia optimista; el registro de la operación en la misma transacción que el efecto; Envíos devuelve el resultado en lugar de lanzar, y Ejecución clasifica el conflicto por el estado actual; las rechazadas se guardan completas; reglas provisorias con un catálogo fijo de motivos hasta CU-06; el repartidor sale del claim `repartidor_id`; hasta 20 operaciones por lote. |
| **Modificado** | Una prueba buscaba el base64 de la firma como texto dentro del JSON guardado, pero `JsonSerializer` escapa `+`, `/` e `=`: se cambió por deserializar y comparar la firma. Se recortan el tipo y el detalle al tamaño de su columna, para poder registrar una operación inválida con un tipo largo. Se sumaron pruebas de datos inválidos y de los demás conflictos, porque la cobertura de ramas había bajado de 79,4 % a 74,7 %. |
| **Descartado** | Procesar todo el lote en un solo ámbito: un fallo dejaba cambios a medias en los DbContext para la operación siguiente. Que Envíos lance excepciones hacia Ejecución: Ejecución no puede conocer `TransicionInvalidaException`, que es interna de Envíos. Pedir las reglas a Administración antes de que exista CU-06. Incluir ahora la carga, la posición y la rendición, que son de Lucas: se rechazan como `TipoNoSoportado` sin cambiar el contrato. |
| **Verificación** | 18 pruebas de integración nuevas con la sesión real del repartidor; 308 pruebas en total (65 unitarias, 54 de arquitectura y 189 de integración), todas pasan. Cobertura: 93,7 % de líneas y 76,4 % de ramas. |

## **3.15 `UnidadDeTrabajo.Sumar`: un solo lugar para sumar un DbContext a la transacción**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal (acordado con Cristian Reyes, que había sumado Identity a la unidad de trabajo) |
| **Fecha** | 16/10/2026 |
| **Archivos** | `UnidadDeTrabajo.cs` y `ModuleDbContext.cs` (BuildingBlocks); `IdentidadDbContext.cs` (Administración); `UnidadDeTrabajoTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Para que el alta de un usuario y su evento se confirmaran juntos, Cristian había hecho pública la `DbTransaction` de la unidad y copiado en `IdentidadDbContext` la lógica de `ModuleDbContext`. La IA propuso concentrar esa lógica en `UnidadDeTrabajo` y volver a hacer `internal` la transacción. |
| **Cómo se trabajó** | La IA revisó el cambio de Cristian, propuso la alternativa y, a pedido de Ezequiel, la implementó con dos pruebas nuevas. |
| **Aceptado sin modificaciones** | `UnidadDeTrabajo.SumarAsync(DbContext)` y `Sumar(DbContext)`: comparan la transacción actual del contexto (`Database.CurrentTransaction`) con la de la unidad y lo suman o lo desenganchan; sin campo por contexto. `ModuleDbContext` e `IdentidadDbContext` los llaman antes de guardar. `Transaccion` vuelve a ser `internal`. |
| **Modificado** | — |
| **Descartado** | Dejar la transacción pública: cualquier módulo podría confirmarla o revertirla por fuera de `EjecutarAsync`. Una clase base común para los dos contextos: `IdentidadDbContext` ya hereda de `IdentityDbContext`. |
| **Verificación** | Dos pruebas nuevas: el mismo contexto se suma a cada unidad nueva y una falla no deshace la anterior; después de una unidad, el contexto vuelve a guardar con su propia transacción. Pasan todas las pruebas en Docker (16/10); cobertura: 94,1 % de líneas y 77,5 % de ramas. |

## **3.16 Trazas: OpenTelemetry en la API y correlación de la sincronización**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 16/10/2026 |
| **Archivos** | `Directory.Packages.props`, `Logistica.Api.csproj` y `Program.cs`; `EjecucionModule.cs` y `ProcesadorEnAmbitoPropio.cs` (Ejecución); prueba en `SincronizacionTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que una operación de la PWA se pueda seguir de punta a punta con un identificador de correlación (letra, sección 6.12; ADR-0004, sección 2.9). La IA detectó que OpenTelemetry todavía no estaba configurado, así que no había traza que continuar. |
| **Cómo se trabajó** | La IA propuso sumar sólo las trazas, eligió versiones de los paquetes publicadas hace más de dos semanas, escribió la configuración, la actividad por operación y la prueba, y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | OpenTelemetry con instrumentación de ASP.NET Core y Npgsql y la fuente `Logistica.Ejecucion`; exportador OTLP sólo si `OTEL_EXPORTER_OTLP_ENDPOINT` está configurado (Docker Compose apunta a Aspire Dashboard). Cada operación es una actividad hija del `traceparent` del dispositivo, con un vínculo al request del lote; el evento del outbox hereda esa traza. |
| **Modificado** | — |
| **Descartado** | Las versiones 1.20.0 de OpenTelemetry, publicadas el día anterior. Métricas y logs: quedan para el hito de observabilidad del 5/11. Colgar las operaciones del request del lote: un lote junta operaciones registradas en momentos y trazas distintas. |
| **Verificación** | Prueba nueva: con un `CorrelacionId` del dispositivo, el `TraceParent` del evento `EnvioEntregado` del outbox pertenece a la misma traza. 353 pruebas (87 unitarias, 54 de arquitectura y 212 de integración), todas pasan. |

## **3.17 Revisión de la mensajería y trazas del publicador y del consumidor**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 16/10/2026 |
| **Archivos** | `TrazasMensajeria.cs` (nuevo), `PublicadorOutbox.cs`, `ConsumidorRabbitMq.cs` y `RabbitMqTransport.cs` (BuildingBlocks); `Program.cs` de la API y del Worker; `Logistica.Worker.csproj`; `TrazasMensajeriaTests` (nuevo); ADR-0003 v1.2 |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Revisar contra el ADR-0003 la mensajería que implementó Lucas Ottonello con CU-43 y completar la sección 2.9: seguir una operación de la API a RabbitMQ y al Worker en una sola traza. |
| **Cómo se trabajó** | La IA comparó el código con cada sección del ADR. Confirmó lo que cumple: `SKIP LOCKED`, confirmaciones del broker, el sobre en cabeceras, la inbox en la misma transacción y la copia al reintento confirmada antes del *ack*. Encontró dos problemas: las actividades se creaban sin `ActivitySource`, así que OpenTelemetry no las exportaba, y el Worker no tenía OpenTelemetry; y el consumidor esperaba 250 ms en cada vuelta aunque hubiera mensajes, lo que lo limitaba a unos 4 mensajes por segundo por cola. Escribió las correcciones y la prueba, actualizó el ADR y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | Fuente `Logistica.Mensajeria` con un span productor hijo del request y un span consumidor hijo de la publicación; la cabecera `traceparent` lleva el span de publicación. Error, intento y destino en el span de un mensaje que falla. El log del reintento con tipo e inquilino, nunca con el contenido. Espera del consumidor sólo cuando todas las colas están vacías. |
| **Modificado** | — |
| **Descartado** | Reemplazar `BasicGet` por `BasicConsume`: con la espera corregida alcanza, y un solo canal evita publicaciones y confirmaciones simultáneas. Llevar `OcurridoEn` con milisegundos en una cabecera: se documenta la precisión en segundos de `timestamp`. |
| **Verificación** | Prueba nueva: el span del consumidor del Worker es hijo del span de publicación, que es hijo del request que guardó el evento. 385 pruebas (90 unitarias, 54 de arquitectura y 241 de integración), todas pasan. |

## **3.18 Caché distribuida sobre Valkey con métrica de aciertos**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 19/10/2026 |
| **Archivos** | `ICacheDistribuida.cs` (SharedKernel, nuevo); `CacheDistribuida.cs`, `MetricasCache.cs` y `CachingExtensions.cs` (BuildingBlocks, nuevos); `Program.cs` de la API; `Directory.Packages.props`; `docker-compose.yml`; `CacheDistribuidaTests` (nuevo) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Dejar la base de la caché del hito del 22/10 ("caché en funcionamiento con su métrica de aciertos") para que los casos de uso la usen sin conocer Valkey: el seguimiento público (CU-60) y el cuadro tarifario (CU-05). |
| **Cómo se trabajó** | Se pidió expresamente que la IA escribiera el código. Escribió la interfaz, la implementación, el registro, las métricas y las pruebas, y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | `ICacheDistribuida` en SharedKernel, con `ObtenerOCrearAsync` e `InvalidarAsync`, implementada sobre `IDistributedCache`: Valkey con cadena de conexión y memoria del proceso sin ella. El operador de la sesión forma parte de la clave, y sin operador falla cerrado (ADR-0002). Si Valkey no responde, la lectura sigue desde la fuente: `AbortOnConnectFail` desactivado, `BacklogPolicy.FailFast` y un segundo de espera como máximo. Medidor `Logistica.Cache` con los contadores de aciertos, fallos y errores, etiquetados por caché y exportados a Aspire Dashboard cada 10 segundos. Un null no se guarda. |
| **Modificado** | — |
| **Descartado** | `HybridCache`: suma una capa en memoria por instancia, y su invalidación entre instancias complica la explicación. Incluir el comercio en la clave automáticamente: una invalidación del Backoffice, que no tiene comercio, no borraría lo guardado desde el Portal. Si el dato depende del comercio, el caso de uso lo pone en la clave. Un indicador con la tasa de aciertos ya calculada: alcanza con los dos contadores. Qué se guarda en la caché, con qué duración y cuándo se invalida lo decide el ADR de caché (Cristian Reyes). |
| **Verificación** | Ocho pruebas nuevas: un fallo seguido de un acierto sin volver a la fuente, el aislamiento entre operadores, el fallo cerrado sin operador, la invalidación, que un null no se guarda, una caché que falla a mitad de camino, dos instancias que comparten un Valkey real (Testcontainers) y Valkey inalcanzable, que responde desde la fuente en menos de 5 segundos. 407 pruebas (98 unitarias, 54 de arquitectura y 255 de integración), todas pasan. Falta verlo en Aspire Dashboard cuando un caso de uso use la caché. |

## **3.19 Reglas de entrega pedidas a Administración y versión de reglas del envío**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 19/10/2026 |
| **Archivos** | `ReglasOperativas.cs` e `IAdministracionModuleApi.cs` (Administracion.Contracts, con la firma de Cristian Reyes); `ReglasOperativasProvisorias.cs` (nuevo) y `AdministracionModuleApi.cs` (Administración, provisorio hasta CU-06); `ReglasDeEntregaDeAdministracion.cs` (nuevo, reemplaza a `ReglasDeEntregaProvisorias`), `Envio.cs`, `ReglasEntrega.cs`, `CrearEnvioHandler.cs`, `EnviosModule.cs` y la migración `VersionReglasDelEnvio` (Envíos); `ReglasDeEntregaDeAdministracionTests` (nuevo) y pruebas de Envíos, de la hoja de ruta y de la sincronización; CU-18 en `casos-de-uso.md` (v1.1) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que CU-17, CU-18 y la hoja de ruta juzguen cada envío con la versión de reglas con que se admitió (letra 6.6; escenario 5 de la defensa), pidiéndolas a Administración por el contrato que definió Cristian Reyes para CU-06, y aplicar la política de devolución al agotar los intentos. |
| **Cómo se trabajó** | La IA revisó la propuesta de Cristian (usar la vigente para todos los envíos) y recomendó guardar la versión en el alta, igual que el tarifario. Se pidió expresamente que la IA escribiera el código: contrato idéntico a la firma de Cristian, una implementación provisoria en Administración para que compile y funcione hasta su PR, el adaptador, la versión en el alta con su migración (generada en Docker) y la política de devolución, con pruebas. Corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | El envío guarda `VersionReglasId` al admitirse; sin reglas vigentes, el alta se rechaza. El adaptador usa la versión del envío y, para los envíos viejos sin versión, la vigente; si la versión del envío no existe, falla en lugar de aplicar otras reglas. El motivo se busca en el catálogo de esa versión. Cada versión se pide una sola vez por request (una hoja de ruta trae muchos envíos). Con `DevolverTrasIntentosAgotados` en false, agotar los intentos deja el envío Reprogramado para que decida el despachador; un motivo no reprogramable devuelve siempre. |
| **Modificado** | Al unir CU-06 de Cristian Reyes (PR #38), se quedó su versión del contrato (mismo código, otros comentarios), se borró la implementación provisoria y las dos pruebas de la sincronización buscan los motivos por código en la versión vigente de los datos iniciales, porque sus Id ahora se generan. |
| **Descartado** | Usar la vigente para todos los envíos hasta guardar la versión: cambiaría las reglas de envíos que ya están en la calle al publicar una versión nueva. Pedirle a Cristian que el adaptador lo escriba él: es código de Envíos (CU-17, CU-18 y CU-10). |
| **Verificación** | Diez pruebas nuevas: seis del adaptador con un Administración falso (la versión 1 se conserva aunque rija la 2, la vigente para envíos sin versión, la versión inexistente falla, el motivo de otra versión no se acepta, la hoja lleva la versión y el catálogo, y una sola consulta por versión), dos de la política de devolución y dos de la versión en el alta. Se ajustaron las pruebas de la hoja de ruta, de la sincronización y del alta. 470 pruebas (122 unitarias, 56 de arquitectura y 292 de integración), todas pasan. |

## **3.20 CU-55 · Confirmar la rendición (nivel mínimo)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 20/10/2026 |
| **Archivos** | Ejecución: `Rendicion.cs` (`Confirmar`, `DebeVolver`, `Resolver`), `IEjecucionModuleApi`, `RendicionPendiente` (`DebeVolver`) y `EjecucionModuleApi`. Envíos: `EnvioReintegradoADeposito` (nuevo), `IEnviosModuleApi` (`ReintegrarADepositoAsync`, `RegistrarFaltantesAsync`), `EnviosModuleApi`, `IEnvioRepository` y `EnvioRepository`. Planificación: `FinalizarRutaAsync`. Depósito: `ConfirmarRendicionHandler` y las páginas `ConfirmarRendicion/Index` y `Detalle` (nuevos), `DepositoModule` y el `.csproj`. Seguimiento: `InvalidarSeguimiento` (el evento nuevo). Backoffice: enlace en el menú. Pruebas: `RendicionTests` y `ConfirmarRendicionTests` (nuevos). CU-55 en `casos-de-uso.md` (v1.3) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar CU-55 sobre la rendición que dejó CU-54 (Lucas Ottonello), respetando el contrato que él propuso: Ejecución sigue siendo la dueña de la rendición y Depósito coordina por contratos. |
| **Cómo se trabajó** | La IA leyó CU-54, CU-55, la tabla de transiciones y el diseño de Lucas, y propuso el diseño. El responsable eligió: coordinar desde Depósito, que los faltantes generen incidencia sin bloquear el cierre y que los sobrantes bloqueen. Se pidió expresamente que la IA escribiera el código y las pruebas. Corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | La regla de cada línea en `Rendicion.Confirmar`: entregado en calle, devuelto al depósito, o faltante (recibida en falso y sin estado final; el extravío lo decide el despachador). T8 sólo para envíos no intentados que volvieron enteros, con `EnvioReintegradoADeposito` por Outbox. Una incidencia de tipo extravío por envío con faltantes. `Ruta.Finalizar`, que ya liberaba la reserva. Todo en una transacción con el bloqueo de planificación del operador. Ante un error, la página vuelve con un GET nuevo, para no mostrar entidades que quedaron cambiadas en memoria después del rollback. |
| **Modificado** | La IA normalizaba lo escaneado a mayúsculas, como en CU-30, y la prueba de punta a punta lo detectó: los códigos de las pruebas tienen minúsculas. Se pasó a comparar sin distinguir mayúsculas. También mostraba los primeros 8 caracteres del Id de la ruta, que en un UUID v7 son la marca de tiempo y se repiten entre rutas creadas juntas: se pasó a los últimos 8. |
| **Descartado** | Ubicar CU-55 en Ejecución: el actor es el operario de depósito, y el plan lo asigna al área de Depósito. Bloquear el cierre ante faltantes: un bulto perdido dejaría la ruta abierta para siempre. Marcar Extraviado desde la rendición: lo decide el despachador (CU-21). Guardar los sobrantes: queda fuera del nivel mínimo. |
| **Verificación** | Tres pruebas unitarias de `Rendicion.Confirmar` y cinco de integración desde la página: confirmación completa (T8, Outbox, incidencia, líneas, ruta finalizada y reserva liberada), sobrante que impide confirmar sin cambiar nada, listado y detalle, 404 para otro operador y 403 para otro perfil. 551 pruebas (154 unitarias, 56 de arquitectura y 341 de integración), todas pasan. |
| **Aporte posterior de Lucas con Codex · 20/10/2026** | Se revisó la diferencia entre bultos que deben volver y los declarados como devueltos. En el listado de pendientes se sustituyó el GUID visible por el nombre del chofer y la matrícula, conservando fecha y cantidad de bultos que deben volver; los enlaces siguen identificando la ruta por su ID. La consulta usa los contratos de Planificación y Administración, incluidos recursos históricos inactivos y sin quitar el aislamiento. Se ajustó la prueba del listado y pasaron las cinco pruebas dirigidas de CU-55. Este aporte no atribuye a Lucas la implementación del cierre, T8 e incidencias, realizada por Ezequiel. |

## **3.21 PWA del repartidor: registrar la entrega y el intento fallido sin conexión (CU-17 y CU-18)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 20/10/2026 |
| **Archivos** | `hoja-ruta.js` (`registrarEntrega`, `registrarIntentoFallido` y `estadosEnvios`), `evidencia.js` (nuevo), `EntregaService.cs` y `Entrega.razor` (nuevos), `HojaDeRuta.razor` y `Program.cs` de la PWA; `repartidor-hoja-ruta.test.mjs`. También `CacheDistribuidaTests` (prueba de Valkey inalcanzable). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Completar la parte de la PWA de CU-17 y CU-18, que faltaba para el hito del 22/10 («registrando entregas sin conectividad y sincronizando»), sobre la cola cifrada y la sincronización que dejó Lucas Ottonello con CU-51 y CU-54. |
| **Cómo se trabajó** | El responsable decidió hacerlo en el equipo de Ezequiel en lugar de esperar a Lucas. Se pidió expresamente que la IA escribiera el código y las pruebas. La IA revisó primero el merge de Lucas (anulación de despacho, mejoras de navegación y sesión, y el listado de CU-55 con chofer y vehículo) y corrió las pruebas en Docker. |
| **Aceptado sin modificaciones** | La entrega y el intento van a la misma cola cifrada de la carga y la rendición, sin borrador, con un registro por envío; un rechazo del servidor queda para despacho. Las reglas (firma siempre, foto, nombre y documento, motivos y evidencia) se validan con las de la versión del envío que trae la hoja; el servidor vuelve a validar. Un envío que la hoja muestra asignado se acepta si este dispositivo confirmó la carga de todos sus bultos, porque sin conexión la hoja no se actualiza. La firma se dibuja en un canvas sobre fondo blanco; la foto se achica y pasa a JPEG hasta entrar en 1 MB; la posición sale del GPS, que funciona sin red. La sincronización reutiliza la de la carga, que reintenta al volver la conexión. La prueba de Valkey inalcanzable pasó a medir la segunda lectura, porque la primera incluye el intento de conexión y con la máquina cargada superaba el límite. |
| **Modificado** | — |
| **Descartado** | Un servicio de sincronización propio: duplicaría la cola y los reintentos de Lucas. Permitir volver a registrar un envío rechazado: el rechazo es definitivo (ADR-0004) y lo revisa despacho. |
| **Verificación** | Cinco pruebas JavaScript nuevas (entrega en la cola y sincronizada, reglas y posición exigidas y un solo registro por envío, motivo del catálogo con evidencia, envío cargado sin conexión, y la rendición que toma lo entregado); 40 pruebas JavaScript, todas pasan. 582 pruebas .NET (154 unitarias, 56 de arquitectura y 372 de integración), todas pasan. Falta probarlo en un teléfono con la cámara, el GPS y el modo avión. |

## **Nota sobre las entradas retrospectivas de Lucas**

Las entradas 3.22 a 3.33 se reconstruyeron el **20/10/2026** con asistencia de Codex, a partir de los chats disponibles del proyecto, los commits de Lucas y la documentación del repositorio. Las fechas corresponden al trabajo original o a sus ampliaciones. Los resultados de pruebas son los registrados durante esas tareas; **no se volvió a ejecutar la suite al completar esta bitácora**. Lucas debe revisar la redacción para confirmar que representa su experiencia; no se supone que escribió manualmente todo el código ni que aceptó sin cambios todas las propuestas iniciales. No se incluyen conversaciones de otros proyectos ni se garantiza recuperar sesiones no disponibles.

## **3.22 Arquitectura: revisión documental y preparación del monitoreo**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | Septiembre–octubre de 2026; revisión de Identity el 13/10 |
| **Referencias** | `docs/analisis-diseno/ADR-0001-estilo-arquitectonico.md`, ADR-0002, diagramas y guía de implementación. Chats «Revisar diagrama .NET en Drawio», «Revisar organización por módulos», «Explicar arquitectura final y hexág», «Explicar el flujo de la aplicación», «Explicar arquitectura de monitoreo» y «Revisar cambios de Identity y migrac». |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Entender y revisar monolito modular, capas, Features, puertos y adaptadores, contratos, host único, multitenancy y mensajería; preparar explicaciones para el monitoreo. |
| **Cómo se trabajó** | La IA leyó documentación y código, explicó el recorrido de las solicitudes y comparó diagramas y ADR. Señaló contradicciones y actualizó el estado del Markdown del ADR-0001 siguiendo la aprobación de Lucas. La autoría original de los ADR y de la infraestructura de otros integrantes no se atribuye a la IA de Lucas. |
| **Aceptado sin modificaciones** | Se utilizó la explicación del flujo pantalla → caso de uso → dominio → repositorio y de la comunicación por Contracts como apoyo para comprender y revisar las implementaciones. No se afirma una aceptación íntegra de todo el texto generado. |
| **Modificado** | Se corrigió el estado documental del ADR-0001 para reflejar la aprobación existente. Las revisiones distinguieron arquitectura prevista de funcionalidad efectivamente implementada y señalaron diferencias entre Markdown, documentos y diagramas. |
| **Descartado o no aplicado** | No se integró la rama de Identity durante la revisión del 13/10, por pedido de Lucas. Se señalaron para coordinación la amplitud de la excepción de Identity y la autorización transitoria; no se registraron como correcciones ya realizadas. |
| **Verificación** | Contraste manual entre documentación, referencias de proyectos, código e historial. La revisión de Identity de esa sesión no pudo certificar las pruebas por problemas del entorno; no equivale a una aprobación automática del cambio. |

## **3.23 CU-13 · Consulta de envíos en Backoffice**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 05–06/10/2026; integración posterior con el aislamiento compartido |
| **Archivos y referencias** | `ConsultarEnviosQuery`, handler, DTO y reader de listado; `ConsultarEnvios/Index.cshtml` y PageModel; configuración e índices de Envíos. Chat «Consultar requisitos del CU-13»; commits `7c191f5`, `e60e23f` y `c408638`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Construir y comprender listado, paginación y filtros por estado, fechas, texto y comercio, manteniendo el alcance del operador y comercio del contexto. |
| **Cómo se trabajó** | Se trabajó por pasos: la IA explicó y proporcionó fragmentos para incorporar, revisó lo escrito y realizó correcciones directas cuando Lucas las pidió. También comprobó el flujo alta por API → listado. No se reconstruye quién tecleó cada línea. |
| **Aceptado sin modificaciones** | Consulta y paginación en PostgreSQL, filtros antes del conteo y de obtener la página, e índices compuestos por operador/fecha y operador/estado como diseño de la consulta. |
| **Modificado** | La búsqueda de texto pasó a enviarse con Enter. Se corrigió Siguiente para conservar el texto y se agregó `ComercioId` opcional a la consulta. Los filtros manuales de aislamiento se retiraron al incorporar el mecanismo compartido, trabajo registrado por Ezequiel en 3.2. |
| **Descartado o diferido** | La propuesta inicial de implementar el aislamiento global desde CU-13 se dejó a sus responsables transversales. La integración del selector y nombres comerciales dependía del contrato de Administración; no se declaró terminado el alcance completo por haber completado el listado inicial. |
| **Verificación** | Se comprobó en la aplicación búsqueda por número, texto inexistente, paginación de 25 resultados sin perder la búsqueda y aparición de un envío creado por API. Se revisó la migración para que sólo agregara los dos índices; el módulo compiló sin errores. |

## **3.24 CU-14 · Detalle del envío e integración con recepción**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 08–09/10/2026; ajustes posteriores de presentación |
| **Archivos y referencias** | Slice `ConsultarDetalleEnvio` de Envíos, readers de detalle/evidencia, integración con `RecibirBulto`, pruebas de detalle y recepción; `docs/analisis-diseno/cu-14/revision-arquitectura.md`. Chat «Planificar detalle de envío CU-14»; commits `5e0e63e`, `b4b213a` y `35f036d`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Mostrar datos, bultos, tarifa y antecedentes del envío; consultar evidencias y navegar a recepción conservando el contexto del listado. |
| **Cómo se trabajó** | Primero se analizó el alcance sin implementar. Luego la IA escribió y modificó código, pruebas, datos de demostración y documentación bajo las decisiones de Lucas, y revisó la arquitectura del resultado. |
| **Aceptado sin modificaciones** | Separación entre consulta, DTOs, readers y presentación, e intercambio con Depósito mediante su contrato. El registro físico sigue siendo una acción explícita, no un efecto de consultar el detalle. |
| **Modificado** | La revisión detectó coordinación en el PageModel y reglas en Razor: se trasladaron a Application. Se agregaron handlers de evidencia y precarga, y se conservaron filtros al ir y volver de recepción. Sólo se precarga el código del bulto; las medidas se ingresan durante la recepción. Se corrigió el error de validación al abrir recepción sin código y después se reorganizó el layout del detalle. |
| **Descartado** | Mantener la decisión de recepción como una comparación de estado en la vista, consultar directamente al reader desde el endpoint de evidencia o completar automáticamente las medidas físicas desde las declaradas. |
| **Verificación** | La corrección arquitectónica terminó con 24 pruebas de integración y 54 de arquitectura correctas, incluido detalle → recepción → registro → detalle → listado con filtros. Los permisos provisionales que existían en esa fecha se integraron posteriormente con Identity; no se presenta aquella revisión como verificación de todos los permisos futuros. |

## **3.25 CU-40 · Planificación de rutas, paradas y restricciones**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | Desde el 09/10/2026; agrupación de paradas el 12/10 y reintentos el 15/10 |
| **Archivos y referencias** | Dominio de Rutas, slices de armado/consulta/validación, repositorios y migraciones de Planificación, contratos con Envíos/Administración/Depósito, páginas y JavaScript; `docs/analisis-diseno/cu-40`. Chat «Analizar el CU-40»; commits `1799495`, `9da8fce`, `f741ef3` y `1c6877c`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Armar una ruta con repartidor, vehículo y envíos validando capacidad, franjas, paradas, disponibilidad y RF-15: impedir que un envío pertenezca simultáneamente a dos rutas activas. |
| **Cómo se trabajó** | Lucas definió las reglas mediante ejemplos y preguntas antes de implementar. La IA escribió dominio, handlers, persistencia, migraciones, interfaz, pruebas y documentación, y los fue corrigiendo con las observaciones de Lucas y de sus compañeros. |
| **Aceptado sin modificaciones** | Evaluación compartida antes de confirmar, evidencia de validación, control de revisión y operación transaccional. Uso de medidas de recepción cuando existen, con respaldo en las declaradas cuando falta el dato. |
| **Modificado** | El modelo inicial confundía envío y parada. A pedido de Lucas se pasó a Ruta → Parada → envíos: la misma dirección, aun con apartamentos diferentes, puede ser una visita si las franjas son compatibles. Las capacidades siguen contando todos los bultos. Se agregó prohibición de crear rutas hacia el pasado y validación de `ReintentoDesde` al armar y despachar, admitiendo el límite exacto en la zona del operador. El máximo de paradas pasó a leerse del contrato conectado por Cristian a CU-06. |
| **Corrección del Outbox** | La IA implementó inicialmente `OutboxMessage` con `IOperadorOwned` y una prueba que exigía el filtro Tenant. Contradecía ADR-0002 §2.4: el publicador sin inquilino no vería los mensajes. Tras la observación de Ezequiel y Cristian se retiró ese enfoque y se adaptó Planificación al Outbox compartido sin marcador implementado por Ezequiel (3.7). Fue un error de la implementación asistida, no una regla válida de CU-40. |
| **Modificado en la interfaz** | Datos de ruta fijos con Modificar/Confirmar/Cancelar; selección de envíos en modal, filtros automáticos y limpieza, actualización parcial sin cerrar el modal y botones de validación/confirmación dependientes de la selección vigente. Se corrigieron búsquedas, errores de binding al cambiar fecha y la presentación de historial, repartidor y vehículo. |
| **Descartado** | Contar cada envío como una parada; distinguir apartamentos como visitas separadas por sí solos; confiar sólo en la prevalidación del navegador; conservar el filtro Tenant en Outbox o saltearlo con `IgnoreQueryFilters`. |
| **Verificación** | Pruebas de reglas, persistencia, aislamiento, concurrencia, rollback y migraciones de agrupación. Para el plazo de reintento se registraron 81 pruebas de integración de Planificación, 61 unitarias y 54 de arquitectura correctas; la integración posterior con main pasó 286 pruebas. Son ejecuciones históricas, no un conteo de pruebas exclusivas de CU-40. |

## **3.26 CU-41 · Quitar un envío de una ruta**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 12/10/2026 y ajustes posteriores de historial |
| **Archivos y referencias** | Slice `QuitarEnvioDeRuta`, dominio, repositorio y auditoría de retiros, presentación de detalle y pruebas `QuitarEnvioDeRutaTests`. Commits `2297467`, `da90523` y `f741ef3`; chat «Analizar el CU-40». |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Quitar un envío antes del despacho, devolverlo a EnDeposito mediante T4 y conservar evidencia de quién lo retiró. |
| **Cómo se trabajó** | La IA implementó el command, handler, coordinación por contratos, transacción y pruebas. También explicó a Lucas cómo el request transporta datos y el handler ejecuta la operación. |
| **Aceptado sin modificaciones** | Revalidar estado y revisión dentro de la operación, coordinar el retiro con Envíos mediante su API y revertir todos los efectos si falla el guardado. |
| **Modificado** | Se agregó historial explícito de retiros al detectar Lucas que no aparecían. Tras agrupar envíos por visita, quitar uno conserva la parada mientras tenga otros; retirar el último elimina la parada y, si no quedan envíos, libera las reservas correspondientes. Fechas del historial en dd/MM/aaaa y hora local UTC−3 sin mostrar esa etiqueta. Se adaptó al Outbox por módulo. |
| **Descartado** | Borrar la visita completa al quitar uno de sus envíos, permitir retirar después del despacho o eliminar la ruta como parte de este caso de uso. |
| **Verificación** | Pruebas de retiro, aislamiento, revisión, carrera con despacho y rollback; revisión de historial y migraciones. La integración con Outbox y agrupación se verificó con la regresión de Planificación. |

## **3.27 CU-43 · Despacho, aviso al repartidor y resolución de rutas anteriores**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 14–16/10/2026; ampliación el 20/10/2026 |
| **Archivos y referencias** | Slices `DespacharRuta` y `AnularDespacho`, evidencia de despacho/anulación, Outbox y transporte/publicador/consumidor, aviso interno e Inbox, API/Worker/PWA; `docs/analisis-diseno/cu-43`. Chat «Planificar CU-43 despachar ruta» y ampliación en «Analizar el CU-40»; commits `e317def`, `bd95dec` y `fc6b037`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cerrar la planificación, ponerla a disposición del repartidor y entregar un aviso durable. Resolver además rutas de días anteriores despachadas pero nunca iniciadas. |
| **Cómo se trabajó** | Se acordó el comportamiento antes del código. La IA implementó el despacho, la mensajería y el aviso, escribió pruebas y documentación, y verificó API/Worker en Docker. La revisión y correcciones posteriores de trazas y consumo realizadas por Ezequiel están registradas en 3.17. |
| **Aceptado sin modificaciones** | Guardar estado, evidencia y `RutaDespachada.v1` en la misma transacción; publicar con confirmación del broker y procesar de forma idempotente. Despachar conserva los envíos AsignadoARuta: EnTransito corresponde a CU-51. |
| **Modificado** | Se exigió usuario repartidor vinculado y habilitado y se integró Identity. El 20/10 se agregó anulación controlada sólo de un despacho anterior sin actividad: motivo, revisión y confirmación de coordinación con el repartidor; vuelve a Planificada, conserva envíos y auditoría. Compite con la carga bajo el mismo bloqueo por operador. Una operación offline anterior que llegue después queda como conflicto, sin reactivar la ruta. |
| **Descartado o limitado** | No se simula carga ni rendición para cerrar una planificación atrasada. No se elimina la ruta ni se permite anular si hay escaneos, operaciones aplicadas o rendición. El evento `DespachoAnulado.v1` no tiene consumidor implementado y no acredita una notificación push. La ausencia de operaciones locales pendientes requiere coordinación humana: el servidor no conoce una cola desconectada. |
| **Verificación** | PostgreSQL/RabbitMQ reales: publicación pendiente al iniciar Worker, persistencia, reentrega, fallos, reintentos, autorización y aislamiento; demo en Docker. La ampliación agregó 17 pruebas dirigidas correctas. La regresión completa de esa ampliación tuvo una prueba de tiempo de Valkey fallida bajo carga, que pasó aisladamente junto a las otras siete de caché; no se declara toda esa ejecución simultáneamente verde. |

## **3.28 ADR-0005 · Modo de renderizado de Blazor**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 12/10/2026 |
| **Archivo y referencia** | `docs/analisis-diseno/ADR-0005-modo-de-renderizado-de-blazor.md`; chat «Redactar ADR sobre modo de render Bl»; commit de renombrado `19e644b`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Justificar WebAssembly frente a Server y Auto según la letra: escalado, estado, latencia y recursos del servidor. |
| **Cómo se trabajó** | La IA explicó los modelos de ejecución, revisó la configuración real y redactó el Markdown tras acordar el contenido con Lucas. |
| **Aceptado sin modificaciones** | Estructura de contexto, decisión, alternativas, consecuencias y verificaciones como fundamento de la elección existente. No implicó migrar las aplicaciones a otro modo de renderizado. |
| **Modificado** | Se aclaró que el seguimiento es público sin login pero requiere el token del enlace. Se corrigieron verificaciones propuestas que inicialmente sonaban como compromisos acordados y se distinguieron obligaciones de recomendaciones. Se renombró de ADR-0004 a ADR-0005 para evitar la colisión con el ADR de sincronización de main. |
| **Descartado o diferido** | Dar por verificadas tarjetas de Trello que no pudieron consultarse o aprobar el ADR por el solo hecho de redactarlo. Se dejó el formato de entrega final para más adelante. |
| **Verificación** | Contraste con letra, ADR-0001 y proyectos WebAssembly/host. El archivo continúa como Propuesto, pendiente de revisión del equipo. |

## **3.29 CU-50 · Descarga de hoja de ruta y consulta sin conexión**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 17/10/2026; ampliación Mis rutas y correcciones el 19–20/10 |
| **Archivos y referencias** | Consulta de hoja en Ejecución, contratos de módulos y HTTP, `HojaDeRutaService`, páginas de PWA, `wwwroot/js/hoja-ruta.js`, service worker y pruebas; `docs/analisis-diseno/cu-50`; commit `17cf3d9`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Descargar una hoja consistente y propia, conservarla cifrada en el dispositivo y permitir consultarla sin conexión. |
| **Cómo se trabajó** | La IA escribió implementación, almacenamiento, pruebas C#/JavaScript y documentación siguiendo las decisiones acordadas; comprobó el flujo publicado con el servidor detenido y luego restablecido. |
| **Aceptado sin modificaciones** | Identidad del espacio por usuario/operador/repartidor, IndexedDB y AES-GCM con clave no exportable, guardado atómico y conservación de la copia anterior ante fallos. La descarga no cambia estados del negocio. |
| **Modificado** | Se ampliaron las tarjetas de Mis rutas y la selección de rutas anteriores abiertas, conservando por separado hojas y operaciones. Se ajustaron bloqueo por cambio/cierre de cuenta y respuestas tardías. El arranque detenido en 98 % se investigó como desalineación/caché de archivos WASM; se recompiló y agregó no-store de recursos sólo en Development. |
| **Descartado** | Usar GUID como nombre de ruta, sobrescribir la copia anterior ante una descarga incompleta, guardar documentos personales en localStorage o borrar IndexedDB para solucionar una actualización de la PWA. |
| **Verificación** | La entrega inicial registró 397 pruebas .NET y 12 JavaScript correctas; luego 25 dirigidas de hoja/unidad de trabajo. Demo publicada: descargar, detener API, abandonar/reabrir la hoja, consultar datos, restaurar API y comprobar el bloqueo al cerrar sesión. El uso sin red exige la PWA publicada; el service worker de desarrollo no cachea la aplicación completa. |

## **3.30 CU-51 · Carga del vehículo y cola de sincronización de la PWA**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 19/10/2026; coordinación con la anulación el 20/10 |
| **Archivos y referencias** | Pantalla/servicio de carga y lector, almacenamiento y cola en `hoja-ruta.js`, `ConfirmarCarga` en Ejecución, APIs de Planificación/Envíos y pruebas; `docs/analisis-diseno/cu-51`; commit `aeddc2d`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Escanear carga por cámara o entrada manual, persistir avance sin red y sincronizar una confirmación sin duplicar transiciones. |
| **Cómo se trabajó** | La IA implementó código, migración de almacenamiento local, pruebas y documentación. El endpoint base de sincronización CU-52 y sus políticas previas pertenecen al trabajo de Ezequiel (3.14); Lucas amplió ese recorrido para carga y la cola cliente. |
| **Aceptado sin modificaciones** | Borradores cifrados por espacio, operación confirmada inmutable e idempotencia por OperacionId. Sólo envíos cargados completos pasan a EnTransito; confirmar faltantes requiere reconocimiento explícito. |
| **Modificado** | La revisión eliminó contratos paralelos de carga y trasladó la coordinación a las APIs de Application de los módulos. Se corrigió una habilitación demasiado amplia: las rutas anteriores se consultan o rinden, pero no admiten nuevas cargas. Una carga guardada en su día puede sincronizarse después, comparando la fecha efectiva de la operación en la zona del operador, no la fecha de llegada. |
| **Descartado** | Aumentar el avance por lecturas repetidas, mostrar una confirmación local como ya aplicada en el servidor, perder el payload de un conflicto o iniciar una carga hoy sobre una ruta atrasada sin revisión de despacho. |
| **Verificación** | Tras corregir contratos: 428 pruebas .NET y 23 JavaScript correctas, incluida decodificación real de imagen CODE-128. El ajuste de fecha pasó 77 de integración de Ejecución, 56 de arquitectura y 35 JavaScript. Cobertura de cortes, reenvíos, doble confirmación, concurrencia entre pestañas, migración local y preservación de operaciones. |

## **3.31 CU-54 · Declaración de rendición del repartidor**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 19–20/10/2026 |
| **Archivos y referencias** | `Rendicion` y líneas en Ejecución, preparación/registro/consulta, repositorio y migración, contratos para CU-55, `Rendicion.razor`, `RendicionService`, cola cifrada y pruebas; `docs/analisis-diseno/cu-54`; commits `f32f123`, `f28cbe6` y `7ededf4`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Declarar qué bultos vuelven al depósito, incluso sin conexión, conservar las observaciones y preparar el control posterior de CU-55. |
| **Cómo se trabajó** | Se acordó y documentó el diseño antes del código. La IA implementó dominio, persistencia, coordinación y pantalla sobre la sincronización existente, escribió pruebas y realizó una demostración offline/online. |
| **Aceptado sin modificaciones** | Diferenciar declaración del repartidor de recepción física del depósito, conservar ambas por línea y mantener la operación confirmada inmutable. Una rendición registrada impide nuevas operaciones de recorrido sobre esa ruta. |
| **Modificado** | Se distinguieron bultos no cargados, carga parcial, entregas e intentos de esta ruta frente a los históricos y operaciones todavía pendientes. Se conservaron las dependencias y el orden de lotes al sincronizar. Al integrar main se corrigió una prueba que aún dependía de reglas provisorias eliminadas, para usar el catálogo real por su contrato. El botón pasó a Finalizar ruta, rojo y a la derecha; el texto no adelanta el cierre administrativo. |
| **Descartado** | Editar la declaración una vez confirmada, marcar automáticamente como extraviado lo que no vuelve, liberar reservas o finalizar la ruta al declarar. Esos efectos corresponden al control de depósito en CU-55. No se incluyó una rendición de dinero. |
| **Verificación** | 49 pruebas dirigidas finales y 31 JavaScript correctas; demo con API detenida, recarga del borrador y operación pendiente, y sincronización al restablecerla. En la regresión completa una prueba de latencia de Valkey falló y pasó aislada; se registró esa limitación. La adaptación posterior de pruebas al contrato de reglas pasó la regresión de esa integración. |

## **3.32 Acceso a las aplicaciones e integración con Identity**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello, sobre Identity de Administración implementado por Cristian |
| **Fecha** | 14–20/10/2026 |
| **Archivos y referencias** | Ingreso/salida de las aplicaciones, middleware de acceso y políticas, `IngresoModel`, `FormularioIngresoVencidoFilter`, consulta de sesión del Portal y pruebas de Identity. Chats «Planificar CU-43 despachar ruta», «Evaluar PWA del repartidor» y «Analizar el CU-40». |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Conectar las pantallas y planificación a la sesión real, impedir que un comercio acceda al Backoffice y dar a Portal y Repartidor sus accesos correspondientes. |
| **Cómo se trabajó** | La IA revisó la compatibilidad con roles/claims y escribió integración, correcciones, pantallas y pruebas. La selección de operador y renovación del claim provienen de la coordinación con los compañeros; no se atribuye a Lucas toda la implementación de CU-03. |
| **Aceptado sin modificaciones** | Autorización del lado servidor por perfil, lectura del responsable desde la sesión y diferenciación de ingreso del comercio, repartidor y personal. |
| **Modificado** | Se corrigieron redirecciones al login incorrecto y acceso del comercio al personal; se agregaron cierres de sesión y consulta de cuenta para el Portal. Ante el HTTP 400 reportado al ingresar se cubrió el caso de formulario antifalsificación vencido: redirección a un GET nuevo con mensaje, manteniendo el rechazo del POST inválido. La contraseña incorrecta normal conserva su mensaje de credenciales inválidas. |
| **Descartado** | Desactivar antiforgery para evitar el 400, aceptar el POST inválido, permitir por estar autenticado sin comprobar el perfil o extender el bypass de desarrollo a usuarios autenticados sin el rol requerido. |
| **Verificación** | Pruebas de login, permisos, logout, selección y aislamiento durante las integraciones. La corrección del formulario pasó 18 pruebas dirigidas y la consulta de sesión del Portal cuatro. El diagnóstico no demuestra retrospectivamente la causa exacta de la captura original sin sus logs; verifica los escenarios reproducidos. |

## **3.33 Presentación común, revisiones de integración y diagnóstico del entorno**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | Octubre de 2026; revisión retrospectiva el 20/10 |
| **Archivos y referencias** | Layouts, navegación y estilos de Backoffice, Portal y PWA; pruebas y documentación de los componentes anteriores. Chat «Agregar menú hamburguesa adaptable» y revisiones de main en los chats de los casos de uso. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Mejorar navegación y legibilidad, revisar la compatibilidad al incorporar trabajo paralelo y diagnosticar fallos de compilación, arranque y CI. |
| **Cómo se trabajó** | Lucas indicó los cambios visuales mediante capturas y ejemplos; la IA editó Razor/CSS/JavaScript y verificó compilaciones o flujos dirigidos. Revisó diferencias con main y ayudó a resolver conflictos. Las modificaciones funcionales específicas y sus pruebas se registran en cada entrada anterior. |
| **Aceptado sin modificaciones** | Se incorporaron las ubicaciones y textos concretos pedidos por Lucas: datos de sesión junto a navegación/cierre, nombres y matrículas en lugar de IDs visibles, fechas legibles y acciones por ruta. No se reconstruye una aceptación sin cambios de todos los layouts intermedios. |
| **Modificado** | Cabeceras, footers y disposición de botones se iteraron con las observaciones de Lucas. En el chat del menú adaptable se incorporó hamburguesa por debajo de 1400 px en las tres aplicaciones y pantallas de cuenta. Se corrigieron conflictos de paquetes conservando referencias necesarias y versiones compatibles con main. |
| **Descartado o no acreditado** | No se atribuyen a Lucas la caché de Ezequiel ni CU-04/05/06/60 de Cristian por haberlos revisado o integrado. La auditoría previa de CU-17/18 distinguió backend de pantallas móviles que entonces faltaban. Posteriormente Ezequiel incorporó esas pantallas a main; su implementación y verificaciones se registran en 3.21. Tampoco se dio por aprobada la propuesta ADR-0006 ni por demostradas en Aspire las métricas sólo por encontrar su instrumentación. |
| **Verificación** | Revisiones de diffs, compilaciones y pruebas dirigidas según cada cambio. En la tarea del menú compilaron los tres proyectos; la compilación completa quedó bloqueada por archivos de la API en ejecución. Los problemas de entorno no se registran como pruebas aprobadas ni las revisiones documentales como implementación de casos ajenos. |

## **3.34 CU-63 · Configurar las suscripciones de avisos del comercio**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | Trabajo del chat iniciado el 20/10/2026; registro en la bitácora el 21/10/2026 |
| **Archivos y referencias** | `SuscripcionAviso`, `TipoEventoAviso`, repositorio, lector, configuración EF y migraciones en Seguimiento; handlers de configuración y prueba; contratos HTTP y endpoints; consulta del vínculo comercial en Administración; pantalla `Pages/Avisos` y navegación del Portal; `SecretosAviso`, `FirmadorAvisos`, `EmisorAvisosHttp` y `ConectorAvisosHttp`; configuración compartida de API/Worker. Pruebas de dominio, cifrado, persistencia, aplicación, API y emisor. Diseño en `docs/analisis-diseno/cu-63/README.md` y continuación para Ezequiel en `integracion-cu64.md`. Referencia: este chat sobre CU-63. |
| **¿Se usó IA?** | Sí, Codex en la aplicación de escritorio. |
| **Finalidad** | Analizar el caso de uso antes de programar, ajustarlo a los documentos y al modelo, implementar la configuración de suscripciones y su prueba manual, explicar el mecanismo y documentar la integración futura con CU-64. |
| **Cómo se trabajó** | Lucas pidió primero acordar qué hacer y cómo, y aprobó progresivamente entidad, reglas, contratos, persistencia, cifrado, API y Portal. Codex escribió el código, migraciones, pruebas y documentación y ejecutó las verificaciones. Lucas revisó el comportamiento, cuestionó el origen y la presentación del secreto y pidió cambios concretos de interfaz. No se atribuye a Lucas una escritura manual de ese código ni una reescritura que no consta en el chat. |
| **Aceptado sin modificaciones** | Se mantuvieron las decisiones aprobadas durante el diseño: agregado de Seguimiento vinculado a la relación comercial; URL HTTPS y tipos `CambioDeEstado`, `EntregaFallida` y `Devolucion`; operador y comercio desde la sesión; secreto cifrado y mostrado al crear o regenerar; desactivación que conserva la configuración. Las consultas usan una proyección sin secreto. La aceptación fue por etapas, no una aprobación sin cambios de todas las propuestas o versiones intermedias de la IA. |
| **Modificado en la interfaz** | A pedido de Lucas, la acción directa «Regenerar secreto» salió del listado y quedó dentro de «Configurar seguridad» y el desplegable «Cambiar la clave de firma», con explicación y confirmación del impacto inmediato. Se presentó como «Integraciones → Avisos automáticos» y se usó «clave de firma» en pantalla, conservando los nombres del modelo y la API. El campo pasó a «URL del sistema receptor», con la ayuda «Dirección HTTPS proporcionada por quien administra tu sistema»; Lucas pidió expresamente omitir la aclaración sobre correo electrónico. |
| **Modificado en la implementación** | Se agregó control de concurrencia sobre el secreto cifrado para impedir que una regeneración con datos anteriores pise otra. Una consulta EF que filtraba después de construir la vista produjo HTTP 500: la IA movió filtro y orden antes de la proyección y repitió las pruebas. También corrigió aserciones que exigían el tipo exacto de excepción criptográfica. Se completó «Enviar prueba» con un único POST firmado, datos ficticios, resultado HTTP o error, límite de 10 segundos y controles de destinos internos, DNS y redirecciones. |
| **Revisión de requisitos y explicaciones** | Se contrastó la implementación con la letra original: RF 28 exige llamadas HTTP salientes y la sección 6.9 exige firma y receptor de prueba; HMAC-SHA256, secreto mostrado una vez y regeneración están concretados en el CU-63 y el modelo del proyecto. Se distinguieron esos requisitos de las decisiones de implementación, como AES-GCM y las cabeceras de firma. La URL receptora debe ser proporcionada por quien administra el sistema del comercio. Se consultaron como referencias la documentación oficial de Stripe sobre firma y renovación de secretos y la guía de OWASP sobre SSRF. |
| **Descartado o limitado** | No se mantuvo la regeneración como acción cotidiana del listado. No se agregaron edición, reactivación ni eliminación fuera del CU documentado. Lucas aclaró que CU-64 corresponde a Ezequiel: no se implementaron su entrega automática, reintentos, cola de fallidos ni el historial de CU-65. Tampoco se presentó una URL de ejemplo como receptor operativo ni el receptor automatizado en memoria como servicio desplegado. La prueba manual no crea envíos reales ni reintenta automáticamente. |
| **Decisiones a poder explicar** | Diferencia entre avisos HTTP al sistema del comercio y correos al destinatario; origen de la URL receptora; secreto de firma por suscripción frente a clave maestra de cifrado compartida por API/Worker; por qué el secreto debe poder descifrarse para firmar; HMAC del cuerpo exacto y la fecha, control de antigüedad y deduplicación por identificador; aislamiento y antifalsificación; límites entre CU-63 y CU-64. |
| **Verificación** | En las ejecuciones registradas en este chat pasaron 83 pruebas unitarias de Seguimiento, 28 de integración de CU-63 con PostgreSQL en Docker y 56 de arquitectura; son conjuntos dirigidos, no toda la suite del repositorio. El receptor de pruebas en memoria verificó el HMAC de forma independiente y simuló 202, 204, 302, 500, desconexión y demora. Un vector de firma se contrastó con `hmac` de Python. En navegador y base desechable se recorrieron alta, muestra y ocultamiento de clave, regeneración, desactivación y recarga, cambios de interfaz y prueba negativa contra un destino interno bloqueado. La compilación final del Portal terminó sin errores ni advertencias. Al escribir esta entrada sólo se revisó la documentación; no se volvieron a ejecutar las pruebas funcionales. |
| **Pendiente de integración del equipo** | Para la demostración conjunta queda disponer del receptor HTTPS desplegado con escenarios de éxito y falla. La guía para Ezequiel documenta el mensaje, cabeceras, vector de firma, tolerancia, configuración y piezas reutilizables para CU-64; dejarla escrita no acredita que Ezequiel haya integrado o validado todavía su implementación. |

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
| 0.10 | 15/10/2026 | Entrada del endpoint de sincronización (CU-52). | Ezequiel Marcenal |
| 0.11 | 16/10/2026 | Entrada de `UnidadDeTrabajo.Sumar`. | Ezequiel Marcenal |
| 0.12 | 16/10/2026 | Entrada de las trazas y la correlación de la sincronización. | Ezequiel Marcenal |
| 0.13 | 16/10/2026 | Entrada de la revisión de la mensajería y sus trazas. | Ezequiel Marcenal |
| 0.14 | 19/10/2026 | Entrada de la caché distribuida con su métrica de aciertos. | Ezequiel Marcenal |
| 0.15 | 19/10/2026 | Entrada de las reglas de entrega pedidas a Administración y la versión de reglas del envío. | Ezequiel Marcenal |
| 0.16 | 20/10/2026 | Entrada de CU-55, confirmar la rendición. | Ezequiel Marcenal |
| 0.17 | 20/10/2026 | Entrada de la PWA de CU-17 y CU-18. | Ezequiel Marcenal |
| 0.18 | 20/10/2026 | Registro retrospectivo del uso de Codex por Lucas: arquitectura, CU-13/14/40/41/43/50/51/54, ADR-0005, acceso y presentación; aporte posterior al listado de CU-55. Reconstruido con chats, Git y documentación, para revisión del responsable. | Lucas Ottonello |
| 0.19 | 21/10/2026 | Entrada de CU-63 basada en este chat: análisis previo, implementación asistida, revisión del origen de los requisitos, ajustes de interfaz pedidos por Lucas, envío de prueba y contrato de continuidad para Ezequiel en CU-64. | Lucas Ottonello |
