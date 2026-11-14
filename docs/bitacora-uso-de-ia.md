# **Bitácora de uso de asistentes de IA**

| Estado | En curso: se agrega una entrada por cada componente trabajado con asistencia de IA |
| :---- | :---- |
| **Fecha** | 14 de noviembre de 2026 (creada el 09/10/2026) |
| **Autores** | Ezequiel Marcenal, Lucas Ottonello y Cristian Reyes |
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
| Claude Code (modelo Claude Opus 5.5), en la aplicación de escritorio | Cristian Reyes | Explicación de conceptos y de los documentos (ADR, fichas), planificación por archivo, revisión de lo que escribí y, cuando lo pedí expresamente, escritura de código, pruebas y documentación de estudio |

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
| **Ampliación de Lucas con Codex · cierre e historial · 22/10/2026** | Lucas reportó una ruta del 20/10 despachada a las 10:44 que figuraba Finalizada, con sus miembros Pendiente y sin cierre en el historial. Codex revisó la persistencia con consultas locales de sólo lectura acotadas al operador: la rendición se había confirmado el 22/10 a las 09:12, con un envío no intentado devuelto al depósito. Escribió el cambio para que Depósito pase por el contrato los envíos totalmente entregados y el dominio de Planificación cierre los miembros como Completada o Fallida (sin entrega); una parada sólo queda Completada si se entregó todo. El detalle muestra la fecha real de la rendición por IEjecucionModuleApi y reconstruye cierres históricos sin modificar registros durante una consulta ni usar el estado actual del envío. |
| **Verificación de esa ampliación** | En este chat pasaron 252 pruebas unitarias, 56 de arquitectura, 6 de ConfirmarRendicionTests y 3 de AislamientoPlanificacionTests. Se verificaron devoluciones, faltantes sin extravío automático, rechazo de entregas ajenas, historial en HTML y consulta histórica sin escrituras. No se atribuye esta implementación de ajustes a la implementación original de Ezequiel. |
| **Ampliación de Lucas con Codex · cierre sin devoluciones · 22/10/2026** | A partir de una captura de la PWA, Lucas planteó cerrar directamente cuando se entregó todo o conservar sólo una confirmación administrativa. Tras contrastar CU-54/55 se implementó la segunda alternativa: si ninguna línea DebeVolver, Depósito muestra únicamente Confirmar cierre de ruta, sin campo de escaneo ni Comparar. Si hay devoluciones o faltantes, conserva los controles de recepción. Codex escribió los cambios de Razor, la adaptación del escenario de pruebas y dos pruebas nuevas: cierre sin enviar Escaneados y rechazo de códigos forzados cuando no debía recibirse nada. El arreglo anterior de Escaneados nulo ya estaba presente al iniciar este ajuste; no se lo atribuye a este trabajo. |
| **Verificación del cierre sin devoluciones** | Pasaron las 15 pruebas dirigidas de ConfirmarRendicionTests; el host completo compiló sin errores ni advertencias y respondió Healthy después del reinicio. La prueba del caso totalmente entregado verifica fecha de cierre, estados Completada, liberación de reservas, envíos todavía Entregado y ausencia de incidencias. Se conservó el cierre definitivo en CU-55; no se inventaron recepciones físicas. |

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
| **Ampliación de la PWA · 22/10/2026** | En el ajuste de cierre sin devoluciones de 3.20, Codex cambió los textos de Rendicion.razor y Home.razor: distingue la recepción de bultos de la confirmación administrativa y muestra Pendiente de cierre en lugar de Pendiente de depósito. El resumen ya no invita a iniciar una declaración que ya fue confirmada. Se actualizó la guía CU-54. La captura de GPS sigue deteniéndose al confirmar la declaración; no se adelantó la finalización definitiva ni la liberación de recursos. La compilación y las 15 pruebas de cierre se registran en 3.20, sin atribuirles una prueba manual en teléfono. |

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
| **Diagnóstico de recursos Blazor · 21–22/10/2026** | Lucas reportó 404 y errores de integridad al descargar WASM/PDB del Portal. Codex comprobó que una compilación aislada del Portal había dejado desalineado el manifiesto del host respecto de sus archivos con huella. Detuvo los procesos verificados del proyecto, compiló Logistica.Api completa y reinició el watcher oculto. Contrastó nombres y hashes SHA-256 de los archivos con el manifiesto, y la API volvió a responder Healthy. No se deshabilitaron integridad ni autenticación; las consultas HTTP sin sesión redirigían al ingreso y no acreditaban una descarga autenticada del binario. |

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
| **Diagnóstico y configuración local del alta · 22/10/2026** | Ante el HTTP 500 al suscribirse en /api/avisos/suscripciones, Codex comprobó que faltaba Seguimiento:Avisos:ClaveCifrado en user-secrets, .env y los contenedores. La clave se usa al cifrar el secreto, por eso el listado podía funcionar sin ella. Después de comprobar que no había suscripciones locales guardadas, generó 32 bytes aleatorios y configuró el mismo valor en AVISOS_CLAVE_CIFRADO de .env ignorado por Git y en user-secrets compartidos por API/Worker. Reinició la API local y recreó API/Worker de Docker, verificando igualdad y longitud sin mostrar el secreto. No se cambió el algoritmo ni se incorporaron claves al repositorio. |
| **Verificación y explicación de claves** | Pasaron las 8 pruebas de SuscripcionesAvisoApiTests con PostgreSQL aislado; la API local respondió Healthy. Se entregaron a Lucas instrucciones para sus compañeros y se distinguieron tres elementos: SEGUIMIENTO_CLAVE_TOKEN firma enlaces públicos; AVISOS_CLAVE_CIFRADO cifra los secretos guardados; la clave mostrada por el Portal pertenece a una suscripción y permite firmar/verificar sus avisos. Se explicó conservar las claves entre reinicios, usar la misma clave maestra para API/Worker que compartan base y no reutilizarla para el seguimiento público. Estas verificaciones no acreditan una nueva suscripción desde el navegador de Lucas. |
| **Texto dirigido al comerciante · 22/10/2026** | Lucas señaló que Base64, HMAC y las cabeceras de firma no se entienden en el Portal. Codex eliminó Cómo verificar la firma de Index.razor y reemplazó la explicación por el propósito de la clave y la indicación de entregársela a quien administra el sistema receptor. Las instrucciones técnicas permanecen en la documentación de integración. Se revisó el diff; no se declaró una prueba funcional nueva por este cambio de texto. |

## **3.35 CU-53 · Posición del vehículo y contratos para CU-66**

| Campo | Registro |
| :---- | :---- |
| **Fecha y responsable** | 21/10/2026 · Lucas Ottonello |
| **Herramienta** | Codex, en este chat |
| **Finalidad** | Analizar e implementar el reporte periódico de posición de la PWA y dejar a Cristian los contratos para el tablero CU-66. |
| **Decisiones del usuario** | GPS y guardado local sin conexión; despacho conoce la posición al sincronizar. Detener al confirmar la declaración de rendición, retener 30 días configurables y advertir sin bloquear si falta ubicación. CU-66 y SignalR quedan a cargo de Cristian. |
| **Trabajo asistido** | Revisión de los CU y ADR existentes, entidad y migración de posiciones, validación en la sincronización de CU-52, evento en memoria posterior al commit, consulta por vehículo, captura común de la PWA y control entre pestañas, indicador de seguimiento, retención periódica, muestras de pendientes y documentación de integración. |
| **Explicación solicitada por Lucas** | Se explicó el recorrido completo desde el GPS hasta el tablero y la responsabilidad de cada clase, contrato y función: puente C#/JavaScript, temporizador, cola cifrada, sesión local, sincronización, handler, entidad, repositorio, configuración EF, migración, consulta de últimas posiciones, evento posterior al commit y retención. Se aclaró la diferencia entre captura y recepción, entre generar y aplicar una migración, y entre dejar contratos disponibles y tener CU-66 integrado. |
| **Decisiones técnicas a revisar y explicar** | Separar captura de recepción para no hacer retroceder el mapa; identidad del vehículo desde Planificación; deduplicación por OperacionId; aceptación de historial anterior a rendición aunque la ruta esté finalizada; estado operativo comprobado y límite inferior por despacho, ya que Ruta no guarda instante de inicio; notificación efímera recuperable por consulta; mantenimiento por operador activo y sin borrar evidencia de rechazos; no conservar recibos GPS exitosos en el dispositivo. |
| **Revisión posterior contra ADR solicitada por Lucas** | La primera implementación usaba limpieza SQL global y eliminaba contenido de rechazos vencidos. Se corrigieron conforme a ADR-0002 §2.4/§4 y ADR-0004 §2.5/§6: ámbitos por operador, contexto fijo y ExecuteDelete con filtro, conservando evidencia. Se trasladó al dominio la regla de jornada, se separó el mapeo del handler y se agregó el aviso durable de rechazos por Outbox. Se corrigió el reintento periódico para que no dependa del GPS o de una ruta abierta, con espera creciente, envío compartido y refresco de hoja. No se alteraron los ADR. La auditoría registra pendientes de CU-52: 5 MB por lote, interfaz persistente de rechazos GPS, métricas completas y exclusión de envíos entre pestañas sin Web Locks. |
| **Resultado de la auditoría comunicado al usuario** | Se verificaron los ADR-0001 a ADR-0006. Las pruebas estructurales de arquitectura pasan, pero no detectaban por sí solas la limpieza global, la pérdida de evidencia ni la ausencia del aviso durable de conflictos. Se corrigió el código en lugar de modificar los ADR para justificarlo. No se afirma cumplimiento completo del ADR-0004 mientras sigan pendientes los puntos de CU-52 enumerados en la auditoría. El tablero, el mapa, SignalR y sus consumidores continúan a cargo de Cristian en CU-66. |
| **Verificación** | En las ejecuciones de este chat pasaron 51 pruebas JavaScript, 232 unitarias .NET y 56 de arquitectura. La API y la PWA compilan y EF Core generó la migración. Las 14 pruebas de integración de CU-53 compilan; se intentó ejecutarlas, incluso fuera del sandbox, pero todas quedaron con error al inicializar el fixture de Testcontainers por la indisponibilidad de Docker, antes de comprobar su comportamiento con PostgreSQL. Quedan prueba en teléfono y aplicación de migración al entorno de desarrollo. Al actualizar esta entrada sólo se revisó la documentación; no se volvieron a ejecutar las pruebas funcionales. |
| **Referencias** | [CU-53](analisis-diseno/cu-53/README.md), [integración para Cristian](analisis-diseno/cu-53/integracion-cu-66.md), [auditoría ADR](analisis-diseno/cu-53/revision-arquitectura.md), CU-52/53/54, ADR-0001/0002/0004 y especificación W3C de geolocalización. |

## **3.36 Entrega de los avisos a los comercios (CU-64)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 21/10/2026 |
| **Archivos** | Envíos: `EnvioAdmitido` y `CrearEnvioHandler`. Seguimiento: `EntregaAviso`, `IntentoAviso`, `EnvioSeguido`, `EstadoEntregaAviso`, `ClasificadorAvisos`, `SuscripcionAviso.Recibe`, `PrepararAvisosHandler`, `EntregarAvisoHandler`, los consumidores `PrepararAvisos` y `EntregarAviso`, `AvisoPendiente`, repositorios, configuración EF y migración `EntregasAviso`. BuildingBlocks: `MensajeIntegracion.Intento`. Pruebas `EntregaAvisoTests` y `EntregarAvisosTests`; ajuste de dos consultas de `ArmarRutaTests`. Documentos: README de CU-64, modelo de dominio y guía de integración de CU-63. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar CU-64 sobre las piezas que dejó Lucas Ottonello con CU-63 (firma, cifrado del secreto y emisor HTTP), para el hito del 29/10. |
| **Cómo se trabajó** | La IA revisó primero el merge de CU-63 y leyó la guía de integración de Lucas. Propuso cuatro decisiones (origen del comercio, clasificación de los hechos, entrega en dos etapas y entidad con intentos); el responsable las aceptó con la condición de no contradecir los ADR y pidió que la IA escribiera el código. |
| **Aceptado sin modificaciones** | Clasificación en un único tipo por hecho, con `CambioDeEstado` como "todos". Entrega en dos etapas: preparar (inbox, entregas y outbox en una transacción) y entregar (un mensaje por entrega, que se reintenta solo). `EntregaAviso` con su colección de intentos, para el historial de CU-65. |
| **Modificado** | La IA había propuesto llevar el `ComercioId` en el contenido de los eventos de Envíos; al verificarlo contra el ADR-0003 (sección 2.2: el inquilino nunca sale del contenido del evento) se reemplazó por `EnvioAdmitido`, publicado desde la sesión del comercio, y la copia `EnvioSeguido` en Seguimiento. También había propuesto confirmar el mensaje al agotar los reintentos; para cumplir la sección 2.6 el último fallo deja la entrega en la cola de fallidos y manda el mensaje a `logistica.fallidos`. Para saber cuándo es el último intento se expuso la cabecera `intento` en `MensajeIntegracion`. |
| **Descartado** | Una excepción para destinos internos en desarrollo: la guía de CU-63 la descarta expresamente. Leer el comercio del contenido de los eventos o cambiar `AgregarAlOutbox` para recibirlo del llamador: contradicen los ADR-0002 y 0003. |
| **Verificación** | 16 pruebas unitarias y 6 de integración nuevas, incluida una de extremo a extremo (Portal, outbox, RabbitMQ, Worker y receptor en memoria que verifica la firma por su cuenta). La primera corrida completa mostró que `ArmarRutaTests` contaba todas las filas del outbox del envío; se restringió la consulta al evento de asignación. Corrida final en Docker: 695 pruebas .NET (233 unitarias, 56 de arquitectura y 406 de integración), todas pasan; cobertura de líneas del 94,7 %. No se probó con `docker compose` ni con un receptor HTTPS público, que todavía no existe. |

## **3.37 Revisión de main e integración con CU-53**

| Campo | Registro |
| :---- | :---- |
| **Fecha y responsable** | 21/10/2026 · Lucas Ottonello |
| **Herramienta y solicitud** | Codex, en este chat. Lucas pidió verificar el último commit de main contra los ADR y resolver el conflicto con su rama. |
| **Origen revisado** | Tras git fetch, origin/main apunta a `04fab20ff1910a145136c1f59ea7ef61f40f5285`, merge de PR #48 con CU-64. Se revisaron los cambios respecto del primer padre y su integración local en lukovski. |
| **Conflicto resuelto** | Únicamente la bitácora: ambas ramas agregaron sección 3.35 y versión 0.20. Se conservaron completos los aportes de Lucas y Ezequiel; CU-53 mantiene 3.35/0.20/0.21 y CU-64 pasa a 3.36/0.22. No se reemplazó una entrada por la otra ni se alteraron sus resultados históricos de pruebas. |
| **Revisión contra ADR** | Se comprobaron los límites de Domain/Application/Infrastructure y Contracts de ADR-0001, los marcadores/filtros y el origen del tenant de ADR-0002, y la persistencia conjunta, Inbox, Outbox y reintentos de ADR-0003. No se encontraron violaciones de dependencias en CU-64. Se señaló el desvío formal de dos índices únicos que no incluyen OperadorId conforme a la convención escrita de ADR-0002 §2.1; los GUID padre son globales y no se demostró una fuga entre operadores. No se cambió el esquema dentro de esta resolución documental. |
| **Límites del alcance** | Los envíos anteriores a EnvioAdmitido, los eventos de transiciones todavía no publicados y el receptor HTTPS público continúan como pendientes documentados de CU-64. Integrar main no resuelve los pendientes de CU-52 identificados en la auditoría de CU-53. No se modificaron los ADR para justificar el código. |
| **Verificación local** | Sobre la integración pasan 248 pruebas unitarias .NET y 56 de arquitectura. Se intentaron las seis de EntregarAvisosTests, también fuera del sandbox: todas fallaron al inicializar los fixtures de PostgreSQL y RabbitMQ con DockerUnavailableException, antes de comprobar el comportamiento funcional. El TRX local queda en TestResults/cu64-main-review, ignorado por Git. No se atribuye a esta revisión la corrida histórica de 695 pruebas declarada por el autor de CU-64. |
| **Referencia** | [Revisión del commit de main](analisis-diseno/cu-64/revision-arquitectura.md). |

## **3.38 Notificar al destinatario por correo (CU-62)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 22/10/2026 |
| **Archivos** | Envíos: destinatario opcional en `EnvioAdmitido`. Administración: `IdentidadVisualActualizada`, su `IIntegrationEventPublisher` y la publicación en `ActualizarIdentidadVisualHandler` (CU-07). Seguimiento: `Notificacion`, `IdentidadOperador`, datos del destinatario en `EnvioSeguido`, `NotificarDestinatarioHandler`, `TextosNotificacion`, `ActualizarIdentidadOperadorHandler`, `IEmisorCorreo`, `EmisorCorreoSmtp` y `PlantillaCorreo` (MailKit), los consumidores `NotificarDestinatario` y `ActualizarIdentidadOperador`, migración `NotificacionesDestinatario`; `HechoEnvio`, `EnviosSeguidos` y `LectorHechosEnvio` pasan a compartirse con CU-64. Paquetes: MailKit 4.18.1 y `System.Security.Cryptography.Pkcs` 10.0.13. Configuración: Compose, `.env.example` y `configurar-secretos.sh`. Pruebas `NotificacionTests` y `NotificarDestinatarioTests`. Documentos: README de CU-62, README de CU-64 y modelo de dominio. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar CU-62 para el hito del 29/10, con Mailpit en desarrollo. |
| **Cómo se trabajó** | La IA relevó qué faltaba (no había envío de correo, y el Worker no tenía el correo del destinatario, la marca del operador ni una dirección pública) y propuso seis decisiones. El responsable las aceptó con la condición de no contradecir los ADR y pidió que la IA escribiera el código. |
| **Aceptado sin modificaciones** | Destinatario como campos opcionales de `EnvioAdmitido` (ADR-0003, sección 2.3). Marca por un evento de CU-07 y copia en Seguimiento, con marca neutra mientras el operador no guarde la suya. Cinco eventos relevantes, una notificación por evento, reintentos de la mensajería y cola de fallidos como en CU-64. `Aplicacion:UrlPublica` para el enlace y MailKit para SMTP. |
| **Modificado** | La compilación falló porque MimeKit trae `System.Security.Cryptography.Pkcs` 10.0.0, con avisos de seguridad, y las advertencias son errores: se fijó la 10.0.13. Un correo mal escrito también queda *NoEnviable*, porque reintentar no lo arregla. `configurar-secretos.sh` no cargaba `Seguimiento:ClaveToken`, que el Worker necesita para firmar los enlaces: se agregó. |
| **Descartado** | Consultar Envíos o Administración desde el Worker: se comunica sólo por la cola. `System.Net.Mail.SmtpClient`, que Microsoft no recomienda para código nuevo. El logo en el correo, que el seguimiento público tampoco muestra. |
| **Verificación** | 15 pruebas unitarias y 6 de integración nuevas, incluida una de extremo a extremo con **Mailpit** en Testcontainers (envío creado en el Portal, outbox, RabbitMQ, Worker y correo recibido con el enlace). Corrida completa en Docker: 716 pruebas .NET (248 unitarias, 56 de arquitectura y 412 de integración); fallaron por tiempo de espera dos ajenas al cambio (`UnidadDeTrabajoTests`, al abrir la conexión a PostgreSQL, y `SeleccionarOperadorTests`, en un request HTTP), que pasaron al repetirlas solas. No se probó con `docker compose` ni contra Brevo. |

## **3.39 Ajustes del historial de avisos del Portal (CU-65)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable y herramienta** | Lucas Ottonello con Codex, en este chat. Registro retrospectivo del 23/10/2026 sobre los ajustes del 21–22/10; no atribuye a Lucas la implementación completa de CU-65. |
| **Solicitud y trabajo asistido** | Lucas consultó el tamaño de página y pidió dar estilo a Limpiar filtros, quitar Filtrar y aplicar filtros al cambiar los campos. Codex verificó el tamaño de 20 avisos en ConsultarHistorialAvisosHandler y editó Historial.razor: enlace de cambios con @bind-Value:after en estado y fechas, botón de limpieza con estilo e icono y eliminación del botón Filtrar. Conservó OnSubmit como alternativa con teclado. |
| **Verificación y límites** | El Portal compiló sin errores ni advertencias. Durante el diagnóstico posterior del HTTP 500 se ejecutaron 16 pruebas de HistorialAvisosTests, todas correctas; el fallo reportado terminó localizado en el alta de suscripciones de CU-63, no en el historial. Esas pruebas no acreditan una nueva prueba manual de los controles en el navegador. |

## **3.40 CU-65 · Consultar el historial de avisos y reenviar los fallidos**

| Campo | Detalle |
| :---- | :---- |
| **Fecha y responsable** | Implementación del 21/10/2026; registro retrospectivo el 23/10/2026. Lucas Ottonello con Codex, en este chat. Complementa los ajustes visuales registrados en 3.39. |
| **Finalidad y acuerdos** | Lucas pidió analizar y luego implementar su CU-65 sobre CU-64 de Ezequiel, una vez incorporado desde main. Se acordó consultar historial e intentos y solicitar reenvíos, conservando en CU-64 la entrega HTTP y sus reintentos automáticos. |
| **Trabajo asistido** | Codex escribió consultas, contratos y endpoints de historial y detalle, reenvío individual y masivo, repositorio, pantalla del Portal, pruebas y `docs/analisis-diseno/cu-65/README.md`. Reutilizó las entregas, intentos y outbox existentes, sin tablas nuevas. El listado pagina de a 20 y filtra por estado y fechas en la zona horaria del operador; el detalle muestra intentos, códigos HTTP y errores, sin secretos. |
| **Aceptado e implementado** | Sólo se reenvían entregas EnColaDeFallidos con suscripción activa; Fallida sigue bajo reintento automático. Cada lote admite entre 1 y 50 identificadores y se valida completo antes de cambiar nada. Bloqueos ordenados de entregas y suscripciones impiden solicitudes concurrentes duplicadas; el paso a Pendiente y AvisoPendiente se guardan juntos. Se conservan identificador e intentos anteriores; 202 acredita encolado, no recepción exitosa. Se mantienen autorización, aislamiento por operador/comercio y antifalsificación. |
| **Modificado a pedido de Lucas** | Se quitaron los botones Actualizar de las pantallas de avisos. Se explicó que desactivar una suscripción conserva su configuración e historial; no se agregó borrado. Los ajustes posteriores de filtros se detallan en 3.39. |
| **Descartado o limitado** | No se reenvían avisos exitosos, pendientes o todavía bajo reintento automático, ni se acepta parcialmente un lote inválido. CU-65 no implementa el receptor del comercio ni modifica la firma o el emisor de Ezequiel. Se muestran códigos y errores persistidos; no se inventa un cuerpo de respuesta HTTP que CU-64 no guarda. |
| **Verificación** | Pasaron 248 pruebas unitarias, 56 de arquitectura y las 64 de integración de Seguimiento, incluidas las 16 de CU-65. El Portal compiló en Release sin advertencias. En navegador y base temporal se revisaron listado, detalle y reenvío que pasa a Pendiente conservando el intento anterior. Se cubrieron concurrencia, aislamiento, filtros, lotes y rollback de estado/outbox. Son resultados de aquella implementación, no pruebas nuevas ejecutadas al actualizar esta bitácora. |

## **3.41 CU-55 · Escaneo vacío y confirmación de faltantes en Backoffice**

| Campo | Detalle |
| :---- | :---- |
| **Responsable y herramienta** | Lucas Ottonello con Codex, en este chat; registro retrospectivo del 23/10/2026. Aporte sobre CU-55 de Ezequiel, complementario de 3.20. |
| **Solicitud y diagnóstico** | Lucas reportó HTTP 500 al dejar vacío el campo de bultos que vuelven. Codex localizó el problema en el enlace del formulario: Escaneados podía ser nulo y el procesamiento llamaba Split directamente. |
| **Trabajo asistido y revisión del usuario** | Se admitió el valor nulo y se lo normalizó a cadena vacía antes de separar los códigos: vacío equivale a cero recibidos. Lucas pidió además advertir antes de confirmar si faltan bultos. Codex agregó comparación en el servidor y un segundo paso con Confirmar con faltantes o Volver y revisar bultos; la primera solicitud con faltantes muestra el aviso sin confirmar la rendición. Sin faltantes se conserva la confirmación directa. |
| **Límites** | No se bloquea definitivamente el cierre por faltantes ni se declara extravío de forma automática. Esta corrección del formulario y su confirmación es anterior al ajuste de cierre sin devoluciones registrado en 3.20. |
| **Verificación** | Pasaron las 13 pruebas dirigidas de ConfirmarRendicionTests de esa etapa. Se revisó el manejo de entrada vacía y el paso de reconocimiento explícito antes de confirmar faltantes. |

## **3.42 CU-43 · Volver a editar una ruta despachada sin iniciar**

| Campo | Detalle |
| :---- | :---- |
| **Fecha y responsable** | Ampliación del 22/10/2026; registro el 23/10/2026. Lucas Ottonello con Codex, en este chat. Complementa 3.27. |
| **Solicitud y trabajo asistido** | Lucas pidió extender la posibilidad de volver atrás de una ruta anterior a la edición de rutas ya validadas, manteniendo los requisitos y exigiendo motivo. Codex revisó que no existe un estado persistido Validada y amplió la anulación de despacho sin iniciar a rutas de hoy y futuras, además de las anteriores. Adaptó dominio, handler, API, interfaz, pruebas y `docs/analisis-diseno/cu-43/anulacion-despacho-sin-iniciar.md`. |
| **Aceptado e implementado** | Volver a editar la ruta exige motivo de hasta 1000 caracteres, revisión vigente, coordinación con el repartidor y ausencia de carga, rendición y actividad aplicada. Vuelve a Planificada, conserva envíos y evidencia y registra auditoría y outbox en la misma transacción. Editar y volver a despachar comprueba nuevamente recursos, fechas, capacidad y demás reglas existentes. |
| **Límites y explicación** | No permite reabrir rutas EnCurso o Finalizada ni borra el despacho histórico. Una ruta Planificada con envíos mantiene las reservas de su fecha original; cambiar fecha las actualiza y retirar el último envío las libera. No se presentó la reapertura como liberación inmediata de todos los recursos. Ante la consulta sobre el repartidor sin cuenta, se explicó el alta e invitación desde Administración → Usuarios con perfil Repartidor y selección del recurso; no se implementó una pantalla nueva para vincular cuentas existentes. |
| **Verificación** | Pasaron 56 pruebas de integración de reapertura, armado y despacho y 20 unitarias de Planificación: fechas actuales y futuras, motivo obligatorio, reservas, auditoría, edición inválida por capacidad, edición válida y nuevo despacho. |

## **3.43 CU-51 · Simplificación de la recuperación del avance de carga**

| Campo | Detalle |
| :---- | :---- |
| **Responsable y herramienta** | Lucas Ottonello con Codex, en este chat; registro retrospectivo del 23/10/2026. Complementa 3.30. |
| **Solicitud y trabajo asistido** | Lucas señaló que no entendía Recuperar avance guardado y pidió retirarlo. Codex comprobó que abrir la ruta ya recupera el borrador y eliminó el botón de `src/Logistica.Repartidor.Pwa/Pages/Carga.razor`. Agregó el texto Tu avance se guarda automáticamente en este dispositivo. |
| **Alcance y verificación** | Se revisó la recuperación existente al abrir la ruta y el guardado local; se conservó ese comportamiento. Se registró el cambio de interfaz en el commit da54c51. No se atribuye a este ajuste una implementación nueva del almacenamiento ni una nueva demostración completa sin conexión. |

## **3.44 Diagnóstico y estabilización de la ejecución de pruebas en CI**

| Campo | Detalle |
| :---- | :---- |
| **Fecha y responsable** | 22/10/2026; registro retrospectivo el 23/10/2026. Lucas Ottonello con Codex, en este chat. |
| **Solicitud y diagnóstico** | Lucas compartió el fallo de CI en main 7e57323 y pidió corregirlo. Los datos remotos mostraban cancelación del paso de pruebas y construcción de imágenes correcta; no se obtuvo un diagnóstico remoto de memoria concluyente. La reproducción completa en Linux mostró consumo superior a 12 GB y agotamiento de memoria; se investigó la acumulación del catálogo de recursos estáticos de Blazor en los hosts de pruebas. |
| **Trabajo asistido** | Codex modificó `.github/workflows/ci.yml`, `coverage.runsettings`, `scripts/test-en-docker.sh`, `Program.cs`, el fixture y la configuración del proyecto de integración. Se serializaron las clases de integración, se limitó a 4 GiB el heap administrado de cada proceso de pruebas y se cerraron servidores de compilación antes de ejecutar. El fixture de pruebas de negocio usa un manifiesto estático vacío; las pruebas de publicación de las aplicaciones WebAssembly conservan el catálogo real. La configuración opcional del host mantiene el comportamiento habitual cuando no se proporciona ese manifiesto. |
| **Diagnóstico y resultados conservados** | Se agregaron tiempos máximos del trabajo y del paso, detección de pruebas bloqueadas y archivos TRX como artefactos incluso ante fallos. El script de Docker separa resultados por ejecución y genera cobertura sobre esa corrida. Se documentaron los cambios en README y se publicó el commit 031fef0 en la rama de Lucas, incorporado por PR #50. No se eliminaron pruebas ni la concurrencia ejercitada dentro de ellas. |
| **Verificación y límites** | La corrida completa final en Docker pasó 753 pruebas: 252 unitarias, 56 de arquitectura y 445 de integración; estas últimas demoraron 8 min 40 s. Compilación sin errores ni advertencias; cobertura de líneas 94,8 % y ramas 78,9 %. Al consultar la ejecución de GitHub tras el push, docker-images había aprobado y build-and-test seguía en cola; ese estado no se registra como CI completo aprobado. Se explicó que los chequeos se disparan al actualizar el PR o main, sin exigir otro commit sólo para iniciarlos. |

## **3.45 CU-63 · Preparación de un receptor HTTPS temporal para pruebas**

| Campo | Detalle |
| :---- | :---- |
| **Fecha y responsable** | 23/10/2026 · Lucas Ottonello con Codex, en este chat. Complementa 3.34. |
| **Solicitud y trabajo asistido** | Lucas pidió un endpoint falso para comprobar la llegada de los avisos. Codex revisó el emisor y las restricciones de destinos, consultó la documentación oficial de Webhook.site y creó allí un receptor HTTPS temporal con respuesta 200. Entregó la URL y el panel de mensajes, junto con los pasos para configurar la suscripción en el Portal y pulsar Enviar prueba. No se incorporó un servicio nuevo al repositorio ni se habilitaron destinos internos. |
| **Verificación y límites** | Se envió un POST sintético, se comprobó HTTP 200 y se consultó la API del receptor para verificar que conservaba método y cuerpo. No se ejecutó desde el Portal una nueva prueba de CU-63 ni se acreditó la recepción automática de CU-64. Este capturador permite inspeccionar mensajes y cabeceras, pero no valida HMAC ni sustituye al receptor con escenarios de error y demora pendiente para la defensa. Se recordó que la prueba manual de CU-63 no genera historial en CU-65. |
| **Referencia** | [Ejemplos oficiales de la API de Webhook.site](https://docs.webhook.site/api/examples.html). La URL temporal entregada en el chat no se incorpora como configuración permanente del proyecto. |

## **3.46 Evento de la recepción en depósito (T2, CU-30) para CU-64 y CU-60**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 26/10/2026 |
| **Archivos** | Envíos: `EnvioRecibidoEnDeposito` y `EnviosModuleApi.RecibirEnDepositoAsync`. Seguimiento: `PrepararAvisos.Tipos`, `InvalidarSeguimiento.Tipos` y `LectorHechosEnvio`. Pruebas: `RecibirBultoTests`, `EntregaAvisoTests` y `EntregarAvisosTests`. Documentos: README de CU-64. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cerrar el pendiente de CU-64: la transición T2 no publicaba evento, así que la llegada al depósito no se avisaba al comercio y el seguimiento público seguía mostrando *Admitido* hasta que vencía la caché. |
| **Cómo se trabajó** | El responsable señaló el pendiente del README de CU-64. La IA ubicó la transición en `RecibirEnDepositoAsync`, propuso el cambio y lo escribió, con sus pruebas, a pedido del responsable y sin contradecir los ADR. |
| **Aceptado sin modificaciones** | El evento sigue el nombre de la tabla de transiciones y del catálogo (`EnvioRecibidoEnDeposito`, sección de CU-30), se registra en el outbox de Envíos en la misma transacción que la recepción, igual que T8, y Seguimiento lo traduce al estado `EnDeposito`. El sobre no trae comercio (lo publica el operario de depósito): el comercio sale de `EnvioSeguido`, como en los demás eventos (ADR-0003, sección 2.2). |
| **Modificado** | — |
| **Descartado** | Notificar la recepción al destinatario: no figura entre los cambios relevantes de CU-62. |
| **Verificación** | Un caso unitario y una prueba de integración nuevos, más la verificación del outbox en `RecibirBultoTests`. Corrida completa en Docker, junto con CU-09: 785 pruebas .NET (271 unitarias, 56 de arquitectura y 458 de integración), todas pasan; cobertura de líneas del 94,6 %. No se probó con `docker compose`. |

## **3.47 Confirmar la devolución al comercio (CU-31)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 27/10/2026 |
| **Archivos** | Envíos: `Devolucion.MarcarRecibida` y `Devolucion.Cerrar`, `EnvioDevuelto`, `DevolucionParaEntrega`, los métodos nuevos de `IEnviosModuleApi` y del repositorio. Depósito: `ConfirmarDevolucionHandler`, la página `/backoffice/devoluciones` y el registro en `ConfirmarRendicionHandler` (CU-55). Seguimiento: `EnvioDevuelto` en los avisos, la caché y `ClasificadorAvisos`. Menú del Backoffice. Pruebas: `DevolucionTests`, `ConfirmarDevolucionTests` y `EntregaAvisoTests`. Documentos: casos de uso, modelo de dominio y README de CU-64. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar CU-31 en nivel mínimo: el operario registra que el comercio recibió el envío devuelto (T15) y quién lo recibió. |
| **Cómo se trabajó** | La IA relevó lo que existía (la devolución con los campos del receptor, sin métodos que la cerraran; T15 en la tabla; sin `EnvioDevuelto`) y propuso cinco decisiones. El responsable las aceptó con la condición de no contradecir los ADR y pidió que la IA escribiera el código, las pruebas y la documentación. |
| **Aceptado sin modificaciones** | Pantalla y coordinación en Depósito, reglas en Envíos, como CU-30 y CU-55. CU-55 deja la devolución recibida en el depósito, y sólo esa se puede cerrar (A1 ampliado). Entrega parcial con confirmación e incidencia (A2). Nombre y documento obligatorios. `EnvioDevuelto` por Outbox, clasificado como `Devolucion` en los avisos. |
| **Modificado** | La primera corrida mostró que, si la confirmación no se completa (A1, A2 o falta el receptor), volver a consultar en el mismo request falla porque la transacción ya terminó. Como en CU-55, la página vuelve con un GET nuevo y lo que cargó el operario viaja en TempData. También que TempData devuelve como `Guid` un texto con esa forma: con un solo bulto marcado, la página volvía a marcarlos todos; se lee con `ToString()`. |
| **Descartado** | Notificar la devolución al destinatario: no figura entre los cambios relevantes de CU-62. Un listado de devoluciones pendientes: el caso de uso empieza por la búsqueda del envío. |
| **Verificación** | 7 casos unitarios y 6 pruebas de integración nuevas (rendición que deja la devolución recibida, entrega completa con el evento, entrega parcial con la incidencia, A1, receptor obligatorio, permisos y aislamiento entre operadores). Corrida completa en Docker: 798 pruebas .NET (278 unitarias, 56 de arquitectura y 464 de integración), todas pasan; cobertura de líneas del 94,7 %. No se probó con `docker compose`. |

## **3.48 Ordenar las paradas de una ruta (CU-42)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 02/11/2026 |
| **Archivos** | Planificación: `OrdenadorParadas.cs`, `Ruta.OrdenarParadas` y `Ruta.MoverParada`, `OrdenarParadasHandler`, `OrdenarParadasEndpoint`, la página `/backoffice/rutas/{id}/orden`, el registro en `PlanificacionModule`, el detalle de ruta y el script de `ConfirmacionQuitarEnvio.cshtml`. DTOs en `Logistica.Http.Contracts/Planificacion/OrdenParadas.cs`. Pruebas unitarias y de integración `OrdenarParadasTests`. Documentación en `docs/analisis-diseno/cu-42/README.md`. |
| **¿Se usó IA?** | Sí, Codex en la aplicación de escritorio. |
| **Finalidad** | Implementar CU-42/RF 16: ordenar por franja horaria, zona y código postal, permitir ajuste manual del despachador y confirmar antes de guardar. Analizar si podía desarrollarse en paralelo a las decisiones pendientes de entrega parcial (RF 31 a 36). |
| **Cómo se trabajó** | Lucas solicitó la implementación. Codex leyó la ficha de CU-42, el modelo de dominio, la agrupación de paradas y los patrones de los casos de Planificación existentes; escribió el código, las pruebas y la documentación. A pedido de Lucas explicó el flujo, la distribución por capas, el criterio de orden y cómo probarlo en el front, y contrastó explícitamente el resultado con los ADR relevantes. Luego Lucas pidió cambiar el modo de edición de la pantalla. |
| **Aceptado sin modificaciones** | Se mantuvo la propuesta automática revisable, el ajuste con Subir/Bajar y la persistencia sólo al confirmar. El criterio ordena por inicio de franja, fin como desempate entre ventanas, zona y código postal; las visitas sin franja quedan al final y los empates mantienen el orden previo. Se conservan identidades y miembros de las paradas, recursos y reservas; un cambio real incrementa una vez la revisión. |
| **Modificado** | La recuperación de errores se ajustó para leer desde un scope nuevo y no reutilizar entidades rastreadas por una transacción fallida. En las pruebas se corrigió la limpieza de una ruta que quedaba EnCurso y ocupaba el repartidor y el vehículo de los casos siguientes. Por pedido explícito de Lucas, Editar paradas pasó a llamarse Editar, con color amarillo: Agregar envíos y Ordenar paradas quedan ocultos hasta activar la edición, junto con las acciones de quitar; Terminar edición vuelve a ocultarlos. Se mantuvo la posibilidad de activar Editar en una ruta vacía para agregar envíos. |
| **Arquitectura y ADR** | Caso localizado en Planificación como vertical slice, con reglas en Domain, coordinación en Application y página/endpoints en Presentation (ADR-0001). Lecturas de Envíos y Administración exclusivamente por contratos públicos, sin acceder a sus DbContext. Tenant de sesión, repositorio filtrado y protección de escrituras existentes (ADR-0002); perfil Despachador y antiforgery. Confirmación con unidad de trabajo, bloqueo por operador y revisión esperada, como CU-40/41/43. No se agregó Outbox porque no se introdujo una reacción asíncrona durable (ADR-0001/0003). No se alteraron los contratos offline ni la política del ADR-0004: sólo se ordenan rutas Planificadas. |
| **Descartado y límites** | No se incorporó optimización de distancias ni una estimación ficticia de viaje: no hay duraciones de traslado o visita para calcularla. A1 usa una cota inferior sin traslados para advertir órdenes incompatibles con las franjas y permite guardar con confirmación explícita; no garantiza puntualidad. El criterio fijo no requiere un ADR nuevo, según la ficha de CU-42. No se adelantaron decisiones sobre estados de bultos, evidencia, intentos ni sincronización parcial; RF 34 podrá reutilizar el ordenamiento de las visitas resultantes. |
| **Verificación** | Compilación de la solución sin errores ni advertencias. Antes del último ajuste de visibilidad de botones aprobaron 28 pruebas unitarias de Planificación, 56 de arquitectura, 11 de integración de CU-42 y 35 regresiones de rutas: 130 escenarios distintos, no la suite completa. Incluyen persistencia, agrupación, advertencias, listas inválidas, permisos, aislamiento, antiforgery, concurrencia y formularios. La primera ejecución quedó bloqueada por Docker; Lucas recuperó el motor y se completaron las pruebas, tras corregir la limpieza mencionada. Después del ajuste de Editar se recompiló correctamente el módulo; no se repitió la integración para ese ajuste ni se hizo inspección visual automatizada en navegador. Logs y TRX en `TestResults/cu-42/` (ignorados por Git); las corridas iniciales fallidas y su corrección se distinguen en el README. |

## **3.49 Decisión de entrega parcial y ADR-0007**

| Campo | Detalle |
| :---- | :---- |
| **Responsable y fecha** | Lucas Ottonello · 02/11/2026 |
| **¿Se usó IA?** | Sí, Codex. |
| **Finalidad** | Registrar en el repositorio el acuerdo funcional cerrado y crear el ADR exigido para el cambio de RF 31 a 36. |
| **Cómo se trabajó** | Se analizaron los borradores aportados por Cristian y el código disponible. Lucas confirmó En operación como nombre del resumen y pidió documentar la decisión cerrada. Codex verificó las secciones 8.2 de la letra y 2.3 del cambio, inspeccionó el estado actual y redactó ambos documentos y sus enlaces. |
| **Decisión registrada** | Estados operativos en bultos; resumen persistido y derivado del envío con Creado, En operación, Entregado parcialmente y Entregado; Terminado y desglose; visita por envío, atomicidad, evidencia, reglas y recargo de revisita según lo acordado en este chat. El criterio técnico de inicio de ejecución y demás contratos detallados quedan explícitamente pendientes. |
| **Alternativas y límites** | Se registraron alternativas descartadas y consecuencias. Se distingue decisión aceptada de implementación pendiente: las pruebas de CU-42 no se presentan como validación de parcial. La retrospectiva exigida por el docente queda preparada para completar con commits y evidencia después de implementar. No se atribuyen aprobaciones individuales adicionales a los compañeros. |
| **Verificación** | Revisión documental, enlaces relativos y consistencia de nombres, referencias y estados de avance. Esta tarea modifica documentación, no código; no se ejecutaron pruebas nuevas de negocio. |

## **3.50 Importar envíos desde un archivo (CU-11)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 03/11/2026 |
| **Archivos** | Envíos: `Envio.ReferenciaComercio` y `Envio.NormalizarReferencia`, `ReferenciaRepetidaException`, el índice único y su migración, `EnvioRepository`, `CrearEnvioHandler` (cotización separada y alta idempotente), `ImportarEnviosHandler`, `IAmbitoPorEnvio` y `AmbitoPorEnvio`, `LectorArchivoEnvios` y `ImportarEnviosEndpoint`. Contratos HTTP `ImportarEnviosRequest`. Portal: página `/envios/importar`, menú y plantilla CSV. Pruebas: `EnvioTests`, `ImportarEnviosTests` unitarias y de integración. Documentos: casos de uso y modelo de dominio. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar CU-11 en nivel mínimo con la idempotencia completa que exige RF 7: reimportar un archivo, completo o en parte, no duplica envíos. |
| **Cómo se trabajó** | La IA propuso un plan en siete pasos para que el responsable escribiera el código; el responsable le pidió que hiciera el caso de uso completo. La IA relevó CU-10, el patrón de ámbito por operación de CU-52 y la propuesta de API pública de Lucas (CU-81 necesita la misma referencia), y escribió el código, las pruebas y la documentación. |
| **Decisiones** | Índice único sobre (operador, comercio, referencia) en lugar de un `RelacionComercialId` en el envío; los NULL del alta individual no chocan. La referencia se recorta, distingue mayúsculas y tiene hasta 50 caracteres. La cotización de CU-10 se separó para que la vista previa la reutilice sin crear el envío ni consumir un número. Cada envío corre en un ámbito de DI propio (como CU-52), porque un guardado fallido dejaría sus cambios en el DbContext. Ante dos importaciones simultáneas (A4), el índice rechaza la segunda y esta informa el envío existente. El archivo viaja en base64 dentro del JSON y la vista previa no se guarda: confirmar lo vuelve a enviar. CSV leído con `TextFieldParser` de .NET, separado por punto y coma o coma. |
| **Descartado** | La descarga del informe por fila: se muestra en pantalla, que alcanza para el nivel mínimo. Sumar el ambiente de pruebas a la unicidad: lo agrega CU-81 cuando exista `EsPrueba`. |
| **Verificación** | 22 casos unitarios (referencia del envío, lectura del archivo y armado de cada envío) y 6 pruebas de integración (vista previa y confirmación, reimportación completa y ampliada, errores por envío, A1, A2 y dos importaciones simultáneas). Corrida completa en Docker: 950 pruebas .NET (357 unitarias, 56 de arquitectura y 537 de integración), todas pasan; cobertura de líneas del 95,2 %. Después se agregó arrastrar y soltar el archivo, sin JavaScript (el `InputFile` transparente cubre la zona), y la página se probó en el navegador con `docker compose`: vista previa de un CSV y rechazo de un archivo que no es CSV. El arrastre real con el mouse no se pudo automatizar. |

## **3.51 Generar la liquidación de un comercio (CU-32)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 03/11/2026 |
| **Archivos** | Depósito: `Liquidacion` y `LineaLiquidacion`, `ILiquidacionRepository` y `LiquidacionRepository`, configuraciones y migración, `GenerarLiquidacionHandler`, las páginas `/backoffice/liquidaciones` y `/backoffice/liquidaciones/{id}`. Envíos: `ListarLiquidablesAsync` en el contrato, `EnvioCerrado` y `ListarCerradosAsync`. Administración: `ConsultarComerciosAsync` y `ComercioRelacionado`. Menú del Backoffice. Pruebas: `LiquidacionTests` y `GenerarLiquidacionTests`. Documentos: casos de uso y modelo de dominio. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar CU-32 en nivel básico: el administrador genera la liquidación de un comercio y un período, la emite y registra el pago. |
| **Cómo se trabajó** | El responsable pidió que la IA continuara con CU-32 después de CU-11, sin consultas porque no iba a estar disponible. La IA tomó las decisiones y las dejó documentadas. |
| **Decisiones** | Los envíos liquidables se piden al contrato síncrono de Envíos en lugar de mantener una copia alimentada por eventos, como decía el requerimiento especial 1: `EnvioEntregado` y `EnvioDevuelto` no traen la tarifa, los consumidores de RabbitMQ sólo corren en el Worker (sumar uno a la API cambiaba la arquitectura) y la definición de envío liquidable cambia con la entrega parcial. El instante liquidable sale del evento de la transición (T6 o T15) y el período son días completos en la zona horaria del operador. Índice único sobre el envío de la línea (A2 y requerimiento 3). La liquidación guarda operador y comercio en lugar de `relacionComercialId`. A pedido del responsable se agregó A4: un borrador se elimina con sus líneas (borrado en cascada) y sus envíos quedan libres para otra liquidación; una emitida o pagada no se elimina, y si otro administrador la emite mientras tanto, la concurrencia optimista rechaza el borrado. |
| **Descartado** | Publicar `LiquidacionEmitida`: no tiene consumidor ni aviso definido. Los ajustes, que quedan en cero porque el requerimiento 2 cobra sólo la tarifa de admisión. La generación desde el cierre diario: corresponde a CU-70. |
| **Verificación** | 6 casos unitarios del agregado y 2 pruebas de integración (generación con entregados, devueltos, cancelados, otro mes y la última hora del período en la zona del operador; regeneración sin duplicados; emisión y pago; aislamiento entre operadores y perfil). Las de integración llevan el envío a su estado final por SQL, sin recorrer la ruta. Corrida completa en Docker: 958 pruebas .NET (363 unitarias, 56 de arquitectura y 539 de integración), todas pasan; cobertura de líneas del 95,4 %. No se probaron las páginas en un navegador ni con `docker compose`. Con A4: un caso unitario y una prueba de integración más (borrado del borrador, regeneración con el mismo envío, rechazo sobre una emitida y 404 para otro operador); corrida completa en Docker con 960 pruebas (364 unitarias, 56 de arquitectura y 540 de integración), todas pasan, cobertura del 95,4 %. El botón se vio en el navegador sobre un borrador creado en la base local. |

## **3.52 Consultar la cuenta corriente y las liquidaciones (CU-33)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 03/11/2026 |
| **Archivos** | Depósito: `ConsultarCuentaCorrienteHandler`, `ListarEmitidasAsync` en `ILiquidacionRepository` y `LiquidacionRepository`, `ConsultarCuentaCorrienteEndpoint` (consulta, detalle y CSV). Contratos HTTP `CuentaCorrienteResponse`. Portal: página `/cuenta-corriente`, menú e inicio. Pruebas: `ArchivoLiquidacionTests` y `ConsultarCuentaCorrienteTests`. Documentos: casos de uso. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar CU-33 en nivel básico: el comercio consulta su saldo pendiente, sus liquidaciones emitidas y pagadas, el detalle por envío y lo descarga. |
| **Cómo se trabajó** | El responsable pidió terminar el caso de uso antes de seguir con el despliegue. La IA relevó la ficha, el patrón de las páginas del Portal y de los endpoints con la política del comercio, y escribió el código, las pruebas y la documentación. |
| **Decisiones** | El saldo pendiente es la suma del total neto de las liquidaciones emitidas; las pagadas se listan pero no suman. Una liquidación ajena o en borrador responde 404 sin distinguir el motivo (ADR-0002). La fecha de cada línea se muestra en días del operador, como el período de CU-32. El handler, además del filtro "Tenant", compara el comercio de la sesión. La descarga es un CSV con punto y coma, coma decimal y BOM, y el Portal la ofrece como un enlace común: la cookie viaja sola y no hace falta JavaScript. |
| **Descartado** | La descarga en PDF: el nivel básico pide poder descargarla, y el CSV se abre en cualquier planilla. Un detalle del envío en el Portal desde cada línea: el Portal todavía no tiene esa página. |
| **Verificación** | Un caso unitario del CSV (BOM, separadores, coma decimal y totales) y 2 pruebas de integración (borradores ocultos, emisión que suma al saldo, detalle con la fecha en días del operador, descarga, pago que descuenta del saldo; el mismo comercio con otro operador no la ve y el personal del operador recibe 403). Corrida completa en Docker: 963 pruebas .NET (365 unitarias, 56 de arquitectura y 542 de integración), todas pasan; cobertura de líneas del 95,4 %. Las páginas se probaron en el navegador con `docker compose`: listado, detalle y descarga. |

## **3.53 Suite de pruebas más rápida**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 03/11/2026 |
| **Archivos** | `PostgresApiFactory` (PostgreSQL compartido y `PostgresCompartido`), `ConfiguracionPruebas.cs` (paralelismo y colección `ContenedoresPropios`), las siete clases con contenedores propios, `RabbitMqFixture`, `scripts/test-en-docker.sh`, `README.md`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Bajar la suite completa de unos 25 minutos, que frenaba cada caso de uso, sin perder el aislamiento por clase ni la cobertura. |
| **Cómo se trabajó** | La IA midió la corrida con los tiempos de los TRX: 12,7 minutos de pruebas en serie y 10,3 de huecos entre clases (74 arranques de unos 8 s: contenedor, migraciones de siete esquemas, datos iniciales y API). A pedido del responsable revisó antes que ninguna prueba dependiera del orden o de estado compartido: no hay estáticos mutables ni configuración global; los oyentes de trazas y métricas filtran por su traza o su caché; los contenedores propios publican puertos al azar; las cuentas fijas se limitan a datos de la propia prueba. |
| **Decisiones** | Un solo PostgreSQL para la suite y una base por clase clonada de una plantilla ya migrada y sembrada; las clonaciones van de a una, porque PostgreSQL no copia una plantilla en uso, y `max_connections` sube a 300 por los pools de las API en paralelo. Las clases corren en paralelo hasta cuatro a la vez, el número de núcleos de los runners de GitHub; las pruebas de una clase siguen en serie. Las siete clases con contenedores propios y esperas de 30 a 45 s corren solas, en una colección sin paralelismo. `RabbitMqFixture` espera el mensaje "Server startup complete" en lugar del puerto: el puerto abre antes de que el broker esté listo, y eso causó el fallo intermitente de `TrazasMensajeriaTests` con la máquina cargada. El script acepta un filtro sin cobertura para el trabajo diario y guarda los paquetes de NuGet en un volumen. Se mantienen el límite de 4 GiB de heap por proceso y el catálogo estático vacío de la entrada 3.44. |
| **Verificación** | Corrida completa en Docker: 963 pruebas (365 unitarias, 56 de arquitectura y 542 de integración), todas pasan, cobertura del 95,4 %, en 8 min 29 s en total (integración en 7 min 4 s, antes 17,5 a 23 min). Con filtro, las 66 pruebas de Depósito, compilación incluida, en 2 minutos. En el CI del 03/11, el paso de pruebas bajó de 8 min 46 s (última corrida de main, 02/11) a 4 min 49 s, y `build-and-test` completo tardó 6 minutos. |

## **3.54 CU-80 · Gestionar las claves de API**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 02–03/11/2026 |
| **Archivos** | Administración: `ClaveApi` en `Domain/Comercios`, handlers en `Application/Features/GestionarClavesApi`, repositorio, configuración y migración en `Infrastructure/Persistence`, endpoints en `Presentation/Features/GestionarClavesApi`. Contrato de autenticación en Administración.Contracts, DTOs en Http.Contracts y pantalla `ClavesApi.razor` en el Portal. Esquema HTTP en `Logistica.Api/ApiPublica`. |
| **¿Se usó IA?** | Sí, Codex. |
| **Finalidad** | Analizar el opcional 7.4 y luego implementar el alta, listado y revocación de claves por relación comercial, con una prueba del uso real de la credencial. |
| **Cómo se trabajó** | Lucas pidió contrastar los casos y el cambio de requerimientos con los ADR. Codex escribió la propuesta, código, migración y pruebas. Se implementó primero la gestión del Portal y se conectó después la autenticación al alta de CU-81. |
| **Aceptado sin modificaciones** | Claves separadas por relación y ambiente, secreto aleatorio de 32 bytes visible una vez, SHA-256 e índice único del hash. Revocación lógica y consulta de vigencia en base. La sesión de Identity autoriza la gestión del Portal. |
| **Modificado** | La auditoría acotó la excepción del ADR-0002 a la consulta de la clave por hash. La relación activa se comprueba con el tenant fijado y el último uso se guarda con `SaveChanges`. El contrato se atiende desde un handler de Application y la persistencia queda en el repositorio de Administración. |
| **Descartado** | Recuperar el secreto, compartir credenciales entre operadores y almacenar en caché una clave activa. La consulta directa permite que la siguiente solicitud observe una revocación ya confirmada. |
| **Verificación** | `ClavesApiTests` cubre ambos tipos, secreto y hash, aislamiento y protección antifalsificación. `CrearEnvioApiTests` verifica Portal → alta → último uso → revocación → 401. La revisión añadió la comprobación de que una relación suspendida rechaza la clave y conserva el último uso. |

## **3.55 CU-81 · Crear envíos por API**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 03/11/2026 |
| **Archivos** | Envíos: `Presentation/Features/CrearEnvio/CrearEnvioApiEndpoint.cs`, handler y command de CU-10, agregado, repositorio, filtro de pruebas y migración. Evento `EnvioAdmitido` en Envíos.Contracts. Seguimiento: lectores y consumidores de avisos, `EntregaAviso` y migración. Contratos HTTP en Http.Contracts. Cuotas en `Logistica.Api/ApiPublica`. |
| **¿Se usó IA?** | Sí, Codex. |
| **Finalidad** | Exponer el alta de envíos de CU-10 por API versionada y completar CU-12. Mantener el aislamiento de prueba y la idempotencia de la referencia comercial. |
| **Cómo se trabajó** | Lucas transmitió la indicación de Ezequiel de reutilizar `ReferenciaComercio` y `CrearOObtenerAsync` de CU-11. Codex contrastó la rama y adaptó el código existente, escribió las migraciones y verificó los flujos HTTP y de mensajería. |
| **Aceptado sin modificaciones** | `POST /api/v1/envios` reutiliza el handler y conserva el origen API. La unicidad distingue relación y ambiente. Envíos confirma el alta y el Outbox juntos según ADR-0003. El filtro `Prueba` conserva `Tenant`. Seguimiento marca el aviso al comercio y omite el correo al destinatario de prueba. Las cuotas son configurables por relación y ambiente y se aplican por instancia. |
| **Modificado** | Las pruebas detectaron que el registro de último uso dejaba una transacción terminada en Administración. Se desasoció antes de consultar las tarifas. Se ajustó la respuesta de errores para que un JSON malformado devuelva ProblemDetails también en producción. |
| **Descartado** | Duplicar el alta o la cotización en la API, aplicar la unicidad de referencias sin distinguir el ambiente y habilitar seguimiento público para un envío de prueba. |
| **Verificación** | `CrearEnvioApiTests` comprueba 201, 200 al repetir, concurrencia sin duplicar el envío ni su evento, 400, 401, 422 y 429 con `Retry-After`. El evento real del Outbox se procesó con los consumidores de Seguimiento y dejó un aviso de prueba sin notificación al destinatario. También se corrieron regresiones de CU-10, CU-11, depósito, liquidaciones y Seguimiento. |

## **3.56 CU-82 · Consultar envíos por API**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 03/11/2026 |
| **Archivos** | Envíos: `Application/Features/ConsultarEnviosApi`, `Presentation/Features/ConsultarEnviosApi` y `Infrastructure/Persistence/EnviosApiReader.cs`. DTOs públicos en Http.Contracts y pruebas en `ConsultarEnviosApiTests`. |
| **¿Se usó IA?** | Sí, Codex. |
| **Finalidad** | Publicar listado paginado y detalle por número, con filtros, historial e información pública de bultos. |
| **Cómo se trabajó** | Codex implementó la lectura, el contrato HTTP y las pruebas. Lucas decidió dejar para la integración con Envíos el progreso real por bulto y continuar con CU-83. |
| **Aceptado sin modificaciones** | Consulta por estado, fechas de Montevideo y referencia. Aislamiento por tenant y ambiente. El detalle publica número, códigos de bulto e historial sin GUID internos, responsables, coordenadas ni notas operativas. |
| **Modificado** | El contrato adopta el vocabulario del ADR-0007 mediante una proyección del resultado disponible por envío completo. La conexión al resumen persistido y a los hechos por bulto queda pendiente de coordinar con Envíos. |
| **Descartado** | Publicar el DTO de Backoffice, inventar resultados por bulto a partir de un envío aún en operación y marcar el caso como completo con la proyección actual. |
| **Verificación** | `ConsultarEnviosApiTests` comprueba filtros, paginación, historial, privacidad y 404 entre comercios, operadores y ambientes. Queda demostrar 2 de 3 entregados con uno pendiente y luego el mismo envío con uno devuelto y `terminado: true`. |

### **Actualización del 05/11/2026 · Integración con RF 31**

Lucas retomó CU-82 tras la integración del PR #71 de Cristian. Con Codex actualizó el lector para filtrar por `Envio.Resumen`, leer el progreso persistido y proyectar el estado de cada bulto. Se conservó la lectura dentro de Envíos, el aislamiento por tenant y ambiente y el historial del envío. Se actualizó el contrato documentado en OpenAPI y se añadieron pruebas HTTP de primera recepción y del cierre mixto del mismo envío con dos de tres bultos entregados.

Pasaron 26 pruebas de integración en Release con PostgreSQL, incluidas las 7 de CU-82 y las regresiones de alta por API, documentación, recepción y sincronización por bulto. La prueba de cierre mixto aplica las transiciones en el agregado y verifica listado y detalle por HTTP. Queda la demostración conjunta del opcional 7.4.

## **3.57 CU-83 · Consultar la documentación de la API**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 03/11/2026 |
| **Archivos** | `Logistica.Api/ApiPublica/DocumentacionApi.cs` y recursos de `Documentacion/`, registro de rutas en `Program.cs`, metadata en los endpoints de Envíos, enlace en el Portal, `DocumentacionApiTests`, README y propuesta de API pública. |
| **¿Se usó IA?** | Sí, Codex. |
| **Finalidad** | Publicar documentación navegable con OpenAPI generado desde el código, esquemas, ejemplos, límites, errores y ejecución con claves de prueba. |
| **Cómo se trabajó** | Codex implementó la generación y la interfaz local, reutilizó los endpoints en un grupo de prueba y verificó el renderizado en Edge. Lucas generó una clave de prueba en el Portal y reportó un 201 al ejecutar el ejemplo desde la página. |
| **Aceptado sin modificaciones** | `/api/docs` y `/api/openapi/v1.json` se sirven en el host existente. El documento incluye las operaciones públicas v1. La consola conserva la clave en memoria y el servidor exige una clave de prueba en sus rutas. |
| **Modificado** | Se alineó `Microsoft.AspNetCore.OpenApi` de 10.0.12 a 10.0.13 durante la auditoría, de acuerdo con el parche fijado en el repositorio. Se actualizó la documentación de implementación con el alcance de cada CU. |
| **Descartado** | Una especificación duplicada a mano y una consola que sólo filtre claves de producción en el navegador. La validación en el servidor devuelve 403 incluso al invocar directamente las rutas de la consola. |
| **Verificación** | `DocumentacionApiTests` comprueba acceso anónimo, alcance del OpenAPI, esquema de la clave, ejemplo de alta, listado y detalle y rechazo de producción. La auditoría conjunta del 03/11 en Release tuvo 82 pruebas de integración y 56 de arquitectura aprobadas. La integración posterior de CU-82 se registra en la entrada 3.56. |

## **3.58 Entorno productivo en DigitalOcean**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 03/11/2026 |
| **Archivos** | `infra/terraform` (proveedores, variables, recursos, salidas, ejemplo de variables y README), job `deploy` de `.github/workflows/ci.yml`, `ConexionesObligatorias`, `ClavesProteccionDbContext` y su migración, `Program.cs` de la API y el Worker, `UsuariosIniciales`, `docker-compose.yml`, `.env.example`, `ConfiguracionProduccionTests`, `ClavesProteccionTests`, `ArmarRutaTests`, `README.md` y la propuesta de stack (secciones 6 y 7). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Preparar el hito del 5/11: Terraform para App Platform con la API y el Worker, PostgreSQL y Valkey administrados y CloudAMQP; despliegue continuo desde `main`, y configuración que impida caer por accidente en la caché en memoria. |
| **Cómo se trabajó** | La IA revisó el arranque de la API y el Worker, los Dockerfile, Docker Compose y la letra (6.12 a 6.14 y escenarios de la defensa). Propuso las decisiones abiertas con una recomendación cada una; el responsable descartó la observabilidad en producción, porque el tablero ya se mostró en el entorno local, y aceptó el resto. Los atributos de los proveedores de Terraform se verificaron en su documentación, y los precios en las páginas de DigitalOcean. |
| **Decisiones** | En Production la API y el Worker no arrancan sin PostgreSQL, Valkey ni RabbitMQ. Las claves de Data Protection van a PostgreSQL y no a Valkey, porque la caché puede perderse y las sesiones no; sin eso, cada despliegue cerraba todas las sesiones e invalidaba las invitaciones. La contraseña de demostración sale de `Demo__Contrasena` en producción (el repositorio es público) y la genera Terraform, igual que las claves del seguimiento y de los avisos. Encabezados del proxy con `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, sin código. TLS obligatorio y pool limitado en PostgreSQL. App Platform no despliega con cada push: lo pide el CI después de las pruebas. Estado de Terraform local, región Nueva York, CloudAMQP gratuito y PostgreSQL y Valkey accesibles sólo desde la aplicación. Se descartó el Aspire Dashboard remoto. |
| **Verificación** | Suite completa en Docker: 368 unitarias y 56 de arquitectura pasan. De las 543 de integración falló una, `La_excepcion_de_desarrollo_no_habilita_acceso_en_produccion`, que arranca la API en Production sin Valkey ni RabbitMQ: es la regla nueva funcionando, y la prueba ahora pasa las conexiones y la contraseña. Se volvieron a correr su clase y las nuevas, y pasan (24 de integración y 3 unitarias). `terraform validate` y `terraform fmt` pasan con Terraform 1.9. Con Docker Compose, una sesión abierta en el Backoffice siguió válida después de reiniciar la API y con un contenedor nuevo. Falta el primer `terraform apply`, que necesita las cuentas de DigitalOcean, CloudAMQP y Brevo. |

## **3.59 ADR-0004 · Conflictos por bulto y atomicidad por visita**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 03/11/2026 |
| **Archivos** | `ADR-0004-resolucion-de-conflictos-de-sincronizacion.md` (v1.4: secciones 2.11, 3.10, 4.2, 5, 6 y 7) y `ADR-0007-adaptacion-a-entrega-parcial.md` (v1.2: secciones 4.3 y 4.4). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Pasar al ADR-0004 la política de conflictos de la entrega parcial (RF 32) que el responsable decidió el 02/11, sobre la propuesta de Lucas. |
| **Cómo se trabajó** | El responsable fijó la decisión: la visita de un envío se aplica completa o se rechaza completa, y los otros envíos siguen. La IA redactó la sección, enumeró los conflictos por bulto a partir de las vías de cambio de la sección 1.2 y de la decisión cerrada, y señaló que RF 32 no exige que el conflicto de un bulto deje aplicar los demás, así que la elección debía justificarse como interpretación. |
| **Decisiones** | Se mantienen los cuatro resultados (sin "aplicada en parte") y la respuesta suma un diagnóstico por bulto, con todos los bultos evaluados. Conflictos: extraviado, en depósito, ya resuelto y fuera de la ruta. Inválidas: bulto ajeno, repetido, sin resultado, entrega parcial no admitida y evidencia incompleta. Una visita rechazada no consume intentos. El riesgo de bultos realmente entregados sin aplicar queda en el ADR-0004 (4.2) y en el ADR-0007 (4.3), con la prueba 10 y dos decisiones a revisar. El equipo aprobó la sección 2.11 el 03/11, junto con la tabla de transiciones por bulto (ADR-0004 v1.5). |
| **Verificación** | Revisión de coherencia con la decisión cerrada (sección 4) y el ADR-0007 (secciones 2.2 y 4.4). No hay código: la entrada se completa al implementar la visita por bulto. |

## **3.60 Visita por bulto en la sincronización (RF 32, CU-52)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 03/11/2026 |
| **Archivos** | Ejecución: `DiagnosticoVisita`, `ProcesarOperacionHandler`, `OperacionDispositivo.Bultos` y la migración `DiagnosticoVisita`, `IniciarRendicionHandler` y `RendicionRepository`. Envíos: `ConsultarParaVisitaAsync` y `RegistrarVisitaAsync` en el contrato y en `EnviosModuleApi`, que reemplazan a `RegistrarEntregaAsync` y `RegistrarIntentoFallidoAsync`; `EnvioConfiguration`. Http.Contracts: `VisitaRequest`, `ResultadoBultoRequest` y `BultoRechazadoResponse`. Pruebas: `DiagnosticoVisitaTests`, `VisitaPorBultoTests` y `DeclararExtravioTests`. Documentos: ADR-0004 v1.6 y CU-52 en casos de uso (v1.9). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar del lado del servidor la política de la sección 2.11 del ADR-0004 sobre el dominio de Cristian (`Envio.RegistrarVisita`), y la traducción de las operaciones viejas de la PWA que decidió el responsable. |
| **Cómo se trabajó** | La IA revisó el dominio de Cristian y vio que rechaza todos los casos con la misma excepción, sin decir qué bulto falla. A pedido del responsable, la clasificación quedó en Ejecución: compara cada bulto de la visita con su estado actual, que expone el contrato de Envíos, y el dominio queda como última barrera. Al preparar las pruebas detectó un defecto: los bultos no se cargaban con el envío, así que un extravío, una cancelación o una reprogramación no los movían, y el resumen se calculaba sobre una lista vacía, con `Terminado` en true. |
| **Decisiones** | Los bultos se incluyen siempre al cargar un envío (`AutoInclude`), en lugar de revisar cada consulta. La invalidez prevalece sobre el conflicto, y el diagnóstico lista todos los bultos y queda guardado para responder igual a un reenvío. Las operaciones viejas se traducen a una visita con todos los bultos y conservan los motivos por envío que conoce la PWA. Avisos v1: `EnvioEntregado` sólo si se entregaron todos los bultos; si no, `IntentoFallidoRegistrado`. Una visita aplicada cuenta como intento para la rendición. Un bulto devuelto mientras otros se reprograman todavía no inicia su devolución (pendiente 2). |
| **Verificación** | Suite completa en Docker: 1.031 pruebas (407 unitarias, 56 de arquitectura y 568 de integración), todas pasan, con cobertura del 95 %. `VisitaPorBultoTests` cubre la visita aplicada y las pruebas 10 a 13 del ADR-0004 (la prueba 11 con la regla publicada, que no admite entrega parcial; el caso que la admite, en `DiagnosticoVisitaTests`) y las dos traducciones. `DeclararExtravioTests` comprueba que el extravío mueve los bultos. Falta la PWA (Lucas). |

## **3.61 Cambio del entorno productivo a Azure**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 05/11/2026 |
| **Archivos** | `infra/terraform` (proveedores, variables, recursos, salidas, ejemplo de variables, README y bloqueo de proveedores), jobs `docker-images` y `deploy` de `.github/workflows/ci.yml`, `README.md` y la propuesta de stack (v1.6). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Reemplazar DigitalOcean App Platform el mismo día del hito, porque DigitalOcean retiró el crédito de estudiante y el equipo decidió no pagar el ambiente. |
| **Cómo se trabajó** | El responsable pidió alternativas gratuitas. La IA buscó las condiciones actuales y descartó Vercel y Netlify (no ejecutan .NET como proceso permanente, ni el worker, ni SignalR) y el plan gratuito de Render (sin workers, apaga la API sin tráfico y borra la base a los 30 días). Propuso una VM gratuita de Oracle con Docker Compose y Azure Container Apps con Azure for Students. El responsable prefirió una nube administrada, como la planeada, y verificó su cuenta de estudiante. |
| **Decisiones** | API, Worker y Valkey como Container Apps con una réplica siempre encendida, porque el outbox, SignalR y el consumidor no admiten apagarse por falta de tráfico. Valkey como contenedor, porque la caché administrada de Azure no tiene plan gratuito; por eso el entorno usa una red propia, que Container Apps exige para exponerlo por TCP. PostgreSQL Flexible Server B1ms y CloudAMQP gratuito. Las imágenes van a GitHub Container Registry: el CI publica la etiqueta del commit, la promueve a `latest` si pasan las pruebas y actualiza las Container Apps con una entidad de servicio (`AZURE_CREDENTIALS`); Terraform no toca la imagen después de crearlas. El código de producción ya hecho (conexiones obligatorias, claves de sesión en PostgreSQL, contraseña de demostración y encabezados del proxy) no cambió. |
| **Verificación** | `terraform validate` y `terraform fmt` pasan con Terraform 1.9 y el proveedor azurerm 4.81; actionlint no encuentra errores en el workflow. En el primer `apply`, el responsable ejecutó Terraform con su cuenta y la IA leyó los errores y los logs con su sesión de Azure. Hubo cuatro problemas: `az login` no encontraba la suscripción hasta desactivar el inicio de sesión integrado de Windows; PowerShell partía el argumento `-var-file`; el argumento vacío de Valkey llegaba como null; y São Paulo no estaba entre las regiones permitidas, así que el ambiente quedó en Chile Central. Con eso, la API respondió por HTTPS (`/health` en Healthy e ingreso en 200). Los logs mostraron que CloudAMQP rechazaba las colas por el límite de 150 del plan gratuito, y RabbitMQ pasó a una Container App sin persistencia (decisión del responsable). Las estimaciones de costo del README no están verificadas para la suscripción de estudiante. |

## **3.62 Tablero de observabilidad en producción**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 05/11/2026 |
| **Archivos** | `BuildingBlocks/.../Observabilidad` (`VentanaLatencias`, `MetricasHttp`), `MetricasMensajeria`, `MetricasCache`, `MetricasAvisos` y `EntregarAviso` (Seguimiento), `Program.cs` de la API y el Worker, `VentanaLatenciasTests`, `infra/terraform` (Aspire Dashboard, variable `observabilidad` y salidas) y su README, y la propuesta de stack (v1.7). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cumplir el objetivo del hito del 05/11, "el tablero de observabilidad operativo", que el equipo había dejado sólo en el entorno local y al que le faltaban métricas de la letra (sección 6.12). |
| **Cómo se trabajó** | La IA comparó la lista de la letra con las métricas del código: existían la caché (contadores, sin la relación), las sincronizaciones pendientes y las trazas; faltaban tiempo medio, p95 y p99, tasa de errores, profundidad de la cola, avisos fallidos y la relación de aciertos. Propuso agregarlas y llevar Aspire Dashboard a Azure; el responsable lo pidió para la muestra del día. |
| **Decisiones** | Latencia media, p95, p99 y tasa de errores de los últimos cinco minutos, calculados en la API y publicados como medidores, porque Aspire Dashboard dibuja los histogramas sólo con los percentiles 50, 90 y 99; se suma además la métrica estándar de ASP.NET Core. La profundidad de cola sale del recuento que devuelve cada `BasicGet`, sin consultas extra a RabbitMQ. Avisos fallidos: intentos rechazados y avisos que agotaron los reintentos. En Azure, el panel va en una Container App con un proxy Caddy, porque Container Apps expone un solo puerto, Aspire Dashboard 9.5 no admite el panel y el receptor OTLP en el mismo puerto (probado: falla al iniciar) y el proveedor azurerm 4.81 no tiene puertos adicionales. El panel pide token y el receptor, clave; la variable `observabilidad` lo apaga para ahorrar crédito. |
| **Verificación** | El panel detrás del proxy se probó en Docker: el ingreso con token responde, OTLP sin clave da 401 y con clave lo acepta. `dotnet build`, `terraform validate` y `terraform fmt` pasan; en Docker, `VentanaLatenciasTests`, las pruebas de arquitectura y las de la caché. El despliegue en Azure lo ejecuta el responsable. |

> **Entradas de Cristian Reyes (3.63 a 3.92):** registro retrospectivo, ordenado por fecha, reconstruido con las conversaciones con el asistente, el historial de Git y la documentación de estudio, para revisión del responsable.

## **3.63 Datos iniciales e `ICurrentTenant` provisorio (hito 8/10)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 06/10 al 08/10/2026 |
| **Archivos** | `SharedKernel/Entity.cs`; `Administracion/Domain/` (Operador, Comercio, RelacionComercial, EstadoRelacion); `AdministracionDbContext.cs`, configuraciones, migración `Inicial`, `DatosIniciales.cs`, `AdministracionModule.cs`; `Api/Program.cs`; pruebas `OperadorTests`, `RelacionComercialTests`, `TenantProvisorioTests`, `DatosInicialesTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Entender ADR-0002 y la guía de la ruta de un caso de uso, y crear el módulo Administración con el operador y el comercio del inquilino provisorio. |
| **Cómo se trabajó** | Con mi permiso, la IA escribió los archivos del dominio, la persistencia y el seed. Los revisamos de a uno: me explicó cada archivo y yo pregunté hasta entenderlo. `Entity.cs` (constructores `protected`) lo escribí yo. `TenantProvisorioTests` la escribió la IA; `DatosInicialesTests` la pegué yo a partir de su propuesta. |
| **Aceptado sin modificaciones** | Las entidades y sus reglas, las configuraciones y el seed idempotente. |
| **Modificado** | Al traer `main`, `ModuleDbContext` había cambiado de firma (recibía la `UnidadDeTrabajo`): adapté `AdministracionDbContext` yo mismo. Decisiones mías: `DeleteBehavior.Restrict` en las FK de `RelacionComercial`, y dejar la prueba de `Comercio` dentro de `RelacionComercialTests` hasta consultarlo con el equipo. |
| **Descartado** | Crear las relaciones con borrado en cascada (se perderían relaciones al borrar un operador). |
| **Verificación** | `dotnet test`: 87 pruebas, todas pasan; el log muestra el seed insertando. |

## **3.64 Segundo operador en los datos iniciales**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 10/10/2026 |
| **Archivos** | `DatosIniciales.cs`, `DatosInicialesTests.cs` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cargar un segundo operador con configuración distinta (monitoreo del 15/10) sin que el interceptor rechace el seed. |
| **Cómo se trabajó** | La IA analizó el filtro y el interceptor que había hecho Ezequiel, me explicó por qué el seed funcionaba "de casualidad" con un solo operador, y propuso un contexto por operador con `InquilinoFijo` (ADR-0002 §2.8). Con mi permiso escribió los dos archivos. |
| **Aceptado sin modificaciones** | El patrón de un contexto por operador y las pruebas. |
| **Modificado** | Decisiones mías: qué hace distinto al segundo operador (zona, franjas, flota y 8 paradas contra 20) y que trabaje con el mismo comercio, para preparar CU-03. |
| **Descartado** | Cargar sólo el operador y su relación: no mostraba una configuración distinta. |
| **Verificación** | Suite completa, 179 pruebas, todas pasan. |

## **3.65 CU-16 · Cancelar un envío**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 11/10 y 12/10/2026 |
| **Archivos** | `CancelarEnvioCommand.cs`, `CancelarEnvioHandler.cs`, `IEnvioRepository.cs`, `EnvioRepository.cs`, `CancelarEnvioRequest.cs`, `CancelarEnvioEndpoint.cs`, `EnviosModule.cs`, `Envios.Contracts/Events/EnvioCancelado.cs`, `CancelarEnvioTests.cs` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Planificar los archivos por capa, entender el repositorio, el Outbox y el endpoint, y redactar el código. |
| **Cómo se trabajó** | La IA propuso cada archivo con su explicación y **yo los escribí en Visual Studio**; después la IA revisó cada uno y corrió las pruebas. Al día siguiente adapté CU-16 al Outbox nuevo de Ezequiel (`AgregarAlOutbox` y el evento en Contracts), también escribiendo yo los cambios. |
| **Aceptado sin modificaciones** | El código propuesto, las 5 pruebas de integración y la aserción del `ComercioId` del sobre del Outbox. |
| **Modificado** | Ajustes de formato al pegar. Corregí mi comprensión del flujo: el JSON llega como *Request* y no como *Command*, y la vuelta no pasa por el command. |
| **Descartado** | Probar la cancelación a mano con `curl`: no hay botón en el Portal (es de Lucas). Lo cerré con pruebas automáticas. |
| **Verificación** | 5 pruebas de integración (flujo principal, sin motivo, A1, otro comercio, inexistente). Suite 184/184 y después 212/212. CI en verde (PR #19). |

## **3.66 Identity: usuarios, perfiles, cookie e inquilino de la sesión**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 12/10 al 14/10/2026 |
| **Archivos** | Paquete Identity; `Perfil.cs`, `ClaimsDeSesion.cs` (Contracts); `Usuario.cs`, `IdentidadDbContext.cs`, migración `IdentidadInicial`, `FabricaDeClaims.cs`, `ServicioSesion.cs`, `UsuariosIniciales.cs` (Infrastructure/Identity); `IServicioSesion.cs`; páginas `Ingresar` y `Salir`; `AdministracionModule.cs`; `Api/Tenancy/TenantDeSesion.cs`, `Program.cs`, `appsettings.Development.json`; `scripts/migracion-en-docker.sh`; prueba de arquitectura y ADR-0001 v2.1 |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar el inicio de sesión con ASP.NET Core Identity y que `ICurrentTenant` salga de la cookie, sin romper las pruebas del equipo. |
| **Cómo se trabajó** | **Escribí yo**: el paquete, `Perfil`, `Usuario`, `IdentidadDbContext`, el registro en el módulo, `Program.cs`, `ClaimsDeSesion`, `FabricaDeClaims`, la cookie, `TenantDeSesion`, la configuración y las páginas `Ingresar` y `Salir`. **Escribió la IA a mi pedido**: el cambio del script de migraciones, la excepción de arquitectura y la nota del ADR, `IServicioSesion` y `ServicioSesion`, y `UsuariosIniciales` (para no escribir la contraseña de prueba en el chat). Generé las migraciones yo con el script. |
| **Aceptado sin modificaciones** | El diseño de la cookie (HttpOnly, Secure, SameSite=Strict, 401/403 en `/api`), la fábrica de claims y el mensaje genérico ante credenciales inválidas. |
| **Modificado** | Decisiones mías: Identity en Administración (guía §2.1) y no en la Api; **transición con interruptor** (sin sesión, sólo en Development, sigue `TenantProvisorio`) en vez de reemplazarlo de golpe; ante el choque entre la guía y la regla de arquitectura, una **excepción acotada a `Microsoft.AspNetCore.Identity`**. Corregí un `using` que faltaba en la propuesta de la IA. Intenté cambiar la contraseña de prueba por una más corta y la volví atrás: no cumplía la política de Identity y no cambiaba los usuarios ya creados. |
| **Descartado** | Reemplazar `TenantProvisorio` de golpe (rompía decenas de pruebas del equipo). Mover Identity a la Api (contradecía la guía). Pasar el nombre de la cookie a la marca (queda para hablar con el equipo). |
| **Verificación** | Suite 212/212. Prueba manual en el navegador: entrar como cada operador y ver datos distintos. CI en verde (PR #20). |

## **3.67 CU-03 · Seleccionar el operador de la sesión**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 14/10 y 15/10/2026 |
| **Archivos** | `IOperadoresDelComercio.cs`, `OperadoresDelComercio.cs`, `ServicioSesion.cs`, `IServicioSesion.cs`, página `/cuenta/operador`, `Ingresar.cshtml.cs`, `AdministracionModule.cs` (gancho `OnRefreshingPrincipal`); pruebas `DatosInicialesTests`, `Identidad/Sesion.cs`, `SeleccionarOperadorTests.cs`; una línea de `PlanificacionIdentityTests` (Lucas) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que el usuario de comercio elija el operador y quede como claim en la cookie, con la única consulta autorizada a ignorar el filtro (ADR-0002 §2.4). |
| **Cómo se trabajó** | Por falta de tiempo, **la IA escribió el código y las pruebas a mi pedido**, explicándome cada parte; yo hice la prueba manual en el navegador, los commits y los PR. Después, Ezequiel y Lucas revisaron y señalaron que el operador se perdía al rearmar la cookie: la IA escribió la corrección. |
| **Aceptado sin modificaciones** | La consulta especial, la verificación de la relación y el gancho que conserva el operador (que además cubre A4). |
| **Modificado** | Dos errores de la IA que detectaron las pruebas: un `OrderBy` que EF no podía traducir a SQL (lo vi como un 500 en el navegador) y el uso de `HttpContext` en Infrastructure (lo detectó la prueba de arquitectura). Al traer `main`, se unió con el ingreso por aplicación de Lucas y se actualizó una línea de su prueba. |
| **Descartado** | Guardar el operador elegido en la base: el ADR-0002 §2.6 dice que va sólo en la cookie. |
| **Verificación** | Prueba con `ValidationInterval = 0`, que **falla sin el arreglo** y pasa con él. Prueba manual: el envío creado con un operador no aparece en el otro. CI en verde (PR #23 y #25). |

## **3.68 Autorización por perfil**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 15/10/2026 |
| **Archivos** | `Politicas.cs` (Contracts), `Presentation/Autorizacion/PoliticaDePerfil.cs`, `AdministracionModule.cs`, `CrearEnvioEndpoint.cs`, `CancelarEnvioEndpoint.cs`, `RecibirBultoEndpoint.cs` (Ezequiel), `.csproj` de Envíos y Depósito, `UsuariosIniciales.cs`, `AutorizacionTests.cs` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que cada perfil haga sólo lo suyo dentro de su operador (pedido de Ezequiel en la revisión; objetivo del 15/10). |
| **Cómo se trabajó** | La IA hizo el inventario de endpoints con el actor de cada caso de uso según el catálogo, y **escribió el código y las pruebas a mi pedido**. |
| **Aceptado sin modificaciones** | Las políticas `Comercio` (con operador elegido), `OperarioDeposito` y `Administrador`, y las 6 pruebas. |
| **Modificado** | Decisión mía: que las políticas respeten el respaldo de Development, como `TenantDeSesion` y el middleware de Lucas, para no romper las pruebas del equipo. |
| **Descartado** | Exigir sesión siempre (rompía las pruebas del equipo). |
| **Verificación** | Suite 272/272. CI en verde (PR #25). |

## **3.69 Vínculo usuario ↔ repartidor**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 15/10/2026 |
| **Archivos** | `Repartidor.cs` (Lucas), `RepartidorConfiguration.cs`, migración `UsuarioDelRepartidor`, `ClaimsDeSesion.cs`, `VinculoRepartidor.cs`, `FabricaDeClaims.cs`, `UsuariosIniciales.cs`, `DatosIniciales.cs`; pruebas `RepartidorTests` y `VinculoRepartidorTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | El claim `repartidor_id` que necesitan CU-50 y CU-52 (ADR-0004 §2.3). |
| **Cómo se trabajó** | La IA buscó en el modelo de dominio de qué lado va el vínculo (`Repartidor.usuarioId`) y **escribió el código a mi pedido**. Yo generé la migración. |
| **Aceptado sin modificaciones** | Todo. |
| **Modificado** | La IA corrigió un error de compilación propio (usar la propiedad `UserManager` de la clase base). |
| **Descartado** | Ignorar el filtro para buscar al repartidor al iniciar sesión: se usa `InquilinoFijo` con el operador del usuario, sin excepciones nuevas al ADR-0002. |
| **Verificación** | 4 unitarias y 3 de integración. CI en verde (PR #26). |

## **3.70 CU-07 · Gestionar la identidad visual**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 15/10/2026 |
| **Archivos** | `IdentidadVisual.cs`, `LogoOperador.cs`, `Operador.cs`, `IOperadorRepository.cs`, `OperadorRepository.cs`, configuraciones, migración `IdentidadVisual`, `DatosIniciales.cs`, `Application/Features/GestionarIdentidadVisual/`, página `/backoffice/identidad-visual`, `MarcaDelOperadorEndpoint.cs`, layouts de Lucas e `index.html` del Portal; pruebas `IdentidadVisualTests` (unitarias e integración) y `MarcaDelOperadorTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que cada operador tenga su marca (logo, colores, contacto) y se vea distinta en el Backoffice y el Portal. |
| **Cómo se trabajó** | La IA leyó la ficha, el modelo de dominio y la propuesta de stack, propuso un plan en tres partes y **escribió el código a mi pedido**. Yo generé la migración. |
| **Aceptado sin modificaciones** | `IdentidadVisual` como value object, el logo en una tabla aislada por operador, `/cuenta/estilo.css` para que los layouts no conozcan los módulos, y el logo servido con `Content-Security-Policy: sandbox`. |
| **Modificado** | La primera migración falló: la IA no había mapeado `LogoArchivoId` (propiedad de sólo lectura). Dos pruebas fallaron porque Razor codifica las tildes; se ajustaron para decodificar el HTML. Con perfil insuficiente, las páginas pasaron a responder 403. |
| **Descartado** | Invalidar la caché (llega con el ADR de caché, 22/10) y la marca en el Seguimiento público (CU-60). |
| **Verificación** | 12 unitarias y 7 de integración. Suite 298/298. PR #28. |

## **3.71 CU-02 · Gestionar usuarios**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 16/10/2026 |
| **Archivos** | `UnidadDeTrabajo.cs` (Ezequiel: `Transaccion` pública), `IdentidadDbContext.cs`, `Usuario.cs`, `Contracts/Events/UsuarioInvitado.cs`, `Application/Features/GestionarUsuarios/`, `Identity/ServicioUsuarios.cs`, `Identity/ErroresDeIdentity.cs`, `Persistence/DatosParaUsuarios.cs`, páginas de `/backoffice/usuarios` y `/cuenta/activar`, `AdministracionModule.cs`, `_Layout.cshtml`; pruebas `UsuarioTests` y `GestionarUsuariosTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar el listado, el alta con invitación, el cambio de perfil y la desactivación (nivel C). |
| **Cómo se trabajó** | El plan estaba para escribirlo yo en 4 sesiones, pero por falta de tiempo decidí que **la IA escribiera CU-02 y yo escribiera CU-19**. La IA escribió el código y las pruebas, y un documento de estudio archivo por archivo (`repaso/09`). |
| **Aceptado sin modificaciones** | Una única clase que consulta la tabla de usuarios, filtrando por operador (compensa la falta de filtro, ADR-0002 §2.4); el enlace de 48 h y un solo uso; desactivar cambiando el security stamp; el alta y su evento en una transacción. |
| **Modificado** | Decisión mía: que Identity use la conexión y la transacción de la `UnidadDeTrabajo` para guardar el usuario y el evento juntos (implica hacer pública la transacción, en un archivo de Ezequiel). La IA corrigió un choque de nombres propio (`TextosUsuario.Perfil`). |
| **Descartado** | Guardar el evento sin transacción común (podía quedar un usuario sin invitación). Esperar a Ezequiel para resolver el outbox. |
| **Verificación** | 6 unitarias y 9 de integración (incluye alta → outbox → activación → ingreso → enlace usado, y que desactivar cierra la sesión). Suite 340/342; las 2 que fallaron por timeout pasan solas. |

## **3.72 CU-19 · Reprogramar un envío no entregado**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 16/10/2026 |
| **Archivos** | `Envio.cs` (`ElegirNuevoIntento`), `Application/Features/ReprogramarEnvio/` (command y handler), evento `EnvioReprogramado.v1`, `IEnvioRepository.RegistrarReprogramacion`, request y endpoint `POST /api/envios/{id}/reprogramacion`, política `Despachador`; pruebas `UnitTests/Envios/ReprogramarEnvioTests` e `IntegrationTests/Envios/ReprogramarEnvioTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Reprogramar un envío no entregado eligiendo la fecha y la franja del nuevo intento (T9, nivel M). |
| **Cómo se trabajó** | **Escribí yo** el método de dominio (`ElegirNuevoIntento`: A1 contra `ReintentoDesde`, reutilizando `DefinirPlanificacion`), sus 4 pruebas unitarias y el command y el handler (franjas con `ResolverFranjasAsync` y paso de la zona horaria del operador a UTC), con la propuesta y la revisión de la IA. La IA escribió el evento, el registro en el Outbox, el request, el endpoint con la política nueva y las 4 pruebas de integración, y el documento de estudio `repaso/11`. |
| **Aceptado sin modificaciones** | El evento por Outbox junto con el cambio de fecha; la política `Despachador` en el endpoint. |
| **Modificado** | *(completar)* |
| **Descartado** | *(completar)* |
| **Verificación** | 4 unitarias y 4 de integración. PR #31 junto con CU-02, CI en verde. |

## **3.73 Repaso guiado de CU-03**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 18/10/2026 |
| **Archivos** | Ninguno (repaso, sin cambios de código) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Entender para la defensa CU-03, que había escrito la IA. |
| **Cómo se trabajó** | La IA me explicó el recorrido y los tres lugares clave del código (la consulta que ignora sólo el filtro "Tenant", la verificación de la relación antes de volver a emitir la cookie y la página con A1 y A3), con preguntas de defensa. Lo probé en la web, cambiando entre los dos operadores. |
| **Aceptado sin modificaciones** | — |
| **Modificado** | Corregí mi idea de que el operador era una persona: es la empresa, el inquilino. |
| **Descartado** | — |
| **Verificación** | Prueba manual en la web: el Portal cambia de marca (verde y azul) al cambiar de operador. |

## **3.74 CU-04 · Definir zonas de cobertura y franjas horarias**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 18/10/2026 |
| **Archivos** | `Zona.cs`, `FranjaHoraria.cs`, `IZonaRepository.cs`, `Application/Features/DefinirZonas/` (`ConsultarZonasHandler`, `GestionarZonasHandler`), `ZonaRepository.cs`, `AdministracionModule.cs`, páginas de `/backoffice/zonas` (listado, nueva y detalle), `_Layout.cshtml`; en Envíos (módulo de Ezequiel): `IEnviosModuleApi.HayEnviosComprometidosAsync`, `Envio.EstadosConEntregaPendiente`, `IEnvioRepository` y `EnvioRepository`; documentos `modelo-de-dominio.md` y `casos-de-uso.md`; pruebas `UnitTests/Administracion/ZonaTests` e `IntegrationTests/Administracion/DefinirZonasTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que el administrador defina dónde reparte su operador (zonas por código postal) y en qué horarios (franjas), nivel M, con A1 a A5. |
| **Cómo se trabajó** | La IA leyó la ficha, el modelo de dominio y el código de Lucas (CU-40), y propuso un plan de 8 pasos de adentro hacia afuera. **Escribí yo** `Zona.cs` (`Modificar`, `Desactivar`, `Cubre`) y `FranjaHoraria.SeSuperponeCon`. Por tiempo, pedí que la IA escribiera el resto de a un archivo, explicándome cada uno. También me explicó el caso sin tecnicismos, con ejemplos. |
| **Aceptado sin modificaciones** | Las reglas que miran una sola zona o franja en el dominio y las que comparan con las demás (A1, A2, A3) en el handler; los DTOs de sólo lectura para las páginas; el repositorio sin filtrar por operador a mano (lo hace el filtro "Tenant"). |
| **Modificado** | Decisiones mías, confirmadas con Lucas: (1) `FranjaHoraria` queda como entidad propia y no dentro de `Zona`, distinto del modelo de dominio, que se alineó; (2) la reprogramación por lote de la ampliación del 09/10 queda pendiente; (3) la zona y sus franjas se cargan en dos pantallas. Por pedido de Lucas se agregó el bloqueo: no se reemplaza una franja con envíos comprometidos desde esa fecha. Para eso se consulta a Envíos por su contrato (aviso a Ezequiel). |
| **Descartado** | Cambiar `FranjaHoraria` a composición (rompía la planificación y CU-19). Implementar ahora la reprogramación por lote. |
| **Verificación** | 8 unitarias y 6 de integración, incluida la del bloqueo. Verifiqué que la prueba de A3 y la del bloqueo fallan si se quita la regla. Arquitectura 54/54. Prueba manual en la web. La suite completa falló por Docker colgado (la integración de WSL con Ubuntu y otros proyectos corriendo a la vez). Después de arreglarlo, las 12 clases que habían fallado pasaron: 117/117. |

## **3.75 ADR-0006 · Estrategia de caché e invalidación (borrador)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 18/10/2026 |
| **Archivos** | `docs/analisis-diseno/ADR-0006-estrategia-de-cache.md` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Borrador del ADR obligatorio de caché (letra 6.7 y 8.2) para el hito del 22/10. |
| **Cómo se trabajó** | Le pedí a la IA un borrador a partir de la letra, la propuesta de stack, el ADR-0001, el ADR-0002 y las fichas de CU-04 a CU-07 y CU-60. Las decisiones que necesitan validación del equipo quedaron marcadas como "a confirmar". |
| **Aceptado sin modificaciones** | Cache-aside sobre una pieza común; la clave con el operador la arma la pieza; dos perfiles (seguimiento con expiración corta e invalidación por evento; configuración con versiones inmutables y un puntero "vigente"). |
| **Modificado** | La v0.2 se alineó con la pieza común que implementó Ezequiel (PR #36); la v0.3, con CU-60; la v0.4 corrigió cómo se invalida cada caso (entrada 3.82); la v0.6, con la aclaración docente (entrada 3.92). |
| **Descartado** | `HybridCache` (caché local por instancia, que complica invalidar); invalidar la configuración por Outbox. |
| **Verificación** | Revisión del equipo y las pruebas de caché de cada caso (tabla de la sección 4 del ADR). |

## **3.76 CU-04 · Correcciones de la revisión de Lucas y Ezequiel**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 19/10/2026 |
| **Archivos** | `GestionarZonasHandler.cs`, `FranjaHoraria.cs` (`ProgramarCambio`), `Zona.cs`, `ZonaRepository.cs`, `Detalle.cshtml.cs`; contratos nuevos en Envíos (`EnviosSinFechaConFranjasAsync`) y Planificación (`HayRutasActivasConEnviosAsync`, una línea en dos repositorios de prueba de Lucas); `DefinirZonasTests`, `ZonaTests`; ficha de CU-04 y modelo de dominio |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Atender la revisión del PR #34: envíos sin fecha en rutas activas (Lucas), validar antes de modificar y fechas pasadas (Ezequiel). |
| **Cómo se trabajó** | Le pasé a la IA los mensajes de Lucas y Ezequiel; verificó que nadie lo hubiera arreglado ya, propuso el arreglo y lo escribió a mi pedido. |
| **Aceptado sin modificaciones** | Usar el mismo bloqueo de planificación del operador que las rutas; preguntar a Envíos y a Planificación por sus contratos; fecha futura en la zona horaria del operador. |
| **Modificado** | La IA encontró que, tras un rechazo dentro de la transacción, la página daba 500; se corrigió sumando cada lectura a la transacción en curso. |
| **Descartado** | — |
| **Verificación** | 10 pruebas de integración de CU-04 (4 nuevas) y verificación de que fallan si se quita cada arreglo. |

## **3.77 CU-05 · Configurar el cuadro tarifario**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 19/10/2026 |
| **Archivos** | Dominio `Domain/Tarifas/` (ReglaTarifa, AjusteTarifa, VersionTarifario, TablaTarifaria, ITarifarioRepository), `Modalidad`, `EstadoVersion`; configuraciones, `TarifarioRepository`, migración `TarifarioCU05`, `DatosIniciales`; `Application/Features/ConfigurarTarifario/` (TarifarioVigente con caché, handlers); `CalcularTarifaAsync` en `IAdministracionModuleApi`; páginas `/backoffice/tarifario`; `VersionTarifarioTests`, `ConfigurarTarifarioTests`; `PostgresApiFactory` (una línea) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | CU-05 nivel C: versiones del tarifario con borrador y publicación, A1 a A5, y la caché del ADR-0006 (escenario 5 de la defensa). |
| **Cómo se trabajó** | La IA armó un plan en 6 sesiones con 5 decisiones que confirmé. **Escribí yo el dominio (sesión 1)** con el código que me pasaba, archivo por archivo. Por tiempo, pedí que la IA escribiera las pruebas unitarias y las sesiones 3 a 6, generara la migración y dejara una guía de estudio y de prueba (`repaso/12`, `13` y `14`). |
| **Aceptado sin modificaciones** | Rangos con "desde" incluido y "hasta" excluido; ajustes sobre el precio base; "programada" por fechas; invalidar la caché en el handler después de guardar; un borrador por operador con índice parcial y A5 con `xmin`; caché con puntero "vigente" y versiones inmutables. |
| **Modificado** | Las pruebas de integración usaban el Valkey real de desarrollo y leían datos de otra corrida: se configuró la fábrica de pruebas sin Valkey y se limpiaron las claves. |
| **Descartado** | Un despachador de eventos en memoria para `TarifarioPublicado`. |
| **Verificación** | 12 unitarias y 4 de integración. Se verificó que, si se quita la invalidación de la caché, la prueba falla. |

## **3.78 CU-06 · Configurar las reglas operativas**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 19/10/2026 |
| **Archivos** | `Domain/Reglas/` (VersionReglas, MotivoNoEntrega, PruebaExigida, PoliticaDevolucion, PlazoModalidad, DatosReglas, IReglasRepository); configuraciones, `ReglasRepository`, migración `ReglasOperativasCU06`, `DatosIniciales`; `Application/Features/ConfigurarReglas/` (ReglasVigentes con caché y handlers); `ReglasOperativas` y dos funciones en `IAdministracionModuleApi`; páginas `/backoffice/reglas`; `VersionReglasTests`, `ConfigurarReglasTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | CU-06 nivel C: versiones de las reglas operativas con borrador y publicación, A1 a A3, caché del ADR-0006 y el contrato que Envíos necesita para CU-10, CU-17 y CU-18 (escenario 5). |
| **Cómo se trabajó** | La IA armó un plan en 6 sesiones con 3 decisiones a acordar con el equipo. Ezequiel definió el contrato (las reglas por versión del envío y las vigentes para el alta) y lo hace él en Envíos; Lucas confirma después lo del máximo de paradas. Por tiempo, pedí que la IA escribiera las 6 sesiones, generara la migración y dejara la guía de estudio y de prueba (`repaso/15`, `16` y `17`). |
| **Aceptado sin modificaciones** | El mismo ciclo de vida que el tarifario; validar al publicar mostrando todos los errores juntos; el código de motivo como identificador estable en mayúsculas; pruebas y devolución como columnas y plazos como JSON. |
| **Modificado** | La pregunta de los motivos con Id nuevo entre versiones se resolvió con la propuesta de Ezequiel: cada envío guarda su versión, así que el motivo se busca en el catálogo de esa versión. |
| **Descartado** | Usar siempre las reglas vigentes para todos los envíos (no cumple la letra 6.6). Tocar el código de Planificación antes de que Lucas confirme. |
| **Verificación** | 9 unitarias y 4 de integración. La de integración comprueba que la versión 1 conserva sus reglas después de publicar la 2, y falla si se quita la invalidación de la caché. |

## **3.79 CU-60 · Consultar el seguimiento público**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 19/10/2026 |
| **Archivos** | Seguimiento: `TokenSeguimiento`, `ClaveSeguimiento`, `SeguimientoModuleApi`, `ConsultarSeguimientoHandler`, `TextosSeguimiento`, `InvalidarSeguimiento`, `InquilinoDelToken`, `ConsultarSeguimientoEndpoint`, página `Enlace`; `ISeguimientoModuleApi`, `LimitesSeguimiento`; Envíos: `SeguimientoDeAsync` e `IdPorNumeroAsync`; Administración: `MarcaDelOperadorAsync`; `Http.Contracts/Seguimiento`; app `SeguimientoPublico` (página `/seguimiento/{token}`); `Logistica.Api/Program.cs` (límite de tasa), `Logistica.Worker/Program.cs` (caché); configuración; pruebas `TokenSeguimientoTests`, `TextosSeguimientoTests`, `SeguimientoDeEnvioTests`, `CacheSeguimientoTests`, `SeguimientoPublicoTests`; ficha de CU-60, ADR-0002 y ADR-0006 |
| **¿Se usó IA?** | Sí |
| **Finalidad** | CU-60 nivel C: seguimiento sin autenticación con token firmado, inquilino desde el token, A1 a A3, caché de 1 minuto invalidada por los eventos de Envíos y la página con la marca del operador. |
| **Cómo se trabajó** | La IA revisó qué existía, armó un plan en 6 sesiones con 5 decisiones (S1 a S5) que acepté y le mandé al equipo, y escribió las sesiones a mi pedido, con un resumen de estudio y una guía de prueba (`repaso/18` a `20`). |
| **Aceptado sin modificaciones** | Token HMAC determinístico con clave de configuración; el token se convierte en claims, como la cookie, sin tocar `TenantDeSesion`; caché con clave EnvioId; invalidación por eventos en el Worker; A1 y A2 con la misma respuesta; límite de tasa por IP. |
| **Modificado** | S1: en lugar de la tabla de lectura del requerimiento 4, un contrato síncrono a Envíos con caché, porque faltaban eventos de Envíos; la tabla queda para CU-66. Se actualizaron la ficha y el ADR-0002. Para obtener el enlace se hizo una página propia en Seguimiento, sin tocar el detalle del envío de Ezequiel. |
| **Descartado** | Data Protection de ASP.NET para el token; buscar el envío sin filtro para descubrir su operador; "cuántas entregas quedan antes" para el 22/10. |
| **Verificación** | 20 pruebas unitarias y 9 de integración. Se verificó que las de integración fallan si se quita el límite de tasa o si el filtro no fija el inquilino. |

## **3.80 Página de seguimiento del Backoffice (pedido de Lucas)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 22/10/2026 |
| **Archivos** | `IEnviosModuleApi.ListarParaSeguimientoAsync` y `EnvioParaSeguimiento` (Envios.Contracts); página `/backoffice/seguimiento` (Seguimiento, perfiles Administrador y Despachador) con "Ver enlace"; ítem en `_Layout.cshtml`; dos pruebas nuevas en `SeguimientoPublicoTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que el personal encuentre un envío y copie su enlace de seguimiento sin conocer el número de memoria, a pedido de Lucas. |
| **Cómo se trabajó** | La IA escribió la página y el contrato a mi pedido, reutilizando el listado de CU-13 con sus validaciones y el filtro "Tenant", y me dejó una guía (`repaso/23`). |
| **Aceptado sin modificaciones** | Reutilizar el listado del Backoffice en lugar de una consulta nueva; perfiles Administrador y Despachador. |
| **Modificado** | Después Lucas sacó el ítem del menú y puso el enlace en el listado de Envíos; se ajustó la prueba que buscaba el ítem. |
| **Descartado** | Mandar el enlace por correo desde esta página: es CU-62 (Ezequiel). |
| **Verificación** | `SeguimientoPublicoTests`: 7 pruebas, incluidas Depósito sin acceso (403) y aislamiento entre operadores. PR #53. |

## **3.81 CU-21 · Declarar un envío como extraviado**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 23/10/2026 |
| **Archivos** | `Envio.DeclararExtravio`; evento `EnvioExtraviado.v1`; `IEnvioRepository`/`EnvioRepository.RegistrarExtravio` (Outbox); `Application/Features/DeclararExtravio/`; página `/backoffice/envios/{numero}/extravio` y botón en el detalle; `InvalidarSeguimiento` (se suma el evento); pruebas `DeclararExtravioTests` (unitarias e integración) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | CU-21 nivel M: T16 con motivo obligatorio, la incidencia de extravío en la misma transacción y el aviso por Outbox. |
| **Cómo se trabajó** | La IA armó el plan sobre la ficha, T16 y el modelo de dominio. `Envio.DeclararExtravio` lo escribí yo; el resto (evento, repositorio, handler, página y pruebas) lo escribió la IA a mi pedido, archivo por archivo, explicando cada uno. |
| **Aceptado sin modificaciones** | El handler busca por número; la incidencia se crea con la transición; perfiles Administrador o Despachador, como dice la ficha. |
| **Modificado** | El botón quedó en el detalle del envío de Lucas, sin cambiar su estructura. |
| **Descartado** | Tratar la parada fallida acá: la cubre el cierre de ruta de Lucas (a confirmar con él en su momento). |
| **Verificación** | 6 pruebas unitarias y 6 de integración. PR #54. |

## **3.82 ADR-0006 v0.4 · Alinear la estrategia de caché con lo implementado**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 24/10/2026 |
| **Archivos** | `ADR-0006-estrategia-de-cache.md`; `InvalidarSeguimiento` (`EnvioExtraviado`); `Logistica.Worker/Program.cs` (exporta la métrica de caché) |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que el ADR dijera lo mismo que el código antes del monitoreo del 29/10. |
| **Cómo se trabajó** | Pedí revisar el ADR contra el código. La IA encontró contradicciones: el ADR decía que el seguimiento se invalidaba con eventos en memoria (en el código va por Outbox, RabbitMQ y Worker), que la configuración se invalidaba con eventos (lo hace el handler de publicar) y justificaba los 60 segundos con un dato no implementado. Escribió la v0.4 a mi pedido. |
| **Aceptado sin modificaciones** | Las correcciones, la tabla de pruebas con su clase y estado, zonas e identidad visual fuera del alcance. |
| **Modificado** | La nota para el ADR-0001 (sección 2.5) se la pasé a Lucas en lugar de aplicarla, porque ese ADR es suyo. |
| **Descartado** | Implementar "cuántas entregas quedan antes" sólo para justificar el valor de expiración. |
| **Verificación** | Revisión contra el código; pruebas de caché existentes. PR #55. |

## **3.83 CU-20 · Iniciar la devolución de un envío**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 25/10/2026 (ajuste el 28/10/2026) |
| **Archivos** | `Envio.IniciarDevolucion`; evento `DevolucionIniciada.v1`; `RegistrarDevolucion` (Outbox); `Application/Features/IniciarDevolucion/`; página `/backoffice/envios/{numero}/devolucion` y botón; endpoint `POST /api/envios/{id}/devolucion` (política Comercio); `InvalidarSeguimiento`; pruebas unitarias e integración |
| **¿Se usó IA?** | Sí |
| **Finalidad** | CU-20 nivel M: T12 y T14 desde el Backoffice y la API del comercio, con la `Devolucion` y el aviso. |
| **Cómo se trabajó** | La IA armó el plan con cinco decisiones (F1 a F5) que acepté, y escribió el código a mi pedido en cuatro bloques, con un resumen corto y una pregunta de defensa por bloque. |
| **Aceptado sin modificaciones** | F1: T10 manual es inalcanzable (NoEntregado es de paso), se explica en la defensa. F2: T14 automático queda fuera. F3: el comercio, sólo por API. F5: en el Backoffice, sólo Despachador. |
| **Modificado** | 28/10: para que CU-31 de Ezequiel pudiera entregarla, la devolución nace recibida en el depósito, salvo que el envío siga en el vehículo (PR #64). |
| **Descartado** | Tocar CU-18 para que su devolución automática publique `DevolucionIniciada` (F4): es código de Ezequiel y se le avisó. |
| **Verificación** | 8 pruebas unitarias y 7 de integración. |

## **3.84 CU-66 · Ver el tablero de operación en vivo**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 26/10/2026 |
| **Archivos** | Seguimiento: `TableroHub` (SignalR, salas por operador, backplane Valkey), `RiesgoDeEntrega`, `ActualizarPosicionTablero`, `NotificarNovedadTablero`, `ConsultarTableroHandler`, `RevisarConflictoHandler`, página `/backoffice/tablero` (Leaflet y cliente SignalR); contratos nuevos de Planificación (rutas en curso) y Envíos (envíos en riesgo); Ejecución: `MarcarRevisada` y contrato de conflictos; pruebas `TableroEnVivoTests`, `RiesgoDeEntregaTests`, `LectorHechosEnvioTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | CU-66 nivel C: mapa con las posiciones de los vehículos, envíos en riesgo y conflictos de sincronización, en tiempo real y aislado por operador. |
| **Cómo se trabajó** | La IA armó el plan en seis bloques con cinco decisiones (G1 a G5). Hicimos juntos los primeros bloques y, a mi pedido, escribió el resto. Lo probé de punta a punta con la PWA de Lucas. |
| **Aceptado sin modificaciones** | G1: el tiempo real llega desde el Worker por el backplane de SignalR sobre Valkey. G4: perfiles Despachador o Administrador. G5: Leaflet con OpenStreetMap. |
| **Modificado** | G2: la foto inicial se arma con los contratos de cada módulo en lugar de la tabla de lectura de la ficha (se anota como desvío). G3: los conflictos quedaron en Ejecución, que ya los guarda, y se marcan revisados ahí; eso además destraba la rendición. Se ajustó `TrazasMensajeriaTests` porque `RutaDespachada` pasó a tener dos consumidores. |
| **Descartado** | Correr consumidores de RabbitMQ dentro de la API, porque duplicaría trabajo con el Worker. |
| **Verificación** | `TableroEnVivoTests` con un cliente SignalR real (salas por operador y autorización), verificada por mutación; prueba manual: la posición de la PWA aparece en el mapa y otro operador no ve nada. PR #60. |

## **3.85 CU-61 · Solicitar la reprogramación desde el seguimiento público**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 27/10/2026 |
| **Archivos** | `Envio` (reprogramación por el destinatario), `ReprogramarPorDestinatarioHandler`, `IEnviosModuleApi` y `OpcionReprogramacion`; contrato de Planificación para quitar el envío de una ruta planificada; `SolicitarReprogramacionHandler` y endpoint con el token; `Seguimiento.razor`; pruebas `ReprogramarPorDestinatarioTests` y `ReprogramacionPublicaTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | CU-61 nivel M: que el destinatario elija otra fecha y franja desde el enlace público, con T18, T19 o un nuevo intento, respetando la anticipación y los intentos del operador. |
| **Cómo se trabajó** | La IA armó el plan en cuatro bloques con seis decisiones (H1 a H6), que acepté. Escribió el código a mi pedido, reutilizando lo de CU-19 y CU-60, y me dejó una guía para probarlo a mano antes del PR. |
| **Aceptado sin modificaciones** | H1: desde NoEntregado se resuelve como Reprogramado. H2: coordina Envíos, Seguimiento sólo expone la página. H3: próximos 7 días, franjas de la zona que empiecen después de la anticipación. H4: T19 con un contrato de Planificación en la misma transacción. H5: el mismo límite de tasa del seguimiento. H6: en Admitido sólo se guardan la fecha y la franja, y se publica `EnvioReprogramado`. |
| **Modificado** | *(revisar: completar si se cambió algo al probarlo)* |
| **Descartado** | Cupos por franja: no existen en el modelo. |
| **Verificación** | Pruebas unitarias de las reglas y de integración (cada estado, A1, A2 y aislamiento por token); prueba manual en incógnito. |

## **3.86 Análisis del cambio de requerimientos (RF 31 a 36)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 01/11 y 02/11/2026 |
| **Archivos** | Documento "Entrega parcial de bultos: lo acordado y lo que falta decidir" y borrador de la matriz de estados (compartidos con el equipo); base de `decisiones-entrega-parcial.md` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Entender el cambio del 30/10, separar lo que corresponde a un equipo de tres (RF 31 a 36) y llevar las decisiones abiertas al equipo. |
| **Cómo se trabajó** | La IA leyó el PDF del cambio, el Trello exportado y el informe de revisión del equipo; estimó la carga, marcó dependencias y contradicciones de fechas, y propuso un orden. Redactó a mi pedido el documento de decisiones pendientes, con una propuesta por punto. |
| **Aceptado sin modificaciones** | El contador "entregados / total"; el reparto por áreas como propuesta. |
| **Modificado** | Las decisiones las cerró el equipo (decisiones-entrega-parcial.md, versiones 1.0 a 1.2). |
| **Descartado** | Retiro en domicilio (RF 37 a 41): es para equipos de cuatro. |
| **Verificación** | Revisión con el equipo. |

## **3.87 CU-08 · Gestionar repartidores y vehículos**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 01/11 y 02/11/2026 |
| **Archivos** | `Repartidor` y `Vehiculo` (`Modificar`, `Desactivar`); `IFlotaRepository` y `FlotaRepository`; `GestionarFlotaHandler` y `ConsultarFlotaHandler`; páginas `GestionarFlota` (listado, repartidor y vehículo); contrato de Planificación para saber si está en una ruta sin terminar; pruebas `RepartidorTests`, `VehiculoTests` y `GestionarFlotaTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | CU-08 nivel M: alta, modificación, desactivación y consulta, con A1 (matrícula y documento únicos por operador), A2 (capacidades mayores que cero) y A3 (no desactivar si está en una ruta sin terminar). |
| **Cómo se trabajó** | La IA propuso empezar por el dominio, porque `Repartidor` y `Vehiculo` ya existían desde CU-40 de Lucas, y siguió de adentro hacia afuera. *(revisar: qué partes escribí yo y cuáles la IA)* |
| **Aceptado sin modificaciones** | Desactivar en lugar de borrar, para no romper las rutas viejas; A1 con índice único en el repositorio, porque la entidad no conoce a los demás vehículos; A3 preguntándole a Planificación por su contrato. |
| **Modificado** | Se agregó una línea a los repositorios de prueba de `ArmarRutaTests` y `QuitarEnvioDeRutaTests` (Lucas) por el método nuevo del contrato. |
| **Descartado** | — |
| **Verificación** | Pruebas unitarias de las reglas y `GestionarFlotaTests` de integración (incluye que otro operador no ve la flota). PR #67. |

## **3.88 CU-02 · Correo de invitación (paso 7)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 28/10 (hallazgo) y 03/11/2026 |
| **Archivos** | Seguimiento: `EnviarInvitacionHandler`, consumidor `EnviarInvitacion` (cola `seguimiento.enviar-invitacion`, con inbox), registro en `SeguimientoModule`, `MensajeCorreo` y `PlantillaCorreo` (botón opcional); `EnviarInvitacionTests` |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que el correo de invitación de CU-02 llegue de verdad: el evento `UsuarioInvitado.v1` quedaba en el Outbox y nadie lo consumía. |
| **Cómo se trabajó** | Al probar con dos repartidores, la IA encontró que no había consumidor del evento. Propuso cuatro archivos siguiendo el patrón de CU-62 de Ezequiel; lo hablé con el equipo y le pedí que lo escribiera. |
| **Aceptado sin modificaciones** | Consumidor en el Worker con inbox; enlace relativo en el evento, que el Worker completa. |
| **Modificado** | — |
| **Descartado** | Sacar el enlace de activación del Outbox a mano (fue sólo el atajo para probar mientras tanto). |
| **Verificación** | 3 pruebas de integración, una de punta a punta: Backoffice, RabbitMQ, Worker y Mailpit. Entró con el PR #71. |

## **3.89 RF 31 · Estados por bulto y resumen derivado del envío**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 03/11/2026 |
| **Archivos** | `tabla-de-transiciones-de-estados.md` (sección de transiciones por bulto y resumen); Envíos: `Bulto.Estado` e `IntentosFallidos`, `ResumenEnvio`, `Envio.Resumen`, `Terminado`, `BultosEntregados` y `RecalcularResumen`, puente en `Transicionar`, `Envio.RecibirBulto` e `IEnviosModuleApi.RecibirBultoAsync`, guarda de T17 por bulto, `Envio.RegistrarVisita` con `ResultadoBulto`, `IntentoEntrega.BultoIds`, consulta con resumen y estado por bulto; migraciones `EntregaParcialBultos` y `BultosDeCadaIntento`; pruebas unitarias y de integración |
| **¿Se usó IA?** | Sí |
| **Finalidad** | RF 31: el estado vive en cada bulto y el envío guarda un resumen derivado (Creado, En operación, Entregado parcialmente, Entregado), con las transiciones declarativas y verificables. |
| **Cómo se trabajó** | Primero la IA escribió, a mi pedido, la tabla por bulto en el documento de transiciones, y el equipo la aprobó. Después escribió el dominio, la persistencia y las pruebas, explicando cada paso. |
| **Aceptado sin modificaciones** | Reutilizar los estados del envío para el bulto; resumen persistido y recalculado al final de cada operación; un puente que mueve los bultos que acompañan al envío mientras los demás módulos siguen decidiendo por el envío entero; la migración preserva las recepciones parciales ya escaneadas. |
| **Modificado** | El ejemplo "cancelados después de recibir" lo propuse como inalcanzable (T17 exige que no se haya recibido ninguno) y el equipo lo aprobó; la migración se regeneró después de integrar `AmbienteEnviosApi` de Lucas. |
| **Descartado** | Agregar sólo un estado "Entregado parcialmente" al envío; convertir el bulto en un agregado propio (ADR-0007). |
| **Verificación** | 391 unitarias, 56 de arquitectura y 560 de integración tras integrar `main`. PR #71. |

## **3.90 RF 33 · Reglas operativas de la entrega parcial y recargo de la nueva visita**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 05/11 al 07/11/2026 |
| **Archivos** | Administración: `ReglasEntregaParcial`, `PoliticaPendientes` (Contracts), `VersionReglas`, `DatosReglas`, configuración y migración `EntregaParcialReglas`, `ReglasOperativas`, `ReglasVigentes`, páginas de `ConfigurarReglas`, `DatosIniciales`. Envíos: `ReglasEntrega` (`DebeDevolver`), `Envio` (`RegistrarVisita` y recargo), `RecargoNuevaVisita`, `RecargoRegistrado`, `IEnvioRepository`, `EnvioRepository`, configuración y migración `RecargoNuevaVisita`, `ReglasDeEntregaDeAdministracion`, `IEnviosHojaRuta`, `IEnviosModuleApi.ListarRecargosAsync` y `RecargoLiquidable`, detalle del envío en el Backoffice. Pruebas: `VersionReglasTests`, `VisitaPorBultoTests`, `EntregaEIntentosTests`, `EntregaParcialReglasTests`, `RecargoNuevaVisitaTests`. `casos-de-uso.md` (CU-06, v2.0 y v2.2) y `modelo-de-dominio.md`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | RF 33 del cambio de requerimientos: que cada operador configure y versione si admite la entrega parcial, qué pasa con los bultos pendientes y el recargo de la nueva visita, y que ese recargo se registre al ocurrir. |
| **Cómo se trabajó** | La IA escribió el código a mi pedido, por capas, explicando cada paso. El 07/11, al revisar la tarjeta de Trello contra el código, detectó que faltaba registrar el recargo; presentó las decisiones R1 a R6, las aprobé y le pedí que lo implementara completo. |
| **Aceptado sin modificaciones** | Las tres reglas como value object dentro de `VersionReglas` (versionado, letra 6.6); las versiones ya publicadas quedan sin entrega parcial; una sola regla `DebeDevolver` para el intento del envío entero y la visita por bulto; el recargo como entidad aparte dentro del agregado `Envio`, uno por visita adicional (aunque falle, nunca la primera ni por bulto), con el monto copiado. |
| **Modificado** | El seed lo elegí yo (opción A): nadie admite la parcial y Envíos del Este cobra $150. |
| **Descartado** | Calcular el recargo al liquidar con las reglas del día (la sección 5 de las decisiones pide cobrar el monto registrado). Integrar el recargo en la liquidación: es de Ezequiel (CU-32) y se le avisó, junto con la frase "sin recargos por reintentos" de la ficha de CU-32, anterior al cambio. |
| **Verificación** | Suite completa (446 unitarias, 56 de arquitectura e integración) y prueba manual en el Backoffice de los valores por defecto y las validaciones. La prueba de integración del recargo encontró una consulta que EF Core no podía traducir (`OrderBy` después de armar el resultado), y se corrigió. PR #80 y #81. |

## **3.91 RF 35 · Avance de la entrega por bulto en el seguimiento público**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 04/11 al 07/11/2026 |
| **Archivos** | Listado del Backoffice: columna "Entrega" (`ResumenEnvioTexto`, PR #75, con la actualización de paquetes a 10.0.14 por el aviso NU1903). Envíos: `SeguimientoEnvio` y `BultoSeguimiento` (Contracts), `SeguimientoDeAsync`. Seguimiento: `TextosSeguimiento` (`Titulo`, `Avance`, `NombreBulto`), `ConsultarSeguimientoHandler`. `Http.Contracts/Seguimiento/SeguimientoPublicoResponse`. App `SeguimientoPublico`: `Seguimiento.razor`. Pruebas: `TextosSeguimientoTests` y `SeguimientoPublicoTests`. `casos-de-uso.md` (CU-60, v2.1). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Que la página pública muestre el avance ("2 de 3 bultos entregados") y el estado de cada bulto sin exponer identificadores internos, y que con entrega parcial el título no diga "Entrega reprogramada". |
| **Cómo se trabajó** | La IA leyó la letra y la tarjeta y armó un plan con 4 decisiones. Escribió el lado del servidor; después cambié la forma de trabajo: me pasó cada bloque con su explicación y yo pegué la página y las pruebas. Creó un envío de prueba en el Portal y simuló la visita parcial con SQL en mi base local para mostrarlo (la PWA todavía no permite elegir bultos). |
| **Aceptado sin modificaciones** | Título con el resumen de RF 31 cuando hay entrega parcial; avance sobre el total original; sin avance con un solo bulto; el historial queda como está. |
| **Modificado** | El nombre de cada bulto: la IA propuso "Bulto N"; lo consulté con Lucas y Ezequiel y quedó "BTO 1, 2, 3", sólo en la página pública (el código de etiqueta sigue en depósito, PWA y Backoffice). Achiqué la documentación de CU-60 y de CU-06 al estilo de mis compañeros. |
| **Descartado** | Mostrar el código del bulto en la página pública. |
| **Verificación** | Pruebas de Seguimiento: 149 unitarias y 91 de integración. La de integración busca en el JSON que no aparezca ni el Id ni el código de ningún bulto. Prueba manual con ENV-000008 (0 de 3 y después 2 de 3). PR #81, con el CI en verde. |

## **3.92 Caché · Frescura del tarifario y las reglas, y ADR-0006 v0.6**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes (con Ezequiel Marcenal) |
| **Fecha** | 08/11/2026 |
| **Archivos** | `TarifarioVigente` y `ReglasVigentes` (puntero de 10 a 1 minuto); prueba nueva `VersionProgramadaTests`; `ADR-0006-estrategia-de-cache.md` (v0.6). |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cerrar mi parte de la aclaración docente de caché: acotar la carrera del puntero viejo y la invalidación fallida, probar con reloj controlado el inicio de una versión programada y actualizar el ADR. |
| **Cómo se trabajó** | La IA comparó contra el código el análisis que hizo la IA de Ezequiel, confirmó cada dato y propuso pasar la demo de las dos APIs a prioridad. Pegué yo el cambio de expiración. La prueba con reloj la escribió la IA para compilarla y correrla antes de dármela, y escribió el borrador del ADR a mi pedido. Me dejó una guía para probarlo en la llamada (`repaso/32`). |
| **Aceptado sin modificaciones** | Puntero de 1 minuto (acota los dos riesgos sin sobreingeniería); reloj ajustable inyectado en la API; sección 2.8 del ADR sobre el estado compartido entre instancias. |
| **Modificado** | La prueba con reloj adelantaba un día y vencía la sesión del administrador; se cambió para cruzar el instante exacto (23:59 a 00:01). Se incorporaron al ADR los cambios de Ezequiel (prueba 4 hecha con `ValkeyCaidoTests`). |
| **Descartado** | Cerrar la carrera por completo (escribir "sólo si no existe" o una doble invalidación): `IDistributedCache` no ofrece lo primero y la aclaración pide resolver o acotar. Reintentar la invalidación: los reintentos del Worker duran más que la expiración. |
| **Verificación** | `VersionProgramadaTests` (2) pasa, y falla si se desactiva la comparación con `ValidoHasta` (prueba de mutación). Todas las de Administración pasan (90 unitarias y 37 de integración). En vivo, el puntero aparece en Valkey con TTL de 60 segundos y desaparece solo. |

## **3.93 Despliegue continuo · El job deploy con el ambiente destruido**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 09/11/2026 |
| **Archivos** | `.github/workflows/ci.yml` (job `deploy`); `infra/terraform/main.tf`, `variables.tf`, `terraform.tfvars.example` y `README.md`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Al reunir evidencia para el ADR del despliegue, la IA encontró que el job `deploy` fallaba en todos los push a `main` desde el 06/11 con «No subscriptions found». |
| **Cómo se trabajó** | La IA leyó los registros del CI y encontró la causa: la entidad de servicio tenía el rol asignado con `--scopes` sobre el grupo de recursos, y `terraform destroy` borró el grupo junto con la asignación. Le pedí que lo arreglara. |
| **Aceptado sin modificaciones** | `azurerm_role_assignment` en Terraform con la variable `ci_principal_id`, para que cada `apply` restaure el permiso. El inicio de sesión con `continue-on-error`: si falla, el job promueve las imágenes, deja un aviso y no despliega, que es lo que ya prometía el README. |
| **Descartado** | `allow-no-subscriptions` en `azure/login`: no se pudo confirmar si la acción igual intenta seleccionar la suscripción del JSON. Dar el rol sobre toda la suscripción: le da al CI más permiso del necesario. |
| **Verificación** | `terraform fmt -check` y `terraform validate` pasan; `actionlint` no marca errores nuevos en el workflow. Falta verlo en GitHub Actions (el próximo push a `main` tiene que quedar en verde con el aviso) y el `apply` con `ci_principal_id`. |

## **3.94 ADR-0008 · Despliegue e infraestructura como código**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 09/11/2026 |
| **Archivos** | `ADR-0008-despliegue-e-infraestructura.md` (v0.1); propuesta de stack (v1.8, secciones 6 y 8); README de `infra/terraform`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Completar los ocho registros que pide el documento de cambio de requerimientos con la decisión de despliegue, que ya estaba tomada y aplicada desde el 05/11. |
| **Cómo se trabajó** | La IA comprobó primero que hubiera material suficiente: Terraform, el README, el CI, las entradas 3.58, 3.61 y 3.62 y la letra (6.14, 6.15 y 8.2). Me preguntó por el costo real, el despliegue continuo y el estado del ambiente, y revisó las ejecuciones del CI (así apareció el problema de la entrada 3.93). Después escribió el borrador a mi pedido. |
| **Aceptado sin modificaciones** | Un solo registro para plataforma, Terraform y entrega continua, escrito después de aplicar la decisión y aclarándolo. Como alternativas, sólo las que se evaluaron de verdad (DigitalOcean, Vercel, Netlify, Render, Oracle, CloudAMQP, la caché administrada, São Paulo, Aspire sólo local y el permiso asignado a mano). |
| **Modificado** | El borrador decía que escalar la API necesitaba un backplane de SignalR y revisar el Outbox con varias instancias. Al verificarlo en el código, el backplane sobre Valkey ya existe (CU-66) y el publicador usa `FOR UPDATE SKIP LOCKED`; lo que realmente falta son las migraciones al arrancar. |
| **Verificación** | Cada dato del ADR se contrastó con `main.tf`, `ci.yml`, `PublicadorOutbox`, `Program.cs` de la API y las ejecuciones de GitHub Actions del 05/11 y el 09/11. Queda abierta la evidencia de costo, que se toma en la próxima puesta en marcha. |

## **3.95 Etiquetas de envío y reimpresión en depósito (CU-15)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 07/11/2026 |
| **¿Se usó IA?** | Sí, Codex. |
| **Finalidad** | Analizar e implementar CU-15 según documentación y ADR, incorporando la reimpresión desde el Backoffice cuando un envío llega sin etiquetas. |
| **Cómo se trabajó** | Lucas aportó el contexto de Cristian sobre RF 33/35 y pidió revisar qué existía, qué faltaba y cómo resolverlo. Después autorizó documentar e implementar la ampliación para depósito. Codex revisó la ficha, ADR y código, implementó el generador, endpoints, accesos del Portal/Backoffice, contrato de logo y pruebas. |
| **Decisiones** | Slice en Envíos con puertos de Application y adaptadores de Infrastructure; QuestPDF y ZXing.Net, Code 128 vectorial, una página de 100 × 150 mm por bulto, nombre del operador si falta logo. Hasta 100 envíos y 200 etiquetas, lote completo o rechazo, códigos y total originales aun con entrega parcial. Reimpresión para operario, administrador y despachador; identificación previa de paquetes. Sin cambios de estado, cargos, eventos ni migraciones. [Alcance y decisión técnica](analisis-diseno/cu-15/README.md). |
| **Correcciones durante la verificación** | Se corrigió el ajuste horizontal del SVG para códigos largos. Las fuentes generadas de evidencias locales antiguas en TestResults rompían la compilación: se excluyeron de Compile sin borrar archivos. |
| **Verificación ejecutada** | Solución compilada sin errores ni advertencias; 453 pruebas unitarias, 56 de arquitectura y 15 de integración aprobadas. Siete pruebas del generador también pasan en Linux .NET 10. pypdf y Poppler comprobaron tamaño, cantidad, numeración y disposición; ZXingBrowser de la PWA reconoció tres muestras renderizadas en Chrome headless. El módulo de descarga conservó los bytes del PDF. |
| **Pendiente** | Aceptación física con impresión a tamaño real, cámara del teléfono y lector del depósito. No se desplegó en Azure ni se afirma que la prueba de hardware esté realizada. |

**Ampliación documental · 08/11/2026.** A pedido de Lucas, Codex preparó la [guía para la defensa de CU-15](analisis-diseno/cu-15/guia-defensa.md): recorrido desde las pantallas y HTTP hasta el handler, lectura filtrada, adaptador QuestPDF/ZXing y descarga; ubicación exacta de GeneratePdf, dependencias, decisiones, límites y preguntas de exposición. Se contrastó con el código actual, sin modificar la implementación ni volver a ejecutar las pruebas históricas. La consulta posterior sobre carga incompleta se contrastó con RF 19 de la letra original, la decisión de entrega parcial (secciones 5 y 6), T4/T5 y el handler de carga; no se registró una nueva decisión funcional ni se implementó RF 32.

## **3.96 RF 32 · Visita por bulto sin conexión en la PWA**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Lucas Ottonello |
| **Fecha** | 08/11/2026 |
| **¿Se usó IA?** | Sí, Codex. |
| **Finalidad y autorización** | Lucas pidió implementar RF 32 después de aprobar alcance de visita, faltantes y reservas, aclarando que una sola foto de presencia respalda la visita mixta. La letra docente aportada contempla un bulto que no se cargó en el vehículo. |
| **Implementación** | Pantalla de visita con escaneo y motivos individuales, firma del conjunto entregado que se invalida al modificarlo, foto única, hoja schema 3 con estados/regla de parcial, borrador y operación cifrados atómicos, diagnóstico por bulto, conservación de operaciones antiguas y adaptación de rendición. Carga nueva por bulto y motivo de faltante; guardas del dominio, no cierre total ni aviso completo cuando quedan pendientes. [Detalle y límites](analisis-diseno/rf-32/README.md). |
| **Correcciones durante la verificación** | La prueba de navegador detectó una colisión entre la clave de renderizado del canvas y el código del primer bulto; se agregó un prefijo propio. Se distinguieron los escaneos de bultos bloqueados por no admitir parcial de los que efectivamente salieron, conservando la interpretación de cargas antiguas. |
| **Entorno** | La API local del usuario mantenía archivos bloqueados. Se compiló y probó con artefactos separados en TestResults/rf32-build, sin detenerla ni modificar su base. |
| **Verificación** | Compilación sin errores ni advertencias; 63 pruebas JavaScript, 455 unitarias y 56 de arquitectura aprobadas. Pruebas dirigidas de integración con PostgreSQL y sesión real; resultados finales en el README. Chrome headless con la PWA compilada, viewport móvil y API simulada verificó foto comprimida, firma obligatoria e invalidada al cambiar el conjunto, persistencia con API caída y reapertura con el mismo OperacionId. |
| **Pendientes explícitos** | RF 34: pantalla de conciliación de custodia, liberación y nueva asignación por bulto; Planificación conserva la reserva actual por envío. No se declaran terminados avisos v2 ni devolución individual. Falta aceptación con cámara física y servidor desplegado. |

**Corrección posterior reportada por Lucas (RF 32).** Al abrir entrega, `anterior.bultos` podía ser undefined porque la clave del borrador de visita coincidía con la de carga. Se separaron las visitas por envío conservando las claves históricas de carga/rendición y se agregó recuperación atómica y recifrado de visitas antiguas que ya hubieran colisionado. Se preservan la cola, evidencias y operaciones confirmadas; si no había operación de carga de la que recuperar las lecturas, se pide revisarla sin inventarlas. Pasan 67 pruebas JavaScript. Chrome headless verificó el formulario, confirmación/reapertura sin API y conservación exacta de un borrador de carga previo. No se borraron datos del navegador del usuario.

**Corrección posterior de cierre de ruta (ENV-000072).** Lucas reportó un envío que no salió por carga incompleta y conservó AsignadoARuta después del cierre. CU-55 ahora permite confirmar su ubicación completa en depósito y liberar su asignación en la misma transacción; las rendiciones cerradas tienen una recuperación explícita e idempotente con la misma comprobación. Se usan contratos entre módulos, bloqueo por operador y Outbox, sin escrituras en GET. Se protege la asignación a una ruta posterior y no se infiere custodia de la ausencia de escaneo. El alcance es el envío completo no cargado; no resuelve la conciliación individual pendiente de RF 34. Se añadieron regresiones de integración y no se cambió la base local. Procedimiento y evidencia en [RF 32](analisis-diseno/rf-32/README.md).

**Corrección de navegación de rendiciones.** Lucas señaló que Rutas pertenece a Despacho, mientras que consultar y recuperar rendiciones corresponde a Depósito. Se quitó el enlace de Rutas y se agregó la pestaña Confirmadas en el listado de Rendiciones de Depósito, con paginación y acceso al detalle. Ejecución expone el historial mediante su contrato; se conservan permisos y aislamiento por operador. Las regresiones cubren el acceso de un operario de depósito sin permisos de Rutas, recuperación desde su sección, denegación a Despacho, paginación y datos ajenos.

**Recepción con datos sugeridos del comercio.** Lucas pidió precargar los datos declarados, permitir editarlos y rechazar valores cero. La pantalla de CU-30 precarga peso y dimensiones al abrir desde el detalle o consultar/escanear el código, conservando la confirmación de diferencias y evitando reutilizar las medidas del bulto anterior. Se agregó validación positiva en el navegador, sin forzar redondeos de los decimales, y se reforzó la validación del dominio aun cuando falta el valor declarado de comparación. Se mantienen las medidas opcionales conforme a CU-30. Se agregaron regresiones para precarga sin recepción automática, persistencia, ceros/negativos en página y API y preservación de ediciones tras errores. Compilación sin errores ni advertencias; 27 pruebas de integración dirigidas y 6 unitarias de recepción aprobadas. No se modificaron datos del usuario ni se requiere migración.

**Inicio explícito de ruta y revisión de carga.** Lucas reportó un borrador de carga con revisión anterior de la hoja y pidió Comenzar ruta, cerrando carga e impidiendo entregas previas. Se conserva el avance al actualizar una carga en preparación y se preservan las operaciones ya pendientes. La PWA agrega inicio cifrado, persistente e idempotente a la cola, con protección entre pestañas; Ejecución coordina el cambio de Planificación mediante su contrato y valida estado, fecha, pertenencia y carga. Se bloquean cargas posteriores, visitas previas y dependencias de un rechazo; GPS y rendición respetan el inicio. Se mantiene la compatibilidad de cargas antiguas y rutas ya EnCurso. Se documentó en ADR-0004, CU-51 y RF 32. La verificación incluye JavaScript, PostgreSQL con sesiones reales y Chrome con PWA compilada y API simulada, sin tocar datos locales del usuario.

**Corrección de una carrera en la prueba de SignalR de CI.** Lucas aportó una ejecución completa de Release con cobertura en la que sólo fallaba por timeout `TableroEnVivoTests.Una_posicion_reportada_llega_al_tablero_de_su_operador`. Se identificó la diferencia entre completar el handshake de SignalR y terminar el alta de la conexión en el grupo del operador. El helper ahora espera el OnConnectedAsync real mediante un filtro exclusivo de pruebas y una señal por conexión; conserva los cinco segundos de límite y libera las conexiones si el inicio falla. Se agregó un caso con suscripción demorada y controlada, sin sleeps ni reenvíos del mensaje, para reproducir el orden que ocasionaba la pérdida. Las cuatro pruebas dirigidas pasan en Release con cobertura; la suite completa local con las mismas opciones de CI también pasó: 625 de integración (8 min 41 s), 475 unitarias y 56 de arquitectura, sin omisiones. La ejecución local es Windows; falta repetir el runner Linux tras subir la corrección. No se modifica la PWA, la entrega parcial ni el protocolo de producción. [Explicación](analisis-diseno/cu-53/integracion-cu-66.md).

**Integración de main del 09/11/2026.** Lucas autorizó resolver los conflictos preservando la confirmación de custodia y la distinción entre escaneo y carga, sumando la consulta de rendición y los avisos v2. Se conservaron ambas bitácoras, renumerando CU-15/RF-32 como 3.95/3.96 y actualizando referencias. Se integró ConsultarParaRendicionAsync en las lecturas de depósito y rendición, se alineó el manifiesto HTTP de la hoja con el de la rendición y se usó ReintegrarNoCargadosAsync después de las comprobaciones y la confirmación de ubicación. Se quitaron las liberaciones automáticas duplicadas. Las regresiones comprueban que una revisita no libera bultos sin confirmar y conserva lo entregado antes, y que un escaneo de un envío bloqueado no simula un retorno físico. Compilación Release sin errores ni advertencias; 143 pruebas de integración dirigidas, 489 unitarias, 56 de arquitectura y 72 de JavaScript aprobadas. Docker estaba detenido en el primer intento de integración; tras iniciarlo pasaron las pruebas. No se integra todavía la rama de Cristian ni se declara resuelto RF 34 completo.

**Integración del filtro de Cristian (PR #90).** Lucas reportó tres conflictos nuevos con main. Se conserva BultosQueAcompanan para planificación y capacidad y se amplía la selección de carga con BultosParaCarga, que mantiene los asignados pendientes después de un primer lote EnTransito. Se preservan estado por bulto, regla de parcial, RegistrarCargaPorBultosAsync y el comienzo explícito. La consulta base de hoja conserva todos los estados para que Ejecución filtre por el manifiesto de la ruta sin perder entregas actuales; se adapta la prueba de la lectura base y se mantienen las comprobaciones HTTP de revisita. Se amplía RF32PwaTests con carga en dos etapas, coherencia de manifiestos y exclusión de entregados anteriores en la revisita con recargo idempotente. No se agrega migración ni se da por completado RF 34.

La primera ejecución dirigida detectó una excepción al entregar el último reprogramado mientras otro bulto seguía EnDevolucion: se intentaba obtener un motivo fallido de una visita exitosa. Se corrigió la guarda y se reforzó EventosDeBultoTests con los estados resultantes; el circuito de devolución individual sigue pendiente. Verificación final: solución Release sin errores ni advertencias, 388 integraciones dirigidas (Envíos, Planificación, Ejecución y Depósito), 490 unitarias, 56 de arquitectura y 72 de JavaScript aprobadas. El aislamiento impidió iniciar el host de tareas WebAssembly en una recompilación; al ejecutar con permiso para los procesos de MSBuild, compiló correctamente. No se ejecutó la suite completa de CI en este merge.

**Aceptación física e integrada de RF 32, 09–10/11/2026.** Lucas pidió completar y ensayar RF 32. Codex preparó el entorno Docker LAN aislado y HTTPS para Android, datos ficticios por los flujos autenticados normales, scripts locales de consulta y documentación. Lucas ejecutó cámara, carga/inicio, captura y confirmación offline, reapertura y sincronización. Ante sus reportes se movió la cámara debajo del botón, se restringió el lector a CODE-128 y se permitió quitar lecturas erróneas del borrador con control de revisión; 75 pruebas JavaScript y comprobación de cámara simulada con etiqueta real aprobadas. Se completó una rendición demo sin volver a recibir los entregados.

En la aceptación final, Lucas mantuvo una visita offline mientras Codex provocó un extravío del envío completo desde Backoffice: rechazo por bulto, evidencia conservada y cero efectos de entrega. Otro envío se confirmó offline y sincronizó, con 2/3 visibles en Backoffice y público. Por autorización expresa, Codex inició el receptor demo existente con túnel HTTPS temporal y suscripciones v1/v2; verificó recepción y HMAC, tres avisos de bulto v2 y ausencia de falsa entrega completa v1. Reenvío del mismo OperacionId, datos y archivos reconstruidos de registros aplicados: YaAplicada, sin nuevos resultados, evidencias, eventos ni avisos; no se presenta como captura byte a byte del JSON original. Se desactivaron las suscripciones y retiraron receptor/túnel conservando historial. Se aceptó el alcance original de RF 32; la recuperación completa antes de confirmar de D9 sigue como decisión interna posterior pendiente, sin atribuirla a la letra docente. No se acreditó GPS nuevo offline ni recargo positivo. Evidencias y límites: [ensayo Android](analisis-diseno/rf-32/ensayo-android-2026-11-10.md) y [aceptación integrada](analisis-diseno/rf-32/aceptacion-integrada-2026-11-10.md).

## **3.97 CU-22 · Gestionar incidencias (nivel básico)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 10/11/2026 |
| **Archivos** | `Incidencia` (`PasarAEnProceso` y `Resolver`); `IEnvioRepository` y `EnvioRepository` (`ObtenerIncidenciaAsync`); `Application/Features/GestionarIncidencias/`; `IncidenciasListadoReader`; páginas `Nueva` e `Index` en `Presentation/Features/GestionarIncidencias/`; `IncidenciaTexto`; botón en `Detalle.cshtml`; ítem en `SeccionesBackoffice`; pruebas `IncidenciaTests` y `GestionarIncidenciasTests`; `casos-de-uso.md` (v2.6) y `modelo-de-dominio.md`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Implementar CU-22 en nivel B, aprovechando lo que ya había de CU-21: la entidad, su tabla y la sección del detalle del envío. |
| **Cómo se trabajó** | Le pedí a la IA dividir el caso en cuatro partes (reglas, handlers, pantallas y prueba con documentación) y hacerlas ella, explicándome cada una. Corrí yo las pruebas unitarias y la reconstrucción con Docker, y probé el flujo en la web con los dos operadores. |
| **Aceptado sin modificaciones** | Se puede resolver sin pasar por *En proceso*, porque las incidencias que abre el sistema (faltantes de CU-55) a veces se resuelven enseguida. El historial de la post-condición son las fechas y la resolución, sin columnas nuevas ni migración. La incidencia sigue siendo del envío, no de un bulto: el bulto se nombra en la descripción, como hacen CU-31 y CU-55 (lo consultó Ezequiel). |
| **Modificado** | La reconstrucción de la API falló: la vista del listado llamaba a `IndexModel.MostrarFecha`, que Razor no encuentra desde la vista. Se cambió por `Model.MostrarFecha`. El modelo de dominio sólo tenía `resolver()`; se agregó `pasarAEnProceso()`, que pide el paso 4 de la ficha. |
| **Descartado** | Guardar cada cambio de estado en una tabla de historial: no lo exige el nivel B y necesitaba una migración. Atar la incidencia a un bulto: ningún documento lo pide. |
| **Verificación** | `IncidenciaTests` (8) y `GestionarIncidenciasTests` (3: flujo completo con A2, filtros de A1 y aislamiento entre operadores con el perfil de depósito) pasan. Todas las de Envíos (160 unitarias y 122 de integración) y las 56 de arquitectura pasan. En la web: registrar, pasar a en proceso, el aviso al resolver sin texto, resolver y filtrar, y que Envíos del Este no vea la incidencia. |

## **3.98 Opcional 7.2 · Pruebas de extremo a extremo con Playwright**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Cristian Reyes |
| **Fecha** | 10/11/2026 |
| **Archivos** | `tests/Logistica.E2ETests` (`AplicacionE2E`, `Ingreso`, `Pasos` y tres clases con cuatro flujos); `scripts/e2e-en-docker.sh`; `Directory.Packages.props` (`Microsoft.Playwright`); job `e2e` en `.github/workflows/ci.yml`; `plan-casos-de-uso-por-hito.md` (v0.5) y `README.md`. |
| **¿Se usó IA?** | Sí |
| **Finalidad** | Cumplir el opcional de pruebas de extremo a extremo, que nadie había empezado. Sin él, el equipo queda con 7 puntos y no alcanza el mínimo de 8 (plan por hito, sección 4). |
| **Cómo se trabajó** | Le pedí a la IA dividirlo en cuatro secciones (laboratorio, flujos, CI y documentación) y le dejé hacer las dos primeras mientras yo trabajaba. Las corrió en Docker y me dejó una guía de estudio (`repaso/34`). A la noche seguimos con el CI y la documentación. |
| **Aceptado sin modificaciones** | La API completa en un puerto real con `UseKestrel` de .NET 10 y un PostgreSQL de Testcontainers, en lugar de depender de `docker compose`, así la prueba es autónoma. `localhost` en lugar de `127.0.0.1`, porque Chromium sólo acepta la cookie `Secure` sin HTTPS en `localhost`. La contraseña de demostración la fija la prueba (`Demo:Contrasena`): mira la aplicación sólo desde afuera. Cada usuario en su propio contexto de navegador, lo que permite probar el aislamiento entre operadores. El proyecto fuera de `Logistica.slnx`, en un job propio. |
| **Modificado** | La primera compilación falló porque `GetRequiredFeature` no existe para `IFeatureCollection`; se usó `Get<IServerAddressesFeature>()`. El registro de la API mostraba cada consulta SQL (unas 3200 líneas): se bajó a avisos y errores. Se agregó la traza de cada ventana, para ver qué hizo el robot si una prueba falla en el CI. |
| **Descartado** | Correr las pruebas contra `docker compose`: el CI tendría que levantar los seis servicios y depender de un `.env`. Incluir el Worker y RabbitMQ: los flujos elegidos no dependen de mensajes, y la caída de esas dependencias la cubre el opcional de resiliencia. Agregar el proyecto a `Logistica.slnx`: rompería `dotnet test` y `test-en-docker.sh` en las máquinas sin Chromium. |
| **Verificación** | `scripts/e2e-en-docker.sh`: 4 aprobadas y 0 fallidas en dos corridas seguidas, en unos 2 minutos, con las trazas guardadas. `actionlint` no marca errores en el job nuevo. Falta ver el job `e2e` en verde en GitHub Actions, en el próximo PR. |

**Integración de main del 10/11/2026.** Lucas pidió traer main y resolver los dos conflictos documentales. Codex conservó el cierre y decisiones de ADR-0007 con el aporte retrospectivo de Cristian, y unió las entradas de CU-15/RF-32 con las nuevas de CU-22 y Playwright, renumerando estas últimas a 3.97/3.98 y ajustando su historial. Se preservaron los cambios de código entrantes y las fuentes de RF 32; la verificación de integración se registra en el resultado del merge, sin atribuir a esta tarea las pruebas históricas descritas por cada responsable.

## **3.99 RF 34 · Reservas, custodia y nueva planificación por bulto**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Trabajo solicitado por Lucas; la asignación de responsable RF 34 se conserva como propuesta. |
| **Fecha** | 10/11/2026 (America/Montevideo) |
| **¿Se usó IA?** | Sí, Codex en la aplicación de escritorio. |
| **Finalidad** | Contrastar letra/cambio del 29/10 y ADR, e implementar el alcance RF 34 conforme a D1/D2/D4/D6. |
| **Cómo se trabajó** | Codex escribió modelos, contratos, coordinadores, pantallas, migraciones, pruebas y documentación. Se contrastó el resultado con escenarios de concurrencia, carga, visita, retorno, rendición y reprogramación. El diff permanece para revisión; las pruebas no se presentan como aprobación humana. |
| **Propuesta implementada** | Reserva única abierta por operador/bulto, custodia auditable, selección y capacidad individuales, transacción compartida, revisiones e idempotencia. Conciliación física de depósito y conservación de reservas tras cierre administrativo. |
| **Modificado durante la verificación** | Se corrigieron el enlace de diccionarios del formulario, instantes no UTC, preparación física de pruebas históricas y comprobación de pertenencia antes de revisar rutas ajenas. La prueba de arquitectura detectó APIs paralelas de escritura: se trasladaron sus acciones a ModuleApi y la idempotencia de depósito a su propio módulo, manteniendo intacta la regla del ADR-0001. |
| **Descartado** | Liberar por Reprogramado/cierre/ausencia de escaneo, suponer custodia desde el estado, elegir una ruta histórica ambigua, relajar la prueba arquitectónica o reescribir colas offline. |
| **Verificación** | Compilación sin advertencias/errores; 498 unitarias, 56 de arquitectura y 225 integraciones seleccionadas aprobadas, incluido API/Worker/RabbitMQ/Valkey reales. [Guía y límites RF 34](analisis-diseno/rf-34/verificacion.md). No se aplicaron migraciones a la base del usuario ni se realizó aceptación física del nuevo recorrido publicado. |

## **3.100 CU-00 · Administrador general de la plataforma**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal (toca Administración e Identity, área de Cristian, que debe revisarlo) |
| **Fecha** | 13/11/2026 |
| **Archivos** | `Perfil.cs`, `Politicas.cs`, `Usuario.cs`, `UsuariosIniciales.cs`, `ServicioSesion.cs`, `Operador.cs`, `OperadorRepository.cs`, `Application/Features/GestionarOperadores/`, `Infrastructure/Identity/ServicioPlataforma.cs`, `Presentation/Features/GestionarOperadores/` (Index, Nuevo y Detalle), `SeccionesBackoffice.cs`, `AccesoAplicacionesMiddleware.cs`; pruebas `GestionarOperadoresTests` |
| **¿Se usó IA?** | Sí, Claude Code. A pedido mío, el asistente escribió el código, las pruebas y la documentación; yo elegí el alcance y revisé. |
| **Finalidad** | Un perfil que no pertenece a ningún operador y da de alta operadores con su primer administrador, los suspende o activa y gestiona sus administradores. |
| **Aceptado sin modificaciones** | El perfil `AdministradorGeneral` sin operador ni comercio (el filtro "Tenant" no le muestra datos); el alta en un ámbito propio con `InquilinoTrabajo`, como el cierre diario, porque el outbox exige un operador en la sesión; la invitación del administrador igual que en CU-02; suspender bloquea el inicio de sesión del personal. |
| **Modificado** | Al correr las pruebas apareció un 403: `AccesoAplicacionesMiddleware` sólo dejaba entrar al Backoffice a los perfiles del operador. Se agregó el admin general, sólo para `/backoffice` y sin operador en la sesión. |
| **Descartado** | Que el admin general sólo gestione administradores de los operadores sembrados (lo elegí yo: alta completa de operadores). Escribir el outbox con un operador pasado por parámetro, porque contradice el ADR-0002. Exigir la regla A5 de CU-02 (no dejar al operador sin administradores): el admin general puede invitar otro. |
| **Verificación** | `GestionarOperadoresTests` (alta e invitación de punta a punta con el outbox del operador nuevo, slug y correo repetidos, suspender y activar, agregar y desactivar administradores, autorización). Suite completa en Docker. Límite: las sesiones abiertas de un operador suspendido siguen hasta que vencen. |

## **3.101 CU-10 y CU-40 · Fecha y franja de entrega en el alta, y su filtro en Armar ruta**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 13/11/2026 |
| **Archivos** | `CrearEnvioRequest.cs`, `CrearEnvioCommand.cs`, `CrearEnvioHandler.cs`, `CrearEnvioEndpoint.cs` (`GET /api/envios/franjas`), `NuevoEnvio.razor`, `NuevoEnvioFormulario.cs`, `SeleccionEnvios.cshtml`; pruebas `FranjaEnAltaTests` |
| **¿Se usó IA?** | Sí, Claude Code. El asistente revisó cómo estaban la franja y el filtro, y escribió el código y las pruebas a pedido mío. |
| **Finalidad** | Que el comercio elija fecha y franja de entrega en el Portal, y verificar que Armar ruta filtre por esa franja. |
| **Aceptado sin modificaciones** | Fecha y franja opcionales y juntas; el servidor valida como CU-19 (la franja rige ese día y es de la zona del código postal) y que la fecha sea desde mañana en el día del operador. El filtro de Armar ruta ya existía y funciona: el envío aparece sólo ese día y en esa franja. |
| **Modificado** | En Armar ruta, el selector mostraba sólo horarios (ambiguo entre zonas): ahora nombra la zona, y la tabla muestra la franja de cada envío. |
| **Descartado** | Elegir sólo la franja sin fecha (lo decidí yo: fecha y franja). |
| **Verificación** | `FranjaEnAltaTests` (franjas por código postal y día, guardado, disponibilidad por día y franja, validaciones). Suite completa en Docker. |

## **3.102 Portal · Motivo de los errores al crear o importar envíos**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 13/11/2026 |
| **Archivos** | `PortalComercio/MotivoDeError.cs`, `NuevoEnvio.razor`, `ImportarEnvios.razor` |
| **¿Se usó IA?** | Sí, Claude Code: diagnóstico, código y prueba en el navegador integrado. |
| **Finalidad** | Reporté que el alta "a veces falla sin decir por qué". |
| **Aceptado sin modificaciones** | El diagnóstico: el Portal sólo leía `detail`, que traen las reglas de negocio; los 401, 403 y 500 llegaban sin cuerpo, el timeout no se capturaba, y el aviso estaba arriba del formulario, fuera de la vista al confirmar. La solución: un ayudante que siempre da un motivo (por código, con la referencia `traceId` ante un 500) y el aviso junto al botón. |
| **Modificado** | La carga de franjas pasó a usar el mismo ayudante, porque `GetFromJsonAsync` convertía un 401 en "no se pudo conectar". |
| **Descartado** | Mostrar detalles internos de un 500. |
| **Verificación** | En la app con Docker: código postal sin cobertura y sesión cerrada en otra pestaña muestran su motivo. Sin prueba automática (el proyecto de pruebas no referencia el Portal). |

## **3.103 Backoffice · Perfil de la sesión, ícono de la pestaña y nombre de la marca**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 13 y 14/11/2026 |
| **Archivos** | `PerfilesBackoffice.cs`, `_Layout.cshtml`, `Index.cshtml`, `site.css`, layouts del Portal y de la PWA; pruebas `PerfilEnBackofficeTests` |
| **¿Se usó IA?** | Sí, Claude Code: código, íconos SVG dibujados por el asistente y verificación en el navegador. |
| **Finalidad** | Distinguir administración, despacho y depósito con un SVG propio, y corregir "VoluptasExpress" en headers y footers. |
| **Aceptado sin modificaciones** | Distintivo con ícono y color por perfil en la barra, con fondo blanco para leerse sobre el color de cualquier operador. |
| **Modificado** | Pedí cambiar "Entraste con el perfil…" por "Bienvenido a …". Aclaré que el SVG era para el ícono de la pestaña: se agregó como data URI con el color del perfil, conservando el distintivo. |
| **Descartado** | — |
| **Verificación** | `PerfilEnBackofficeTests` (cada perfil, su saludo y su ícono) y revisión en el navegador con cada perfil, en escritorio y celular. |

## **3.104 Entrega parcial · Tablero y lecturas por bulto (pendientes 7 y 8)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal (toca Seguimiento y la PWA, áreas que deben revisar Cristian y Lucas) |
| **Fecha** | 13/11/2026 |
| **Archivos** | `NotificarNovedadTablero.cs`, `ConsultarTableroHandler.cs`, `Tablero.cshtml`, `EnvioParaTablero.cs`, `TableroResponse.cs`, `Envio.cs` (`TieneEntregaPendiente`), `EnviosDeComercio.cs`, `HojaDeRuta.razor`; pruebas `NovedadesTableroTests` y `ProgresoPorBultoTests` |
| **¿Se usó IA?** | Sí, Claude Code: contrastó la sección 6 de la decisión y el ADR-0007 con el código, propuso el orden y escribió código, pruebas y documentación. |
| **Finalidad** | Cerrar los pendientes 7 y 8 de la decisión de entrega parcial (D4 y D5). |
| **Aceptado sin modificaciones** | El tablero se suscribe a los eventos de bulto y suma bultos entregados sobre el total por ruta; "entregado" sale del resumen y "entrega pendiente" de los bultos. Los pendientes del comercio se cuentan por `Terminado`. La hoja de la PWA muestra "N de M bultos entregados". |
| **Modificado** | La revisión mostró que CU-61 ya decidía por bultos desde RF 34: sólo se actualizó el documento. |
| **Descartado** | Reemplazar los contadores por envío del tablero por contadores de bultos con el mismo nombre (D5 lo prohíbe). |
| **Verificación** | `NovedadesTableroTests` y `ProgresoPorBultoTests` (1/3 y 2/3), más tablero, comercios, PWA y arquitectura en Docker. Pendiente: el ensayo con Worker, RabbitMQ y Valkey reales. |

## **3.105 CU-32 · Liquidación según D8 (entrega parcial)**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 13/11/2026 |
| **Archivos** | `Envio.cs` (`TerminadoEn`), `EnvioRepository.cs`, `EnvioCerrado.cs`, `EnvioLiquidable.cs`, `Liquidacion.cs`, `LiquidacionConfiguration.cs`, `LiquidacionRepository.cs`, `GenerarLiquidacionHandler.cs`, `DepositoModuleApi.cs`; migraciones `TerminadoEnvio` y `ConceptosLiquidacion`; pruebas `LiquidacionTests`, `ResumenEnvioTests` y `GenerarLiquidacionTests` |
| **¿Se usó IA?** | Sí, Claude Code: leyó D8, diseñó, escribió código, migraciones (con el script de Docker) y pruebas. |
| **Finalidad** | Cobrar la base al cerrar el envío (cierre mixto incluido), cobrar los recargos de nueva visita y usar pesos enteros. |
| **Aceptado sin modificaciones** | `TerminadoEn` al pasar Terminado a verdadero, con relleno de los ya terminados desde su último evento; una línea por concepto (`BaseEnvio` o `RecargoVisita`) con índice único; recargos en `TotalAjustes` por la fecha de la visita; redondeo al peso más cercano con 0,50 hacia arriba. |
| **Modificado** | Las filas del historial de los documentos ya estaban commiteadas: se agregaron filas nuevas en lugar de modificarlas. |
| **Descartado** | Deducir el cierre del historial sin columna nueva (los cambios de bulto no siempre generan un evento del envío). **Simplificaciones aceptadas:** redondear al liquidar y no al registrar (las tarifas ya son enteras) y no agregar la identidad de visita `(OperadorId, VisitaId)`. |
| **Verificación** | Unitarias de totales, recargos y redondeo; integración con cierre mixto (450 una vez), cancelado y extraviado sin cobro, recargo de 120,50 → 121 y regeneración sin duplicar. Suite completa en Docker: 507 unitarias, 56 de arquitectura, 680 de integración, 95 % de cobertura. |

## **3.106 Revisión de la documentación para la entrega final**

| Campo | Detalle |
| :---- | :---- |
| **Responsable** | Ezequiel Marcenal |
| **Fecha** | 14/11/2026 |
| **Archivos** | Esta bitácora, `ADR-0002`, `ADR-0007`; comentario de cierre en la tarjeta de Trello de la especificación técnica |
| **¿Se usó IA?** | Sí, Claude Code: leyó la letra (8.2, 8.4 y 9.5) y el cambio de requerimientos, revisó el estado de los 8 ADR y de los entregables, y redactó estas entradas y los ajustes. |
| **Finalidad** | Saber qué falta para la entrega del 15/11. |
| **Aceptado sin modificaciones** | El inventario: faltan el documento técnico, el registro de horas y la presentación; los ADR 0002, 0005 y 0008 esperan la aprobación del equipo. |
| **Modificado** | — |
| **Descartado** | Dar por aprobados los ADR pendientes: lo decide el equipo. |
| **Verificación** | Revisión del responsable antes de entregar. |

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
| 0.20 | 21/10/2026 | CU-53: acuerdos, captura y sincronización de posiciones, retención y contratos para CU-66; verificación local y bloqueo de pruebas con Docker documentados. | Lucas Ottonello |
| 0.21 | 21/10/2026 | Se amplía CU-53 con la explicación de clases solicitada por Lucas, el resultado de la revisión contra los ADR, las correcciones, los pendientes de CU-52 y la distinción entre pruebas aprobadas e integración bloqueada al inicializar Docker. | Lucas Ottonello |
| 0.22 | 21/10/2026 | Entrada de CU-64, entregar los avisos a los comercios; numeración ajustada al integrar main para conservar también las entradas de CU-53. | Ezequiel Marcenal |
| 0.23 | 21/10/2026 | Revisión de main 04fab20 contra los ADR e integración con lukovski; resolución del conflicto conservando CU-53/CU-64, observaciones y verificación local. | Lucas Ottonello |
| 0.24 | 22/10/2026 | Entrada de CU-62, notificar al destinatario por correo. | Ezequiel Marcenal |
| 0.25 | 23/10/2026 | Ampliaciones faltantes de este chat: cierre e historial de ruta, cierre administrativo sin devoluciones, textos de PWA, diagnóstico Blazor, configuración y explicación de claves, texto del Portal e interfaz del historial de avisos. | Lucas Ottonello |
| 0.26 | 23/10/2026 | Registro de lo faltante en el chat de CU-63: implementación de CU-65, corrección del escaneo vacío y confirmación de faltantes, reapertura de rutas actuales y futuras, simplificación de carga, estabilización de CI y preparación del receptor HTTPS temporal. Se conservan las ampliaciones previas y se distinguen verificaciones ejecutadas de pasos pendientes de prueba. | Lucas Ottonello |
| 0.27 | 26/10/2026 | Entrada del evento de la recepción en depósito (T2) para CU-64 y CU-60. | Ezequiel Marcenal |
| 0.28 | 27/10/2026 | Entrada de CU-31, confirmar la devolución al comercio. | Ezequiel Marcenal |
| 0.29 | 02/11/2026 | CU-42: implementación asistida, revisión de arquitectura y ADR, decisiones y límites, resultados de pruebas y ajuste de Editar solicitado por Lucas. | Lucas Ottonello |
| 0.30 | 02/11/2026 | Decisión cerrada de entrega parcial, ADR-0007 y referencias a la evolución de los documentos existentes. | Lucas Ottonello |
| 0.31 | 03/11/2026 | Entrada de CU-11, importar envíos desde un archivo. | Ezequiel Marcenal |
| 0.32 | 03/11/2026 | Entrada de CU-32, generar la liquidación de un comercio. | Ezequiel Marcenal |
| 0.33 | 03/11/2026 | CU-32: eliminación del borrador (A4). | Ezequiel Marcenal |
| 0.34 | 03/11/2026 | Entrada de CU-33, consultar la cuenta corriente y las liquidaciones. | Ezequiel Marcenal |
| 0.35 | 03/11/2026 | Entrada de la suite de pruebas más rápida. | Ezequiel Marcenal |
| 0.36 | 03/11/2026 | Entradas de CU-80 a CU-83, auditoría de ADR y carpetas, ajustes de autenticación y OpenAPI, y registro del pendiente de entrega parcial en CU-82. | Lucas Ottonello |
| 0.37 | 03/11/2026 | Entrada del entorno productivo en DigitalOcean (3.58). | Ezequiel Marcenal |
| 0.38 | 03/11/2026 | Entrada del ADR-0004 con los conflictos por bulto (3.59). | Ezequiel Marcenal |
| 0.39 | 05/11/2026 | Integración del resumen persistido y el progreso por bulto en CU-82 (3.56). | Lucas Ottonello |
| 0.39 | 03/11/2026 | Entrada de la visita por bulto en la sincronización (3.60). | Ezequiel Marcenal |
| 0.40 | 05/11/2026 | Entrada del cambio del entorno productivo a Azure (3.61) y corrección de la tabla de la entrada 3.59. | Ezequiel Marcenal |
| 0.41 | 05/11/2026 | Entrada del tablero de observabilidad en producción (3.62). | Ezequiel Marcenal |
| 0.42 | 07/11/2026 | CU-15: análisis, impresión individual/múltiple en el Portal, reimpresión en depósito, decisión técnica y verificación automatizada/visual (3.95). | Lucas Ottonello |
| 0.43 | 08/11/2026 | Guía de estudio de CU-15 para la defensa y registro del contraste entre carga incompleta, entrega parcial y comportamiento implementado. | Lucas Ottonello |
| 0.44 | 08/11/2026 | RF 32: visita por bulto en la PWA, foto compartida, operación offline e integración de carga y rendición; límites con RF 34 y verificación (3.96). | Lucas Ottonello |
| 0.45 | 08/11/2026 | Registro retrospectivo de las entradas de Cristian Reyes (3.63 a 3.92), del 06/10 al 08/11, y su herramienta en la sección 2. Reconstruido con las conversaciones con el asistente, Git y la documentación de estudio, para revisión del responsable. | Cristian Reyes |
| 0.46 | 09/11/2026 | Entrada del job de despliegue con el ambiente destruido (3.93). | Ezequiel Marcenal |
| 0.47 | 09/11/2026 | Entrada del ADR-0008, despliegue e infraestructura como código (3.94). | Ezequiel Marcenal |
| 0.48 | 10/11/2026 | Entrada de CU-22, gestionar incidencias (3.97). | Cristian Reyes |
| 0.49 | 10/11/2026 | Entrada de las pruebas de extremo a extremo con Playwright (3.98). | Cristian Reyes |
| 0.50 | 10/11/2026 | Integración de main: conservación de las entradas de RF 32/CU-15 y CU-22/Playwright, renumeración 3.97/3.98 y resolución de ADR-0007 manteniendo decisiones y aceptación RF 32. | Lucas Ottonello |
| 0.51 | 14/11/2026 | Entradas del 13 y 14/11: administrador general (3.100), fecha y franja en el alta (3.101), motivo de los errores del Portal (3.102), perfil e ícono del Backoffice (3.103), tablero y lecturas por bulto (3.104), liquidación según D8 (3.105) y revisión para la entrega (3.106). | Ezequiel Marcenal |
