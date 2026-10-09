# Especificación técnica · CU-40

Fecha: 09/10/2026. Estado: especificación objetivo; la base de CU-40 está implementada para desarrollo. El [avance de implementación](avance-cu-40.md) distingue lo disponible de las integraciones pendientes.

Completa las [decisiones acordadas](diseno-cu-40.md), el [modelo de dominio](../modelo-de-dominio.md) y [CU-40](../casos-de-uso.md). Las elecciones técnicas de este documento concretan el comportamiento aprobado y los ADR-0001, ADR-0002 y ADR-0003.

## 1. Alcance y dependencias

CU-40 implementa crear una ruta, consultar candidatos, prevalidar, agregar envíos, modificar fecha/repartidor/vehículo y consultar ruta y evidencia. No implementa ordenar/optimizar, despachar, ejecutar ni rendir: corresponden a CU-42, CU-43, CU-51 y CU-55. CU-41 conserva su operación propia de quitar envíos.

Se anticipan en Administración entidades, consultas, migraciones y datos iniciales de vehículos, repartidores, zonas/franjas y reglas; en Envíos los datos de planificación y operaciones T3/T13; en Depósito la conservación y consulta de medidas recibidas. Las pantallas de gestión de Administración quedan en sus CU.

La programación de cambios de franjas, reprogramación masiva, avisos y T19 son una ampliación de CU-04. Su contrato se define aquí para que CU-40 pueda consumir las vigencias. Su flujo completo no se presenta como una acción de la pantalla de rutas.

CU-40 completo exige aislamiento, sesión autenticada con perfil Despachador y escritura durable de EnvioAsignadoARuta en Outbox. Un tenant provisorio o un adaptador sin persistencia sólo habilita desarrollo, no satisface esos criterios. El publicador y la entrega de avisos siguen el trabajo transversal del ADR-0003; la integración con el publicador debe probarse antes de declarar disponible el flujo completo de avisos de CU-04.

### Integración con el trabajo de aislamiento de Ezequiel

La guía `guia-aislamiento-por-inquilino.md`, compartida el 09/10/2026, describe una implementación transversal de filtro global Tenant, TenantSaveChangesInterceptor e InquilinoFijo, con pruebas en AislamientoTests. Es la base a reutilizar para CU-40, no un trabajo a duplicar en Planificación.

Al revisar este checkout, ModuleDbContext aún no recibe ICurrentTenant ni aplica el filtro; tampoco están los archivos del interceptor, InquilinoFijo o AislamientoTests. La guía es evidencia del trabajo paralelo, no de su integración en esta rama. Lucas autorizó avanzar con la base de CU-40 mientras se termina ese trabajo. Las consultas nuevas llevan guardas explícitas temporales por operador, identificadas en el código, siguiendo el comportamiento del checkout actual. Deben retirarse al integrar el filtro global: no sustituyen el interceptor ni permiten certificar el aislamiento transversal. No se escribe otra versión de esa infraestructura.

La adición prevista para participar en la transacción antes de leer/bloquear se realiza sobre la versión integrada de ModuleDbContext, conservando su constructor, OnConfiguring, registro del interceptor y filtros. Un override del módulo llama a base.OnModelCreating. Identity sigue pendiente según la propia guía: filtro/interceptor no equivalen a autenticación ni autorización.

## 2. Dominio y límites de módulos

Todo tipo de implementación es internal. Domain no depende de EF, ASP.NET ni entidades internas de otros módulos. Application traduce DTO de Contracts a objetos de entrada del dominio.

| Tipo | Definición técnica |
| :--- | :--- |
| Ruta | Entity e IOperadorOwned. Id, OperadorId, RepartidorId, VehiculoId, Fecha, Estado, CreadaEn, DespachadaEn opcional, Revision: long y ReservaActiva: bool. Colección privada de Parada; métodos que protegen Planificada y duplicados. |
| Parada | Entity e IOperadorOwned. Id, OperadorId copiado de Ruta, RutaId, EnvioId, Orden, Estado, LlegadaEn opcional. Sólo Ruta crea o elimina paradas. |
| ValidacionRuta | Entity e IOperadorOwned. Datos ya acordados en el modelo, OperadorId copiado de Ruta y RevisionRuta evaluada. Registro histórico inmutable. |
| DatosBultoValidado | Objeto valor con EnvioId, BultoId, PesoKg, LargoCm, AnchoCm, AltoCm y volumen calculado. Se persiste como detalle de ValidacionRuta. |
| BultoValidacionRuta | Entidad de persistencia histórica e IOperadorOwned, con Id, OperadorId, ValidacionRutaId y los datos del objeto valor DatosBultoValidado. Permite que la tabla hija participe del filtro/interceptor sin intentar aplicar un filtro independiente a un complex type. No referencia Parada. |
| PlanificadorDeRuta | Servicio puro de dominio: recibe fecha, paradas existentes, candidatos, vehículo y reglas como datos propios de Planificación. Devuelve restricciones identificadas y carga calculada. No consulta base ni módulos. |

Revision nace en 1 y aumenta con cada modificación efectiva del agregado, incluidas agregar/quitar paradas y cambios de estado. Se configura como token de concurrencia. Así cambiar sólo una parada también actualiza la fila de Ruta. Envíos conserva su token xmin existente.

ReservaActiva se deriva y mantiene desde Ruta: Planificada con paradas, Despachada o EnCurso implica reserva; Planificada vacía y Finalizada no. No es un campo editable por el cliente. Orden inicial es el de selección, numerado desde 1; agregar continúa el orden actual. CU-42 realiza el ordenamiento posterior.

Para una modificación o ampliación, los envíos que ya pertenecen a la ruta se reconocen como asignaciones propias: no se exige que vuelvan a EnDeposito/Reprogramado ni se reaplica T3/T13. Las validaciones de fecha/franja y carga sí abarcan toda la ruta.

## 3. Contratos síncronos

Los nombres siguientes son las firmas objetivo. Todos reciben CancellationToken. OperadorId y responsable proceden de servicios de sesión; ninguna petición HTTP los decide. DTO públicos viven en Contracts del módulo dueño; se añaden capacidades sin retirar las existentes.

### Administración · IAdministracionModuleApi

| Operación | Firma objetivo y resultado |
| :--- | :--- |
| Contexto del operador | ObtenerContextoPlanificacionAsync(ct) → ContextoPlanificacion: OperadorId, Activo, ZonaHoraria. |
| Exclusión para confirmar | BloquearPlanificacionAsync(ct) → Task. Requiere IUnidadDeTrabajo activa; bloquea la fila del operador actual dentro de Administración. |
| Recursos | ConsultarRecursosPlanificacionAsync(ct) → repartidores y vehículos activos. ObtenerRepartidorAsync(id, ct) y ObtenerVehiculoAsync(id, ct) → resumen o null si inexistente/no accesible. |
| Reglas | ObtenerReglasPlanificacionVigentesAsync(instanteUtc, ct) → VersionReglasId y MaxParadasPorRuta; ausencia de versión impide confirmar. |
| Zonas | ConsultarZonasPlanificacionAsync(ct) → identificador, código y nombre para filtros. |
| Franjas por fecha | ResolverFranjasParaFechaAsync(franjaIds, fecha, ct) → por cada referencia: franja aplicable, zona, horas, días y vigencias, o restricción de incompatibilidad. Sigue reemplazos hasta la versión aplicable a esa fecha. |

VehiculoPlanificacion contiene Id, matrícula, activo, capacidades kg/m³ y dimensiones de caja cm. RepartidorPlanificacion contiene Id, nombre y activo. Una franja sin día compatible no se resuelve forzando otra fecha.

### Envíos · ampliación de IEnviosModuleApi

| Operación | Firma objetivo y resultado |
| :--- | :--- |
| Candidatos | ConsultarCandidatosRutaAsync(ConsultaCandidatosRuta consulta, ct) → página de EnvioPlanificable. Consulta: Fecha, ZonaId?, FranjaId?, Texto?, Pagina y TamanoPagina. |
| Relectura por lote | ObtenerParaPlanificacionAsync(envioIds, ct) → datos actuales por envío autorizado. El solicitante compara los IDs devueltos con los requeridos; no se omiten faltantes silenciosamente. |
| Asignación | AsignarARutaAsync(AsignacionEnviosRuta asignacion, ct) → Task. Incluye RutaId, FechaRuta y pares EnvioId/FranjaAplicadaId?. Relee entidades, aplica T3/T13, guarda historial y EnvioAsignadoARuta en Outbox. Requiere transacción del caso de uso. |

EnvioPlanificable contiene Id, Número, estado, ZonaId, FranjaHorariaId opcional, FechaEntregaProgramada opcional, dirección/código postal y todos sus bultos declarados. No expone Domain de Envíos. La compatibilidad de la franja por fecha se resuelve con Administración. Un envío sin franja no obtiene una por defecto. La asignación guarda la referencia de franja resuelta cuando existe, sin inventar una FechaEntregaProgramada.

Envíos filtra sus estados y fecha. Planificación excluye las paradas activas y aplica la resolución de franjas antes de paginar el resultado visible; los filtros de franja usan la versión aplicable al día consultado. La consulta entre módulos puede hacerse en lotes y no debe consultar cada bulto por separado.

### Depósito · ampliación de IDepositoModuleApi

ObtenerMedidasRecepcionAsync(bultoIds, ct) → lista de MedidasBultoRecibido con BultoId, PesoKg?, LargoCm?, AnchoCm?, AltoCm? y RecibidoEn. El resultado sólo incluye bultos del operador autorizado. Se conservan valores medidos en RecepcionDeposito como columnas opcionales, incluso cuando la comparación fue conforme. La implementación de planificación elige cada valor medido disponible y usa el declarado como respaldo; volumen se calcula después de resolver las tres dimensiones.

### Planificación · contratos de consumidores concretos

Handlers internos: CrearRuta, AgregarEnviosARuta, ModificarPlanificacionRuta, PrevalidarRuta, ConsultarRutas, ConsultarDetalleRuta y ConsultarValidacionesRuta.

IPlanificacionModuleApi expone a CU-04: ObtenerAsignacionesActivasAsync(envioIds, ct), PrevalidarCambioFranjasAsync(cambios, ct) y QuitarPorReprogramacionAsync(cambios, ct). Este último exige transacción y exclusión del operador, verifica Planificada y versiones, elimina paradas, actualiza Revision y libera reservas de rutas vacías. CU-04 coordina esta operación con ReprogramarPorCambioFranjaAsync de Envíos; no se hacen llamadas circulares entre las implementaciones de ambas APIs.

CU-43/CU-51/CU-55 usan operaciones del módulo dueño para despachar, iniciar y finalizar. La finalización libera reservas al cerrar la rendición; completar paradas por sí solo no lo hace. Sus implementaciones completas quedan en esos CU.

## 4. Persistencia

PlanificacionDbContext usa schema planificacion, ModuleDbContext y la conexión de IUnidadDeTrabajo. Agrega tablas Rutas, Paradas, ValidacionesRuta y BultosValidacionRuta. Las referencias a recursos/envíos de otros módulos se guardan como IDs, sin FKs ni navegación entre sus modelos EF. Dentro del módulo se usan FKs compuestas por OperadorId e Id para impedir asociar hijos a otro operador.

| Restricción | Implementación |
| :--- | :--- |
| RF 15 | Índice único parcial Paradas(EnvioId) WHERE Estado = 'Pendiente'. EnvioId es Guid global; OperadorId también se valida en toda lectura/escritura. |
| Reserva por día | Índices únicos Rutas(OperadorId, Fecha, RepartidorId) y (OperadorId, Fecha, VehiculoId), ambos WHERE ReservaActiva = true. |
| Versión de ruta | Revision como token de concurrencia EF, comparado además con RevisionEsperada de la petición. |
| Parada repetida en ruta | Índice único (RutaId, EnvioId) más protección del agregado. |
| Evidencia por confirmación | Unicidad (RutaId, RevisionRuta) en ValidacionesRuta. No se crea evidencia nueva para una operación sin cambios. |
| Historial independiente | BultosValidacionRuta depende de ValidacionRuta, no de Parada. Quitar una parada nunca elimina evidencia. EnvioId/BultoId son valores históricos. |

Lectores de rutas e historial usan consultas sin tracking y paginación. Mutaciones cargan paradas actuales; no necesitan cargar toda la evidencia histórica. El repositorio agrega un registro de validación sin editar registros previos. Ruta, Parada, ValidacionRuta y BultoValidacionRuta implementan IOperadorOwned y reciben el filtro Tenant y la validación de escrituras de ModuleDbContext integrado con el trabajo de Ezequiel. No implementan IComercioOwned: la planificación es del operador, aunque contenga envíos de varios comercios. Los repositorios no duplican los filtros de sesión ni reciben ICurrentTenant sólo para escribir un WHERE tenant; las comparaciones entre filas y las validaciones de negocio se conservan.

El dominio sigue creando los hijos con OperadorId copiado de su padre; el interceptor verifica esa pertenencia. Los DTO y complex types no son entidades con filtro independiente. Outbox mantiene la excepción prevista por ADR-0002/ADR-0003 y el aislamiento se transporta en sus metadatos.

## 5. Exclusión y flujo transaccional

Se usa una exclusión corta por operador al confirmar. Administración realiza SELECT de su OperadorId con FOR UPDATE, siempre dentro de la transacción compartida. No se mantiene ningún bloqueo mientras el usuario edita o confirma en pantalla. No se agrega una tabla de reservas: la reserva se representa por Rutas.ReservaActiva.

Ese SELECT es un caso de SQL específico y delimitado según ADR-0002, sección 4. Operador es una entidad global, sin filtro Tenant; el SQL usa obligatoriamente WHERE Id = @operadorId con parámetro tomado de ICurrentTenant, falla antes de consultar si no hay operador y no admite un ID enviado por el cliente. Vive sólo en el adaptador de Administración, sin IgnoreQueryFilters, y cuenta con pruebas de exclusión e aislamiento. No se extrapola esta excepción a consultas o escrituras SQL de negocio. Los índices/migraciones se gestionan como infraestructura; las demás escrituras del flujo pasan por SaveChanges y el interceptor.

ModuleDbContext debe exponer a la infraestructura una operación de participación en la transacción antes de leer o bloquear, además de la adhesión actual al guardar. BloquearPlanificacionAsync la utiliza antes del SELECT FOR UPDATE. El instante de lectura y la transacción no se delegan al comportamiento implícito del proveedor. Las consultas de confirmación usan DTO sin tracking o entidades recién cargadas; no reutilizan una lectura de prevalidación.

Esta elección serializa confirmaciones del mismo operador durante el guardado y permite operadores distintos en paralelo. Es una decisión de simplicidad para el proyecto; la infraestructura puede evolucionar a bloqueos por recurso sin cambiar el contrato funcional. No basta con una comprobación previa o sólo con los índices por fecha para detectar rutas EnCurso de días anteriores.

Todas las operaciones que alteren reservas o configuración usada al planificar toman esta misma exclusión antes de leer datos para confirmar: CU-40, quitar paradas, despachar/iniciar/finalizar, cambios de franjas, publicación de reglas y cambios/desactivación de recursos. El módulo propietario actualiza sus propias filas. Las publicaciones de avisos no necesitan este bloqueo.

Flujo de confirmación:

1. Verificar sesión/rol, forma del pedido y referencias duplicadas; abrir IUnidadDeTrabajo.
2. Bloquear el operador mediante su contrato de Administración y obtener el instante de evaluación desde TimeProvider.
3. Releer ruta y RevisionEsperada si existe; configuración, recursos, envíos y medidas. Ningún dato de prevalidación se usa como autorización definitiva.
4. Comprobar recursos activos y propios. Excluir la reserva de la ruta editada. Rechazar otra reserva para el mismo día o una ruta Despachada/EnCurso de un día anterior al solicitado que siga sin finalizar.
5. Validar todos los envíos y paradas existentes con PlanificadorDeRuta; identificar candidatos que ya no están disponibles. No se guardan subconjuntos.
6. Crear/modificar Ruta y sus Paradas; actualizar Revision/ReservaActiva; asignar sólo los nuevos envíos por T3/T13 mediante Envíos.
7. Crear ValidacionRuta con el estado final completo evaluado, la revisión resultante y las copias de recursos, límites y medidas. Guardar cambios y mensajes Outbox de los módulos en la misma transacción.
8. Confirmar y responder. Ante un fallo, revertir todo, descartar el contexto de trabajo y obtener los datos necesarios para el conflicto con un scope limpio. No continuar guardando con el tracker del intento fallido.

La exclusión no reemplaza el índice RF 15 ni los tokens: cancelaciones, reprogramaciones u otros escritores pueden competir con un envío. Envíos verifica su transición y xmin. Las excepciones de restricciones conocidas se traducen por nombre de índice/token; no se convierte todo error de base de datos en conflicto.

No se reintentan automáticamente asignaciones parciales. Un fallo técnico desconocido permanece como fallo técnico, con rollback y registro interno.

Fundamento: [bloqueos de filas de PostgreSQL](https://www.postgresql.org/docs/17/explicit-locking.html#LOCKING-ROWS) y [Read Committed](https://www.postgresql.org/docs/17/transaction-iso.html#XACT-READ-COMMITTED) permiten releer después de obtener la exclusión dentro de la transacción compartida; [Npgsql](https://www.npgsql.org/efcore/modeling/concurrency.html) documenta el token xmin ya utilizado en Envíos.

## 6. API HTTP y errores

DTO en Logistica.Http.Contracts/Planificacion. Minimal APIs y páginas llaman a los mismos handlers. Ningún request incluye operador, responsable, peso, capacidades ni versiones de reglas como valores confiables.

| Método y ruta | Datos y resultado |
| :--- | :--- |
| GET /api/planificacion/recursos | Recursos activos y filtros de zona/franja para la fecha consultada. |
| GET /api/planificacion/envios-disponibles | Fecha y filtros paginados; candidato con peso/volumen efectivos. |
| POST /api/planificacion/rutas/prevalidacion | RutaId? y RevisionEsperada?; Fecha, RepartidorId, VehiculoId, EnvioIds. Resultado con carga, límites y restricciones; no escribe ni reserva. |
| POST /api/planificacion/rutas | Fecha, RepartidorId, VehiculoId y EnvioIds no vacíos. 201 con Id, estado, Revision, Location y resumen de carga. |
| GET /api/planificacion/rutas | Listado paginado por fecha y estado. |
| GET /api/planificacion/rutas/{id} | Detalle, paradas, recursos y Revision. |
| POST /api/planificacion/rutas/{id}/envios | RevisionEsperada y nuevos EnvioIds no vacíos. 200 con nueva Revision y resumen. |
| PUT /api/planificacion/rutas/{id}/planificacion | RevisionEsperada, Fecha, RepartidorId, VehiculoId. 200 con nueva Revision. Conserva envíos. |
| GET /api/planificacion/rutas/{id}/validaciones | Historial paginado; detalle de una validación por identificador perteneciente a la ruta. |

200 en prevalidación puede contener restricciones: es el resultado de evaluar una selección. Un pedido mal formado responde 400. Al confirmar, una restricción de dominio responde 400 ProblemDetails con codigo y restricciones; una selección desactualizada responde 409 con codigo, envioIdsNoDisponibles, recursosEnConflicto o revisionActual según el caso. Identificadores no accesibles no revelan información de otros operadores; ruta inexistente/no accesible responde 404. TenantMismatchException se registra para auditoría y se traduce a 404 genérico, sin exponer IDs ni detalles del otro inquilino; se implementa una vez en el host, reutilizando el trabajo transversal si ya está integrado. Sesión ausente 401 y perfil insuficiente 403.

Códigos estables: CapacidadPesoExcedida, CapacidadVolumenExcedida, BultoNoAdmitido, MaximoParadasExcedido, FechaIncompatible, FranjaIncompatible, EnvioNoDisponible, RepartidorOcupado, VehiculoOcupado, RutaModificada y RutaNoEditable. Campos opcionales sólo se incluyen cuando aplican. El cliente no decide el texto de la regla ni obtiene nombres de recursos de otro operador.

## 7. Backoffice, sesión y tiempo

Páginas dentro de Presentation/Features del módulo, siguiendo el patrón Razor existente:

* /backoffice/rutas: listado por fecha/estado, acceso a nueva ruta y detalle.
* /backoffice/rutas/nueva: fecha, recursos, filtros, selección y carga contra límites. Agregar a la selección prevalida; confirmar escribe. Cancelar no persiste.
* /backoffice/rutas/{id}: paradas, carga, estado y acciones de agregar/modificar sólo en Planificada; acceso al historial de validaciones.

Ante EnvioNoDisponible, se quitan de la selección únicamente los envíos informados, se recalcula y se solicita reconfirmar. Ante recurso ocupado se conserva la selección y se pide otro recurso. Ante RutaModificada se recarga la ruta actual, se conserva la intención de agregar para reevaluarla y se exige nueva confirmación. No se reenvía automáticamente con la nueva versión.

Policies separadas: PlanificarRutas exige perfil Despachador, operador resuelto y ausencia de sesión de comercio; AdministrarFranjas exige Administrador. Cookies y antiforgery protegen los POST/PUT; en pruebas se usa autenticación de prueba con claims de usuarios distintos, no una cabecera tenant habilitada en producción. El responsable se resuelve desde la identidad mediante un servicio scoped, no desde el body.

La ruta usa DateOnly en la zona horaria de Operador. Reloj y registros se guardan en UTC. Las 24 horas de CU-04 se calculan convirtiendo fecha + hora de franja de esa zona a un instante, no usando la zona horaria del equipo del despachador. Una hora local inexistente o ambigua por cambio horario se rechaza con una restricción explícita antes de programar; no se elige silenciosamente un instante.

## 8. Integración del cambio programado de franjas · CU-04

Comando objetivo: ProgramarCambioFranja(FranjaAnteriorId, VigenteDesde, HoraDesde, HoraHasta, Dias). Devuelve nueva franja, vigencias, envíos/rutas afectados y mensajes registrados. Una prevalidación separada muestra el impacto; al confirmar se vuelve a consultar bajo la exclusión del operador.

La nueva franja conserva ReemplazaAId. Un reemplazo tiene vigencia posterior al inicio de su anterior, pertenece a la misma zona y no introduce ciclos. No se admiten dos reemplazos competidores para una misma franja: los cambios sucesivos se encadenan reemplazando la última franja de la cadena. Los días, intervalos y vigencias no pueden producir franjas ofrecidas superpuestas para una misma zona/día/fecha.

Envíos expone ConsultarAfectadosCambioFranjaAsync y ReprogramarPorCambioFranjaAsync. Sólo se modifican estados operativos anteriores al despacho: Admitido actualiza el compromiso sin transición; EnDeposito con día definido aplica T18; Reprogramado actualiza fecha/franja sin repetir transición; AsignadoARuta aplica T19 y sale de su ruta Planificada. Estados en calle, terminales o en devolución no se reprograman; no se altera su historial. Una ruta Despachada/EnCurso afectada impide confirmar.

Si una franja nueva no atiende el día ya comprometido, se informa incompatibilidad y se rechaza el conjunto; no se cambia automáticamente la fecha. Los envíos sin día definido conservan la referencia de preferencia a la cadena de franjas y resuelven su versión por fecha al planificar. Se registra el cambio de oferta y se informa al destinatario/comercio que el nuevo horario aplica desde la fecha de vigencia, sin inventar una fecha de entrega.

Orden transaccional: bloquear operador, releer impacto, verificar >24 horas para cada compromiso fechado, prevalidar las rutas restantes, aplicar reprogramaciones y quitar paradas por T19, cerrar/crear franjas, agregar evidencia de revalidación de rutas afectadas y mensajes Outbox, guardar y confirmar. La nueva evidencia usa una operación adicional CambioFranjaProgramado. Un conflicto con un envío revierte el conjunto y exige reevaluar el impacto antes de otra confirmación.

CU-04 guarda en su Outbox el cambio de configuración; Envíos guarda EnvioReprogramado y el evento de cambio de compromiso sin transición cuando corresponda. Los mensajes identifican versión del esquema, MessageId, OperadorId, CorrelationId, origen, responsable, instante UTC y datos de horario anterior/nuevo. Seguimiento procesa avisos al destinatario y comercio con inbox idempotente. No se envía correo dentro de la transacción. La cobertura del flujo completo de CU-04 es un entregable propio, no se considera implementada al terminar la pantalla de CU-40.

## 9. Pruebas y criterios de aceptación

| Grupo | Casos verificables |
| :--- | :--- |
| Dominio | Igualar límites permitido; exceder peso/volumen/paradas rechazado; rotación válida y bulto que no entra; sumas por todos los bultos; dimensiones medidas parciales con respaldo; duplicados; estados editables; Revision y ReservaActiva. |
| Fecha/franja | Fecha programada coincidente/diferente; envío sin fecha; sin franja; día incompatible; franjas antes/después de vigencia y cadena de reemplazos; zona horaria del operador. |
| Flujo | Creación 201; T3/T13 y eventos; ampliación incluye carga existente; modificación conserva paradas y revalida; fallo no cambia recursos; historial inmutable sin borrar al quitar parada. |
| RF 15 | Dos requests con scopes/conexiones independientes y recursos distintos compiten por el mismo envío: una confirma y la otra obtiene 409, sin filas parciales. Se prueba además el índice parcial con SQL directo. |
| Recursos | Dos requests para el mismo vehículo/repartidor y fecha; misma fecha contra ruta activa; bloqueo por ruta EnCurso anterior; ruta vacía libera y recupera; Finalizada libera. |
| Ruta concurrente | Dos ampliaciones con la misma RevisionEsperada: una confirma; otra recibe conflicto y no supera límites. Competencia entre modificar/agregar/quitar y despachar. |
| Atomicidad | Fallo después de guardar un módulo antes del commit: ningún cambio, evento ni mensaje persiste. Conflicto se reconstruye desde contexto limpio. |
| Seguridad | Dos operadores, varios despachadores, sesión de comercio y sesión ausente; IDs ajenos no se leen ni asignan; CSRF rechazado; actor y operador provienen de claims. |
| Aislamiento transversal | Reutilizar AislamientoTests y extender a PlanificacionDbContext: filtro Tenant en todas sus entidades IOperadorOwned, lecturas A/B con contextos distintos, ausencia de tenant, escrituras ajenas, cambio de OperadorId, hijos e historial. Verificar también el SQL de bloqueo global con ID exclusivamente de la sesión. |
| CU-04 | Vigencia futura no se desplaza con pedidos nuevos; >24 horas permitido y <=24 bloqueado; T18/T19; salida de ruta preserva fecha y evidencia; cambio inválido revierte todo; nueva franja sin día compatible se rechaza. |
| Mensajería | EnvioAsignadoARuta en Outbox con la asignación; publicador/receptor según ADR-0003; reintento de aviso no revierte operación y duplicado no duplica efectos. |
| UI | Carga y errores visibles; selección conservada en conflicto de recursos; sólo indisponibles se retiran; reconfirmación explícita; acciones según estado. |

Integración usa PostgreSQL 17.6 real y PostgresApiFactory/Testcontainers. Las carreras se coordinan con barreras en las pruebas, no con sleeps. No se exige que gane un usuario concreto. Tests unitarios verifican dominio y no replican mapeos EF. Se ejecutan pruebas de arquitectura existentes y suites afectadas. No se certifica concurrencia mediante base en memoria.

Seed y pruebas que crean datos de otro operador construyen el DbContext con InquilinoFijo, siguiendo la guía; no escriben datos ajenos con el contexto del request. Las excepciones a IgnoreQueryFilters siguen la lista cerrada del ADR-0002. Las pruebas no sustituyen el filtro global por un filtro sólo en readers, y no es necesario modificar deliberadamente el interceptor para validar CU-40.

## 10. Orden de implementación y cierre

1. Preparar contratos y datos mínimos persistidos de Administración, campos de planificación de Envíos y medidas de recepción de Depósito. Por acuerdo de Lucas se avanza en desarrollo antes de integrar el tenant. Añadir participación transaccional sin duplicar su infraestructura. Al integrar la versión de Ezequiel, conservar filtros/interceptor y ejecutar sus pruebas; conectar sesión y políticas del host antes del cierre.
2. Dominio y persistencia de Planificación, reservas, Revision, índice RF 15 y ValidacionRuta.
3. Consulta/prevalidación, confirmación transaccional T3/T13, Outbox y errores de conflicto.
4. Endpoints y páginas del Backoffice; pruebas unitarias, PostgreSQL y arquitectura.
5. Integraciones de lifecycle y mensajería según sus CU; programación masiva de franjas como ampliación de CU-04, con sus pruebas propias.

El diseño de CU-40 queda definido para comenzar. Su implementación sólo se declara completa cuando pasan los criterios de su alcance y se integran autenticación y Outbox durable. Los CU vecinos se identifican como dependencias o entregables separados, sin afirmar que estén implementados por existir sus contratos.
