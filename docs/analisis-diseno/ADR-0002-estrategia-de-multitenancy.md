# **ADR-0002 · Estrategia de multitenancy**

| Estado | Pendiente de revisión por el equipo |
| :---- | :---- |
| **Versión** | 2.1 |
| **Fecha** | 2 de octubre de 2026 |
| **Responsable** | Cristian Reyes |
| **Equipo** | Equipo 1 - Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Estrategia de multitenancy |
| **Relacionado con** | ADR-0001 · Estilo arquitectónico · ADR-0003 · Outbox y mensajería |

**Este registro define cómo un único despliegue del sistema atiende a varios operadores logísticos manteniendo aislados sus datos, y cómo se aísla además a los comercios dentro de cada operador.**

# **1\. Contexto y restricciones**

## **1.1 Qué es multitenancy**

Multitenancy significa que **un solo despliegue del sistema** —una sola API corriendo, una sola base de código— atiende a **varios clientes distintos** sin que ninguno vea los datos de otro. Cada cliente es un *tenant* (inquilino).

Analogía: es la diferencia entre construir una casa entera para cada familia (un despliegue por cliente, carísimo de mantener) y construir un edificio de departamentos donde cada familia tiene su propia unidad con llave propia, pero comparten el mismo edificio, el mismo ascensor y los mismos cimientos (un único sistema, aislamiento lógico entre inquilinos).

En este proyecto, el **inquilino es el operador logístico** (§3.3 de la letra: *"El operador logístico es el inquilino (tenant) de la plataforma: contrata el servicio y opera con independencia de los demás operadores"*). Dos operadores logísticos distintos usan exactamente el mismo sistema desplegado, pero el Operador A jamás debe poder ver un envío, un repartidor o una tarifa del Operador B.

Hay además un **segundo nivel de aislamiento**, más chico, adentro de cada operador: los **comercios**. Un comercio es cliente *de* un operador y sólo debe ver sus propios envíos: ni los de otros comercios del mismo operador, ni mucho menos los de otro operador.

## **1.2 Restricciones del laboratorio**

- La letra (§6.5) exige explícitamente: (a) una estrategia de tenencia **definida y justificada** —por fila, por esquema o por base de datos— que trate también el segundo nivel de aislamiento de los comercios; (b) resolución del inquilino **en tiempo de ejecución**; (c) aislamiento efectivo en lecturas y escrituras, **con pruebas automatizadas** que demuestren que no hay filtración de datos entre inquilinos; (d) identidad visual y reglas de negocio diferenciadas por inquilino; (e) migraciones y datos de inicialización con soporte multi-inquilino; (f) demostración en vivo con **al menos dos operadores de configuración distinta**.
- El monitoreo del 15 de octubre pide explícitamente "multitenancy funcionando con dos operadores de configuración distinta": para esa fecha esto tiene que estar andando, no sólo diseñado.
- Equipo de 3 personas, con dedicación parcial y sin experiencia previa fuerte en soluciones multi-tenant. La cantidad de horas semanales que cada uno le dedica es un dato de planificación del equipo y se lleva en el plan de trabajo, no en este ADR; lo que importa acá es la restricción que se deriva de ese dato: tiempo y experiencia acotados condicionan directamente qué estrategias son viables (ver sección 3).
- El equipo está exento del requerimiento de escalado horizontal (§6.15): corre una sola instancia de la API, lo cual **simplifica** esta decisión, porque no hay que sincronizar el contexto del tenant entre varias instancias.
- Restricción de negocio: un mismo comercio puede operar con más de un operador logístico (§3.3). El aislamiento de comercios no puede asumir que un comercio pertenece a un único operador de forma fija.
- Se usa .NET 10 con EF Core, con inyección de dependencias nativa como estándar del equipo.
- **Organización definida por el ADR-0001:** monolito modular de seis módulos, cada uno con su propio DbContext; código técnico común en BuildingBlocks; un único host web (Logistica.Api) que sirve la API, el Backoffice y las tres aplicaciones Blazor WebAssembly desde el mismo origen; y un worker desplegado aparte que recibe mensajes por RabbitMQ. El aislamiento entre inquilinos tiene que funcionar igual en todos esos puntos de entrada, no sólo en los requests HTTP.
- **Dos puntos de entrada donde el inquilino todavía no se conoce.** Los usuarios inician sesión con ASP.NET Core Identity (CU-01, CU-03), cuyo UserManager hace sus propias consultas a la base. Y el equipo decidió implementar la API pública (opcional 7.4, CU-80 a CU-83): el sistema del comercio se autentica con una clave, sin cookie. En los dos casos hay que encontrar un dato *para saber* quién es el inquilino.

# **2\. Decisión adoptada**

## **2.1 Aislamiento por fila**

Todos los operadores comparten la misma base de datos y las mismas tablas. Toda entidad que pertenezca a un operador lleva una columna OperadorId e implementa el marcador IOperadorOwned; si además pertenece a un comercio, lleva ComercioId e implementa IComercioOwned. El aislamiento se aplica automáticamente con un **Global Query Filter de EF Core**, de modo que ninguna consulta LINQ puede "olvidarse" de filtrar por tenant salvo que alguien lo desactive a propósito.

La extensión ApplyTenantQueryFilters() recorre las entidades marcadas y les agrega el filtro. Dos reglas de implementación son parte de la decisión:

- **El filtro lee el tenant desde una propiedad del DbContext**, nunca desde un valor capturado al construir el modelo. EF Core construye el modelo una sola vez y lo reutiliza en todos los requests: un valor capturado quedaría fijo para siempre, y todos verían los datos del primer operador.
- **El filtro tiene nombre: "Tenant".** EF Core 10 admite varios filtros por entidad, identificados por nombre. Sin nombre, un segundo HasQueryFilter sobre la misma entidad (por ejemplo, un borrado lógico agregado más adelante) reemplazaría silenciosamente al filtro de inquilino.

**Fallar cerrado.** Si ICurrentTenant no tiene un OperadorId cargado, la condición no coincide con ninguna fila. Sin inquilino no se ve nada; nunca se ve todo.

Como refuerzo adicional, toda restricción de unicidad que hoy sería "global" (por ejemplo, número de envío único) pasa a ser una restricción **compuesta con OperadorId**: única por operador, no única en toda la tabla. Esto evita, además, que dos operadores choquen entre sí por una regla de negocio que en realidad es local a cada uno.

## **2.2 Dónde vive cada pieza**

| **Pieza** | **Proyecto** | **Motivo** |
| :---- | :---- | :---- |
| IOperadorOwned, IComercioOwned, interfaz ICurrentTenant, TenantMismatchException | Logistica.SharedKernel | C# puro, sin dependencias: el dominio de cada módulo puede usarlos |
| TenantSaveChangesInterceptor, ApplyTenantQueryFilters(), ModuleDbContext | Logistica.BuildingBlocks.Infrastructure | Dependen de EF Core; una sola implementación para los seis módulos |
| Implementación de ICurrentTenant para mensajes | Logistica.BuildingBlocks.Infrastructure | Depende de los metadatos del mensaje. La usan todos los consumidores: los del Worker y los de los módulos que corren en la API (ADR-0003, sección 2.4) |
| Implementación de ICurrentTenant para HTTP (claims o token de seguimiento) | Logistica.Api | Depende de ASP.NET Core |
| Handler de autenticación por clave de API (/api/v1) | Logistica.Api | Depende de ASP.NET Core; convierte la clave en claims, igual que la cookie |
| IdentityDbContext con la entidad Usuario | Logistica.Api (o el proyecto de infraestructura que lo aloje) | Hereda de la clase base de Identity, no de ModuleDbContext (sección 2.4) |

Cada DbContext de módulo hereda de ModuleDbContext, que registra el interceptor y los filtros. Un módulo no escribe filtros ni validaciones de inquilino propias: le alcanza con implementar los marcadores en sus entidades.

El tenant actual se resuelve una vez por request (o por mensaje, en cada consumidor) y se expone mediante un servicio de ámbito *scoped* (ICurrentTenant), inyectado por DI. Nunca se lee "a mano" desde el contexto HTTP en medio de la lógica de negocio, para no romper el aislamiento entre dominio e infraestructura que define el ADR-0001.

## **2.3 Escrituras: TenantSaveChangesInterceptor**

Los Global Query Filters protegen automáticamente las **lecturas**. Pero por sí solos **no garantizan el aislamiento de las escrituras**: nada impide, salvo disciplina humana, que un handler guarde una entidad con el OperadorId equivocado, la deje en null o modifique el OperadorId de una entidad ya existente. Por eso la estrategia incluye TenantSaveChangesInterceptor, un SaveChangesInterceptor de EF Core que se ejecuta en cada SaveChanges y:

- Asigna automáticamente el OperadorId del tenant actual a toda entidad nueva, y el ComercioId cuando la sesión trae uno, sin que el handler tenga que setearlos a mano.
- Rechaza, con TenantMismatchException, cualquier intento de guardar una entidad con un OperadorId distinto al del tenant actual.
- Impide modificar el OperadorId de una entidad ya existente una vez creada.
- Aplica las mismas reglas sobre ComercioId cuando la sesión trae un comercio (portal del comercio): la entidad debe tener ese ComercioId y no puede cambiarlo.

**Lo que el interceptor no valida.** Cuando el personal del operador crea o edita un envío para un comercio, la sesión no trae ComercioId. En ese caso hay que verificar que el comercio tenga una RelacionComercial activa con el operador, y eso es una regla de negocio. Según el criterio de admisión del ADR-0001, BuildingBlocks no puede conocer conceptos del dominio como Comercio o RelacionComercial. Esa validación pertenece al caso de uso (por ejemplo, CrearEnvio), que la consulta al módulo de Administración y configuración mediante su contrato IAdministracionModuleApi.

Este interceptor es el que cierra, del lado de las escrituras, lo que los Global Query Filters resuelven del lado de las lecturas. Su alcance tiene un límite conocido: cubre SaveChanges, pero no las operaciones que EF Core ejecuta sin pasar por el change tracker (ExecuteUpdate, ExecuteDelete) ni el SQL crudo. Ver sección 4.

## **2.4 Uso de IgnoreQueryFilters() y entidades sin filtro**

Ignorar el filtro "Tenant" —por nombre, o con IgnoreQueryFilters() sin argumentos, que ignora todos los filtros— está prohibido fuera de esta lista cerrada. Ignorar otros filtros con nombre (por ejemplo, un borrado lógico) sí está permitido.

| **Caso autorizado** | **Por qué necesita ignorar el filtro** | **Cómo se protege** |
| :---- | :---- | :---- |
| Seed de datos de inicialización | Escribe datos de varios operadores | Se ejecuta sólo al iniciar o migrar; ningún endpoint lo alcanza |
| Pruebas automatizadas de aislamiento | Necesitan ver qué hay realmente en la base | Existe sólo en el proyecto de pruebas |
| Listar los operadores con los que trabaja un comercio, al iniciar sesión | Antes de elegir operador, el usuario del comercio necesita ver sus RelacionComercial con todos sus operadores | Es una única consulta que filtra explícitamente por el ComercioId del usuario autenticado (tomado de la cookie, nunca de un valor enviado por el cliente) y devuelve sólo la identificación de cada operador |
| Buscar la ClaveApi por su hash, al autenticar un request de la API pública | El request trae sólo la clave; el inquilino se conoce *después* de encontrarla | Es una única consulta, sin seguimiento de cambios, en el handler de autenticación. Busca por el hash de la clave recibida y devuelve sólo OperadorId, ComercioId, tipo (producción o prueba) y si está activa. A partir de ahí el request tiene inquilino y todo lo demás pasa por el filtro |

Cada uso se documenta en el código, en el mismo lugar donde ocurre, con una referencia a esta sección. Cualquier caso nuevo requiere actualizar este ADR.

**ClaveApi implementa IOperadorOwned e IComercioOwned.** Pertenece a una RelacionComercial, así que lleva los dos identificadores. Así, el listado de claves del Portal (CU-80) queda protegido por el filtro sin escribir nada: un comercio sólo ve sus claves, y sólo las de la relación con el operador elegido. La excepción se limita a la búsqueda por hash.

**Por qué esta búsqueda es aceptable y la del seguimiento no.** En la sección 2.5 se descarta buscar el envío ignorando el filtro para "descubrir" su operador. Lo que se evita ahí es la **enumeración**: probar valores hasta acertar con uno que exista. La clave de API es un valor aleatorio largo (por ejemplo, 32 bytes) y se busca por su hash, así que no hay valores que probar: la protección la da que el secreto no se puede adivinar. Aun así, la limitación de tasa de la API pública (§7.4 de la letra) se aplica también a los intentos con claves inválidas.

**Último uso de la clave.** CU-80 muestra cuándo se usó cada clave por última vez. Ese dato se actualiza después de fijar el inquilino, con un SaveChanges normal que pasa por el interceptor; nunca con ExecuteUpdate sobre la consulta que ignora el filtro (sección 4).

Tres situaciones que podrían parecer excepciones se resuelven por diseño, sin ignorar el filtro:

- **Outbox.** La tabla del Outbox no implementa IOperadorOwned: es infraestructura técnica, no un dato que un operador posea. Guarda el OperadorId como metadato del mensaje, la lee únicamente el publicador del Outbox en BuildingBlocks y ningún endpoint la expone. Figura con nombre propio en la lista de excepciones de la prueba estructural (sección 4).
- **Tareas del worker que procesan todos los operadores** (por ejemplo, reintentos de webhooks). El worker recorre los operadores activos y procesa cada uno en un ámbito de DI propio, con ese operador fijado como inquilino. Cada vuelta se comporta igual que un request de ese operador. La lista de operadores se puede leer porque Operador no tiene filtro de inquilino.
- **Usuario (ASP.NET Core Identity).** No implementa IOperadorOwned y no tiene filtro de inquilino, por dos motivos:
  - El UserManager y el SignInManager de Identity buscan al usuario por correo antes de que exista una sesión: al iniciar sesión, al activar la cuenta con el enlace de invitación (CU-01) y al recuperar la contraseña. Con el filtro, fallando cerrado, esas búsquedas no encontrarían a nadie y nadie podría entrar.
  - El usuario de un comercio no tiene un OperadorId fijo (sección 2.6), así que un filtro por operador nunca lo encontraría.

Además, el IdentityDbContext hereda de la clase base de Identity y no de ModuleDbContext, así que el filtro no se le aplicaría de todos modos. Usuario figura con nombre propio en la lista de excepciones de la prueba estructural, junto al Outbox (sección 4).

**Cómo se compensa la falta de filtro.** Las consultas del personal del operador sobre usuarios (por ejemplo, el listado de CU-01) filtran por el OperadorId de ICurrentTenant en **un único lugar**, un servicio de consulta de usuarios; ningún otro código consulta la tabla de usuarios directamente. Al crear un usuario, el caso de uso asigna el OperadorId desde ICurrentTenant, nunca desde un valor enviado por el cliente, porque acá el interceptor no actúa. La prueba de aislamiento incluye este listado (sección 4).

## **2.5 Resolución del inquilino en tiempo de ejecución**

- **Backoffice, Portal Comercio y App del repartidor** (todas autenticadas): el OperadorId (y el ComercioId cuando corresponde) viaja como *claim* en la cookie de autenticación, cargado al hacer login. Como las tres se sirven desde Logistica.Api (ADR-0001, sección 2.6), un único middleware lo lee al principio del pipeline, antes de que el request llegue a cualquier handler.
- **Seguimiento público** (sin autenticación, RF 25): no hay usuario logueado que declare el tenant. El enlace de seguimiento (/seguimiento/{token}) no es un ID incremental adivinable: es un **token firmado** que incluye el OperadorId —y el ComercioId cuando aplica— del envío al que apunta. El middleware valida la firma y toma el OperadorId **directamente del token**, antes de tocar cualquier repositorio. No se busca primero el envío ignorando el filtro para "descubrir" a qué operador pertenece: eso reintroduciría exactamente el vector de enumeración que la 6.3 pide evitar.
- **API pública** (/api/v1, CU-81 a CU-83): el sistema del comercio envía la clave en una cabecera. El handler de autenticación busca la clave por su hash (cuarto caso de la sección 2.4) y, si está activa, arma la identidad del request con OperadorId, ComercioId y el tipo de clave como claims. Desde ahí ICurrentTenant se resuelve igual que con la cookie, y el resto del request no distingue de dónde vino. Una clave inexistente o revocada responde **401**, sin indicar cuál de los dos casos es.
- **Consumidores de mensajes** (Worker y API): el OperadorId (y el ComercioId cuando aplica) viaja en los metadatos del mensaje. Ese dato se escribe al crear la fila del Outbox, tomado del ICurrentTenant del request original; nunca se toma del contenido del mensaje ni de un origen externo. La regla es la misma para los consumidores del Worker y para los de los módulos que corren en la API (ADR-0003). El consumidor abre un ámbito de DI por mensaje y fija el inquilino antes de resolver cualquier DbContext. Un mensaje que llega sin inquilino va a la cola de mensajes fallidos y nunca se procesa sin filtro.
- **SignalR:** al establecerse la conexión, el hub lee el OperadorId de los claims y agrega la conexión al grupo operador-{OperadorId} (y comercio-{ComercioId} cuando corresponde). Los mensajes en tiempo real se envían siempre a un grupo, nunca a todos los clientes conectados.

## **2.6 Segundo nivel: comercios dentro del operador**

Las entidades del ciclo de vida del envío (Envío, Bulto, etc.) llevan tanto OperadorId como ComercioId. El filtro de estas entidades es condicional: siempre filtra por OperadorId y, además, por ComercioId cuando ICurrentTenant trae uno cargado (sesión del portal del comercio o clave de API). Cuando no lo trae —el personal del propio operador, que legítimamente ve todos los comercios de su operador— el filtro se aplica sólo por OperadorId. Así el mismo filtro sirve para los dos roles sin duplicar la entidad ni el filtro.

Como un comercio puede operar con más de un operador (§3.3), la relación comercio↔operador queda decidida como **muchos a muchos**, modelada mediante una entidad propia, RelacionComercial, que vincula un comercio con un operador y guarda los datos propios de ese vínculo. La cuenta de usuario del comercio no tiene un único OperadorId fijo: tiene una o varias RelacionComercial activas, y el comercio elige con qué operador trabaja en cada sesión.

**Selección del operador en la sesión.** Cuando el usuario del comercio elige un operador, la aplicación vuelve a emitir la cookie de autenticación con el claim OperadorId de ese operador. Cambiar de operador es una nueva emisión de la cookie; la selección no se guarda en la base. Hasta que elige, la sesión tiene ComercioId pero no OperadorId y, por la regla de fallar cerrado, no ve datos de ningún operador. La única operación permitida en ese estado es el tercer caso de la sección 2.4.

**RelacionComercial y Comercio.** RelacionComercial implementa IOperadorOwned e IComercioOwned: el personal de un operador sólo ve las relaciones de su operador, y un comercio sólo ve las suyas. Comercio no tiene filtro de inquilino, porque es compartido entre operadores; por eso los casos de uso de un operador nunca lo consultan directamente, sino siempre a través de RelacionComercial.

## **2.7 Identidad visual y reglas de negocio por tenant**

Una vez resuelto el OperadorId, se carga la configuración de ese operador (colores, logo, cuadro tarifario, reglas operativas: RF 3, RF 4, RF 5) desde la misma base, filtrada igual que el resto. No hace falta ningún mecanismo aparte: es una entidad de configuración más, sujeta al mismo filtro por fila.

## **2.8 Migraciones y datos de inicialización**

Al ser aislamiento por fila, las migraciones **no dependen de la cantidad de operadores**: agregar un operador nuevo no requiere ninguna migración, sólo filas. Como el ADR-0001 define un DbContext por módulo, hay un conjunto de migraciones por módulo (un historial por DbContext), igual para todos los operadores, a diferencia de lo que pasaría con schema-per-tenant o database-per-tenant, donde cada cambio debe repetirse por operador. Si los módulos se separan en esquemas de PostgreSQL, se trata de una organización por módulo, no del schema-per-tenant descartado en la sección 3.

El seed de datos crea, desde el arranque, **al menos dos operadores** con configuración distinta (colores, cuadro tarifario, reglas operativas) y, para cada uno, sus propios comercios y relaciones comerciales. Siempre que sea posible, carga los datos de cada operador fijando ese operador como inquilino actual, de modo que el interceptor asigne y valide los OperadorId igual que en producción. Así, la demostración del 15/10 ("dos operadores de configuración distinta") no depende de cargar nada a mano el día de la demo: alcanza con correr las migraciones y el seed.

## **2.9 Respuesta ante intentos de acceso a otro inquilino**

Un intento de acceder a un recurso de otro inquilino responde igual que si el recurso no existiera: **404 genérico, sin detalle**. En las lecturas ocurre naturalmente, porque el filtro hace invisible el recurso. En las escrituras, TenantMismatchException se registra en el log con todos sus datos para auditoría, pero se traduce a la misma respuesta genérica: nunca un 403 ni un mensaje que confirme que el recurso existe. Es el mismo motivo por el que el seguimiento usa un token firmado: no dar información que permita enumerar recursos (§6.3).

# **3\. Alternativas consideradas y motivo de su descarte**

- **Por esquema (schema-per-tenant):** cada operador tendría su propio conjunto de tablas dentro de la misma base de datos. Se descartó porque EF Core no tiene soporte cómodo para apuntar dinámicamente a un esquema distinto por request, complica las migraciones (una migración deja de ser "una operación" y pasa a ser "una operación por cada esquema existente, incluyendo los que se crean después") y agregar un operador nuevo requiere crear infraestructura en caliente: mucho más trabajo del que el equipo puede sostener con el tiempo disponible.
- **Por base de datos (database-per-tenant):** cada operador con su propia base de datos completa. Es el aislamiento más fuerte de los tres, pero el más caro operativamente: N cadenas de conexión, N migraciones a mantener sincronizadas y, para la demo con al menos dos operadores, dos bases en el mismo ambiente. Se descartó por complejidad y por un costo de infraestructura desproporcionado para el alcance del laboratorio. Sería la elección correcta si un operador exigiera aislamiento físico de datos por un requisito legal o contractual real, que no es el caso.
- **Usar una librería de terceros para multitenancy (por ejemplo, Finbuckle.MultiTenant):** se descartó a favor de una implementación propia y simple. La letra evalúa la comprensión individual y la defensa oral del código propio (§2, §9.4); una librería resuelve el problema pero deja una caja negra que después es más difícil de explicar y defender que un ICurrentTenant y un filtro escritos por el equipo.
- **Filtrar a mano en cada consulta (Where(e => e.OperadorId == ...)):** se descartó porque el aislamiento dependería de que nadie se olvide nunca de escribir la condición. Con un filtro global, el olvido no es posible salvo desactivándolo a propósito.
- **Implementar el filtro y el interceptor dentro de cada módulo:** descartado en el ADR-0001. Serían seis copias del mecanismo que impide la filtración de datos, y cualquier diferencia entre ellas sería una fuga.

# **4\. Consecuencias**

**A favor:** una sola base de datos y un docker compose simple. Demostrar "dos operadores con configuración distinta" es sencillo: alcanza con datos de inicialización con dos filas de Operador configuradas distinto, no con infraestructura separada. El mecanismo se implementa una sola vez, en BuildingBlocks, y un módulo queda protegido con sólo marcar sus entidades. Es el camino con menor riesgo de no llegar a tiempo al monitoreo del 15/10.

**En contra / riesgos a mitigar:** el aislamiento depende por completo de que **toda** consulta pase por el filtro. Una consulta SQL cruda, un IgnoreQueryFilters() mal usado, una entidad nueva a la que alguien se olvide de ponerle el marcador, o una operación que pase por ExecuteUpdate/ExecuteDelete (que no pasan por el interceptor) son fugas de datos reales entre operadores: exactamente lo que la letra pide demostrar que *no* pasa.

**Lo que el filtro y el interceptor no protegen:**

| **Punto** | **Riesgo** | **Regla** |
| :---- | :---- | :---- |
| SQL crudo (FromSqlRaw, ExecuteSqlRaw) | No pasa por el filtro ni por el interceptor | Prohibido salvo un caso revisado que incluya el OperadorId explícitamente |
| ExecuteUpdate / ExecuteDelete | El filtro sí agrega el WHERE del inquilino, pero el interceptor no se ejecuta | Nunca modifican OperadorId ni ComercioId, y nunca se combinan con IgnoreQueryFilters() |
| Caché (Valkey) | El filtro no protege lo que ya está en caché | Toda clave de datos de un operador incluye su OperadorId (por ejemplo, tarifario:{OperadorId}); el seguimiento usa el token como clave |
| SignalR | El filtro no protege lo que se transmite | Grupos por operador y por comercio (sección 2.5) |
| Mensajes a los consumidores | El inquilino no llega por una cookie | Metadatos escritos desde el Outbox (sección 2.5) |
| Usuario (Identity) | No tiene filtro ni pasa por el interceptor | Consultas del personal en un único servicio que filtra por OperadorId; alta con el OperadorId de ICurrentTenant (sección 2.4) |

**Mitigación obligatoria, mediante pruebas automatizadas:**

1. **Prueba de aislamiento** (exigida por la §6.5): se siembran dos operadores y se verifica, en lecturas y en escrituras, que consultando como el operador A nunca aparece una fila del operador B. Cubre los DbContext reales de todos los módulos, no sólo un DbContext de prueba en BuildingBlocks. En la misma ejecución consulta como A y después como B con instancias de contexto distintas, para detectar un inquilino "congelado" en el modelo. Incluye también el listado de usuarios como personal de A (no aparece ningún usuario de B) y la API pública: con una clave de un comercio de A no se ve ningún envío de B ni de otro comercio de A.
2. **Prueba estructural, en dos partes:**
  - (a) Toda entidad con una propiedad OperadorId implementa IOperadorOwned, salvo una lista explícita de excepciones: el Outbox y Usuario de Identity (sección 2.4).
  - (b) En el modelo ya construido de cada DbContext real, toda entidad que implementa IOperadorOwned tiene el filtro "Tenant".

La parte (a) detecta "me olvidé del marcador". La (b) detecta "tiene el marcador, pero el filtro no se aplicó"; por ejemplo, un OnModelCreating que no llama a la clase base.

3. **Regla de arquitectura (a confirmar):** una prueba con ArchUnitNET que impida usar IgnoreQueryFilters, ExecuteUpdate, ExecuteDelete y SQL crudo fuera de los lugares autorizados. Se incorporará si la biblioteca permite expresarla; mientras tanto, se controla en la revisión de código.

Todas estas pruebas tienen que existir desde el principio y correr en el pipeline, no agregarse al final.

**Qué dificulta a futuro:** si algún día un operador exigiera aislamiento físico de sus datos (por un contrato o una norma), migrar de "por fila" a "por base de datos" para ese operador no es un cambio incremental: es prácticamente extraer sus datos a una base nueva y reescribir la resolución del tenant. Es una limitación real de esta decisión. El equipo la eligió por ser la adecuada para su tamaño y el tiempo disponible, no por ser la mejor opción en abstracto; frente a un requisito real de aislamiento físico, la decisión correcta habría sido database-per-tenant desde el principio.

# **5\. Decisiones abiertas conectadas a este ADR**

| **Tema** | **Por qué importa** |
| :---- | :---- |
| Reportes o consultas administrativas que necesiten ver todos los operadores (si los hubiera) | Tienen que ser un camino explícito y auditado, nunca el comportamiento por defecto. Mientras no exista ese caso de uso en el alcance actual, no hay excepciones a IgnoreQueryFilters() más allá de las de la sección 2.4. |
| RelacionComercial con filtro de inquilino | El modelo de dominio la lista como excepción a la convención de OperadorId; este ADR propone que implemente IOperadorOwned e IComercioOwned (sección 2.6). Debe acordarse con el responsable del modelo de dominio. |
| Outbox sin filtro de inquilino | Se confirma al implementar el Outbox en BuildingBlocks (ADR-0003). |
| Regla de arquitectura sobre IgnoreQueryFilters y operaciones masivas | Depende de que ArchUnitNET permita expresarla (sección 4, punto 3). |
| Correo único en todo el sistema | Identity exige por defecto que el correo sea único en toda la tabla, no por operador. Un repartidor o un empleado que trabaje para dos operadores necesita dos correos distintos. Los usuarios de comercio no tienen el problema: usan una sola cuenta para todos sus operadores (CU-01, decisión 4). Debe confirmarse con el equipo. |
| Envíos de prueba (EsPrueba) de la API pública | Los envíos creados con una clave de prueba no deben verse en el Backoffice, las rutas ni las liquidaciones. Puede resolverse con un segundo filtro con nombre ("Prueba"), separado de "Tenant": ignorarlo está permitido (sección 2.4), así que la API con clave de prueba puede verlos sin tocar el aislamiento entre operadores. Se define junto con la implementación de la API pública. |

# **6\. Referencias**

- Letra del laboratorio: §2 y §9.4 (evaluación y defensa individual); §3.3 (operadores y comercios); §6.3 (enumeración); §6.5 (multitenancy); §6.15 (escalado); §7.4 (API pública); RF 3, RF 4, RF 5 (configuración por operador); RF 25 (seguimiento público).
- ADR-0001 · Estilo arquitectónico.
- ADR-0003 · Outbox y mensajería (sobre del mensaje, consumidores con inbox).
- Casos de uso: CU-01 y CU-03 (usuarios e inicio de sesión), CU-80 a CU-83 (API pública).
- Modelo de dominio del equipo (convención de OperadorId, RelacionComercial, Usuario y ClaveApi).
- Microsoft Learn. EF Core: Global Query Filters (filtros con nombre en EF Core 10) e Interceptors.

*Equipo 1 · Laboratorio .NET 2026*
