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

# **4\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 09/10/2026 | Versión inicial: criterios y entradas del aislamiento por inquilino (hito del 15/10). | Ezequiel Marcenal |
