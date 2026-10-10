# Revisión de arquitectura de CU-14

Fecha: 09/10/2026. Revisión del estado actual del workspace, incluyendo los archivos trabajados en este chat y su integración con CU-30. No se modificó código funcional durante esta revisión.

## Actualización: correcciones implementadas

Después de la revisión original se corrigieron los cuatro hallazgos, por solicitud del responsable:

- ConsultarDetalleEnvioHandler compone la consulta de Envíos con IDepositoModuleApi y devuelve Recibido y PuedeRecepcionar en cada BultoDetalleDto. Primero verifica que el envío sea visible; sólo entonces consulta recepciones.
- PuedeRecepcionar se calcula en Application con TablaTransiciones y la condición de recepción inicial de CU-30. Se excluye el reintegro T8 desde EnTransito. La vista sólo representa el resultado. El contexto provisional distingue personal del operador y comercio; el permiso real de operario continúa pendiente de Identity.
- Los parámetros de navegación se conservan en el enlace a recepción, mediante campos ocultos al registrar y en el regreso al detalle. El destino se construye con una ruta fija y el número del envío obtenido del caso de uso, sin aceptar URLs de redirección arbitrarias. Las medidas no se precargan.
- EvidenciaEndpoint invoca ConsultarEvidenciaHandler, que usa IArchivoEvidenciaReader. La precarga de recepción también usa ConsultarBultoParaRecepcionHandler en Application.

Las secciones siguientes conservan el diagnóstico original y su inventario como registro de la revisión, no como descripción de problemas todavía abiertos. No se alteraron las migraciones ni el esquema para estas correcciones.

Verificación de la corrección: 24 pruebas de integración de CU-14 y CU-30 y 54 pruebas de arquitectura aprobadas. Incluyen conservación de filtros antes y después del POST, recepción parcial/completa, precarga sin medidas, formulario sin código y exclusión de la recepción inicial desde tránsito.

Referencias: ADR-0001, secciones 2.1–2.6; ADR-0002; guía de la ruta de un caso de uso; modelo de dominio; CU-14 y CU-30; propuesta de stack, sección 3.9.

La rama ya incorpora filtros globales de inquilino. También hay cambios de CU-40 sin confirmar: esta revisión no los modifica ni certifica su alcance funcional. La implementación de DepositoModuleApi actualmente está en Application; su ubicación inicial en Infrastructure ya fue reemplazada en el workspace.

## Resultado

La estructura de módulos, namespaces, DTOs internos, persistencia y presentación es compatible con la arquitectura. Hay cuatro ajustes recomendados para alinear el recorrido completo y la navegación con lo documentado. Las pruebas de arquitectura verifican dependencias, pero no detectan automáticamente estos puntos de distribución de responsabilidades y comportamiento.

## Hallazgos

### 1. P2: la vista decide la disponibilidad de recepción

Archivo: `src/Modules/Envios/Logistica.Modules.Envios/Presentation/Features/ConsultarDetalleEnvio/Detalle.cshtml`, línea 73.

La condición `envio.Estado == "Admitido" && !Model.BultosRecibidos.Contains(bulto.Id)` es correcta para el flujo mínimo actual, pero fija en Razor una decisión de negocio. CU-14 exige calcular las acciones con la misma tabla de transiciones que usa Envio.Transicionar. Tampoco incorpora todavía el perfil de operario exigido por CU-30.

Recomendación: calcular PuedeRecepcionar en Application usando TablaTransiciones, el estado de recepción del bulto y la política de permisos cuando esté disponible. La vista debe limitarse a mostrar la acción resultante. El backend de registro debe conservar todas sus validaciones: una acción visible nunca sustituye la autorización ni protege contra un cambio concurrente de estado.

### 2. P2: el PageModel compone dos consultas del caso de uso

Archivo: `src/Modules/Envios/Logistica.Modules.Envios/Presentation/Features/ConsultarDetalleEnvio/Detalle.cshtml.cs`, líneas 56–64.

El PageModel llama al handler y después al contrato de Depósito. La dependencia hacia Contracts está permitida y no hay acceso directo a otro DbContext. Sin embargo, la composición del detalle y sus recepciones queda repartida entre Application y Presentation. Otro consumidor del handler no obtiene el mismo resultado completo.

Recomendación: ConsultarDetalleEnvioHandler debe coordinar IEnvioDetalleReader e IDepositoModuleApi, consultar recepciones sólo después de encontrar un envío visible y devolver un DTO enriquecido por bulto. Presentation debe encargarse de HTTP, navegación y formato. La precarga de recepción también puede encapsularse en una consulta de Application para mantener el mismo recorrido.

### 3. P2: se pierden los filtros al pasar por recepción

Archivos: `Detalle.cshtml.cs`, método UrlRecepcion; `src/Modules/Deposito/Logistica.Modules.Deposito/Presentation/Features/RecibirBulto/Index.cshtml.cs`, propiedad UrlDetalle.

Listado → detalle conserva página, tamaño y filtros. Detalle → recepción sólo transmite CodigoBulto, y el regreso construye una URL de detalle sin esos parámetros. Por eso, después de recibir y regresar, Volver al listado pierde el contexto original.

Recomendación: conservar los parámetros de navegación en ese recorrido. La URL de retorno debe permanecer restringida a destinos locales válidos; no introducir un redirect arbitrario. Las medidas deben seguir excluidas de la precarga.

### 4. P3: la descarga de evidencia evita el handler

Archivo: `src/Modules/Envios/Logistica.Modules.Envios/Presentation/Features/ConsultarDetalleEnvio/EvidenciaEndpoint.cs`, línea 15.

El endpoint invoca directamente IArchivoEvidenciaReader. No depende de EF ni de Infrastructure, y la consulta mantiene el aislamiento y la comprobación del vínculo con el intento. Es una simplificación respecto del recorrido endpoint → handler → puerto documentado en ADR-0001 y la guía.

Recomendación: agregar una consulta/handler de evidencia en Application y mantener en el endpoint únicamente el mapeo HTTP, 404, encabezados y Results.File. Es un ajuste de consistencia, no una fuga de datos detectada.

## Inventario revisado

Las rutas de Envíos siguientes se resuelven bajo `src/Modules/Envios/Logistica.Modules.Envios/`.

| Grupo | Archivos | Evaluación |
| --- | --- | --- |
| Consulta de detalle | Application/Features/ConsultarDetalleEnvio: ConsultarDetalleEnvioQuery, ConsultarDetalleEnvioHandler, IEnvioDetalleReader | Slice correcto. El handler debe concentrar la composición con Depósito. |
| DTOs | EnvioDetalleDto, DestinatarioDetalleDto, DireccionDetalleDto, BultoDetalleDto, EventoEnvioDetalleDto, IntentoEntregaDetalleDto y PruebaEntregaDetalleDto, IncidenciaDetalleDto, DevolucionDetalleDto, VersionTarifarioDetalleDto | Internos, específicos de la consulta y sin referencias a EF/ASP.NET. Correcta ubicación. |
| Evidencia | IArchivoEvidenciaReader y ArchivoEvidenciaDto; Infrastructure/Persistence/ArchivoEvidenciaReader; Presentation/Features/ConsultarDetalleEnvio/EvidenciaEndpoint | Puerto y adaptador separados; lectura sin tracking; verifica archivo, envío e intento visibles. Ajuste del hallazgo 4. |
| Agregado Envio | Domain/Envios/Envio, EventoEnvio, IntentoEntrega, ResultadoIntento, PruebaEntrega, Ubicacion, DatosVersionTarifario, ArchivoEvidencia | Dueño correcto. Sin dependencia de HTTP, EF ni PostgreSQL. Reglas básicas de firma, motivo, número único, coordenadas y archivos. Los flujos de registro siguen pendientes. |
| Agregados independientes | Domain/Incidencias/Incidencia; Domain/Devoluciones/Devolucion y sus enums | Pertenecen a Envíos y tienen tablas propias. No se agregaron a la colección interna de Envio. Los métodos de evolución corresponden a sus CU. Separar cada enum en su archivo sería una mejora de orden, no una infracción de dependencias. |
| Persistencia | Infrastructure/Persistence/EnvioDetalleReader, EnviosDbContext; Configurations/EnvioConfiguration, IntentoEntregaConfiguration, ArchivoEvidenciaConfiguration, IncidenciaConfiguration, DevolucionConfiguration | EF queda en Infrastructure. Proyección, AsNoTracking y AsSplitQuery. Filtro global Tenant aplicado por la base del contexto. Índices únicos para intento y devolución. |
| Migraciones | 20261008152416_DetalleEnvioAmpliado.cs, su Designer y EnviosDbContextModelSnapshot | Ubicación correcta; campos adicionales y nuevas tablas. La versión histórica de la migración no se reescribe para los cambios futuros. Las pruebas aplican las migraciones en una base nueva. |
| Detalle Razor | Presentation/Features/ConsultarDetalleEnvio/Detalle.cshtml y Detalle.cshtml.cs | Ruta absoluta y PageModel interno correctos. Formato y navegación pertenecen aquí. Hallazgos 1–3. |
| Enlace desde listado | Presentation/Features/ConsultarEnvios/Index.cshtml e Index.cshtml.cs | Conserva filtros al entrar y volver directamente del detalle. No se amplió el listado con colecciones de bultos innecesarias. |
| Formato compartido | Presentation/EstadoEnvioBadge, EstadoEnvioTexto, OrigenEventoTexto y _ViewImports.cshtml | Colores, espacios y tildes son responsabilidad de presentación. Los valores de dominio y base se conservan. |
| Composición | EnviosModule y Logistica.Modules.Envios.csproj | DI y rutas en la entrada del módulo. Envíos referencia sólo Deposito.Contracts. |
| Depósito | IDepositoModuleApi; Application/DepositoModuleApi actual; IRecepcionRepository y RecepcionRepository; DepositoModule | Contrato público y adaptación interna correctos. RecepcionRepository consulta las filas protegidas por Tenant. La implementación actual en Application cumple el ADR. |
| Integración visual CU-30 | Presentation/Features/RecibirBulto/Index.cshtml e Index.cshtml.cs de Depósito | GET sólo precarga código y no guarda; medidas por POST; código nullable evita error al abrir sin selección. El registro reutiliza el handler y su transacción. Hallazgo 3. |
| CSS | src/Logistica.Backoffice/wwwroot/css/site.css | Archivo estático de presentación. Sin referencias a módulos o reglas de negocio. |
| Datos demo | scripts/datos-demo-cu14.sql | Script separado de la aplicación, para desarrollo; transacción y omisión de duplicados. No representa un flujo de negocio ni debe usarse para validar los CU de registro. Copiar el archivo UTF-8 al contenedor evita los problemas de codificación del pipe de PowerShell. |
| Pruebas | ConsultarDetalleEnvioTests, DetalleEnvioPaginaTests, DetalleEnvioAmpliadoTests, RecepcionDesdeDetalleTests, DatosDetalleEnvioTests; regresión RecibirBultoTests | Cubren consulta, aislamiento, evidencia, persistencia, navegación directa, precarga, registro y validaciones. Falta cobertura para conservar filtros a través de recepción y para la futura política de acciones. |

## Orden funcional y límites del alcance

El orden actual es listado → detalle → recepción por bulto → registro por POST → vuelta al detalle. Es coherente con CU-14 de sólo lectura y CU-30 encargado del registro. El detalle no registra recepciones y no precarga medidas declaradas como medidas verificadas. En la implementación mínima actual, recibir el último bulto pasa el envío a EnDeposito dentro de la misma transacción.

La persistencia de intentos, evidencia, incidencias y devolución es preparación autorizada en este chat. No equivale a implementar CU-17, CU-18, CU-20, CU-22 ni CU-31. Esos CU deberán completar las reglas, transiciones y operaciones de escritura. El catálogo de motivos y la versión tarifaria real también necesitan su integración.

El contexto de usuario sigue siendo TenantProvisorio. Los filtros globales protegen el contexto que se les proporciona, pero no sustituyen Identity, la autorización por perfiles ni la resolución de un tenant autenticado. CU-14 completo también contempla acceso desde el Portal, aún no implementado por esta pantalla de Backoffice.

Orden recomendado de ajuste: (1) mover composición al handler y enriquecer DTOs; (2) centralizar acciones con TablaTransiciones y permisos disponibles; (3) mantener navegación y filtros a través de CU-30; (4) uniformar la consulta de evidencia con handler; (5) integrar permisos y registros cuando estén disponibles.

## Verificación ejecutada

- 54 pruebas de arquitectura: aprobadas.
- 22 pruebas de integración seleccionadas de CU-14 y CU-30: aprobadas, sobre PostgreSQL de prueba.
- 7 pruebas unitarias de DatosDetalleEnvioTests: aprobadas.

Estos resultados corresponden al workspace revisado. No certifican que todo CU-14 esté completo ni reemplazan los hallazgos manuales anteriores. No se aplicaron migraciones a la base local durante esta revisión.
