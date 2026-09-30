# **ADR-0002 · Estrategia de multitenancy**

| Estado | Aceptado |
| :---- | :---- |
| **Fecha** | 25 de septiembre de 2026 |
| **Responsable** | Cristian Reyes |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Estrategia de multitenancy |

## **1\. Contexto y restricciones**

### **1.1 Qué es multitenancy**

Multitenancy significa que **un solo despliegue del sistema** una sola API corriendo, una sola base de código atiende a **varios clientes distintos** sin que ninguno vea los datos de otro. Cada cliente es un *tenant* (inquilino).

Analogía: es la diferencia entre construir una casa entera para cada familia (un despliegue por cliente — carísimo de mantener) y construir un edificio de departamentos donde cada familia tiene su propia unidad con llave propia, pero comparten el mismo edificio, el mismo ascensor, los mismos cimientos (un único sistema, aislamiento lógico entre inquilinos).

En este proyecto, el **inquilino es el operador logístico** ( 3.3 de la letra: *"El operador logístico es el inquilino (tenant) de la plataforma: contrata el servicio y opera con independencia de los demás operadores"*). Dos operadores logísticos distintos usan exactamente el mismo sistema desplegado, pero el Operador A jamás debe poder ver un envío, un repartidor o una tarifa del Operador B.

Hay además un **segundo nivel de aislamiento**, más chico, adentro de cada operador: los **comercios**. Un comercio es cliente *de* un operador, y sólo debe ver sus propios envíos ni los de otros comercios del mismo operador, ni mucho menos los de otro operador.

### **1.2 Restricciones del laboratorio**

* La letra ( 6.5) exige explícitamente: (a) una estrategia de tenencia **definida y justificada**  por fila, por esquema o por base de datos que trate también el segundo nivel de aislamiento de los comercios; (b) resolución del inquilino **en tiempo de ejecución**; (c) aislamiento efectivo en lecturas y escrituras, **con pruebas automatizadas** que demuestren que no hay filtración de datos entre inquilinos; (d) identidad visual y reglas de negocio diferenciadas por inquilino; (e) migraciones y datos de inicialización con soporte multi-inquilino; (f) demostración en vivo con **al menos dos operadores de configuración distinta**.  
* El monitoreo del 15 de octubre pide explícitamente "multitenancy funcionando con dos operadores de configuración distinta": para esa fecha esto tiene que estar andando, no sólo diseñado.  
* Equipo de 3 personas, con dedicación parcial (part-time) y sin experiencia previa fuerte en soluciones multi-tenant. La cantidad de horas semanales que cada uno le dedica es un dato de planificación del equipo y se lleva en el plan de trabajo del laboratorio, no en este ADR; lo que importa acá es la restricción que se deriva de ese dato: tiempo y experiencia acotados condicionan directamente qué estrategias son viables (ver sección 3).  
* Están exentos del requerimiento de escalado horizontal ( 6.15) — van a correr una sola instancia de la API, lo cual **simplifica** esta decisión: no hay que preocuparse por sincronizar el contexto del tenant entre varias instancias.  
* Restricción de negocio a tener en cuenta: un mismo comercio puede operar con más de un operador logístico ( 3.3) el aislamiento de comercios no puede asumir que un comercio pertenece a un único operador de forma fija.  
* Se usa .NET 10 con EF Core, con inyección de dependencias nativa como estándar del equipo.

## **2\. Decisión adoptada**

**Aislamiento por fila**: todos los operadores comparten la misma base de datos y las mismas tablas. Toda entidad que pertenezca a un operador lleva una columna `OperadorId`, y el aislamiento se aplica automáticamente con **Global Query Filters de EF Core** (`modelBuilder.Entity<T>().HasQueryFilter(e => e.OperadorId == _tenantContext.OperadorId)`), de modo que ninguna consulta LINQ puede "olvidarse" de filtrar por tenant salvo que alguien lo desactive a propósito (`IgnoreQueryFilters()`).

`IgnoreQueryFilters()` queda prohibido fuera de dos casos autorizados explícitamente: el seed de datos de inicialización (que necesariamente escribe filas de varios operadores) y las propias pruebas automatizadas de aislamiento, que necesitan poder leer sin filtro para verificar qué hay realmente en la base. Cualquier otro uso debe documentarse en el código, en el mismo lugar donde se usa, con el motivo puntual.

Los Global Query Filters protegen automáticamente las **lecturas**: cualquier consulta LINQ generada por EF Core queda filtrada por tenant sin que el desarrollador tenga que acordarse. Pero por sí solos **no garantizan el aislamiento de las escrituras**: nada impide, salvo disciplina humana, que un handler guarde una entidad con el `OperadorId` equivocado, la deje en null, o modifique el `OperadorId` de una entidad ya existente. Por eso la estrategia incluye un `SaveChangesInterceptor` de EF Core o un mecanismo equivalente  que se ejecuta en cada `SaveChanges` y:

* Asigna automáticamente el `OperadorId` del tenant actual a toda entidad nueva, sin que el handler tenga que setearlo a mano.  
* Rechaza, con una excepción de dominio explícita, cualquier intento de guardar una entidad con un `OperadorId` distinto al del tenant actual.  
* Impide modificar el `OperadorId` de una entidad ya existente una vez creada.  
* Aplica la misma validación sobre `ComercioId` en las entidades donde corresponde (segundo nivel de aislamiento).

Este interceptor es el que cierra, del lado de las escrituras, lo que los Global Query Filters resuelven del lado de las lecturas. Su alcance tiene un límite conocido: cubre `SaveChanges`, pero no las operaciones que EF Core ejecuta sin pasar por el change tracker (`ExecuteUpdate`, `ExecuteDelete`) ni el SQL crudo  ver "En contra / riesgos a mitigar" en la sección 4\.

Como refuerzo adicional al `OperadorId`, toda restricción de unicidad que hoy sería "global" (por ejemplo, número de envío único) pasa a ser una restricción **compuesta con `OperadorId`** — único por operador, no único en toda la tabla. Esto evita, además, que dos operadores choquen entre sí por una regla de negocio que en realidad es local a cada uno.

El tenant actual se resuelve una vez por request y se expone mediante un servicio de ámbito *scoped* (`ICurrentTenant`), inyectado por DI en repositorios y handlers nunca leído "a mano" desde el contexto HTTP en medio de la lógica de negocio, para no romper el aislamiento entre dominio e infraestructura que ya define el ADR-0001.

**Resolución del tenant en runtime, según la app:**

* **Backoffice, Portal Comercio, App del repartidor** (todas autenticadas): el `OperadorId` (y el `ComercioId` cuando corresponde) viaja como *claim* en el ticket de autenticación, cargado al hacer login, y un middleware lo lee al principio del pipeline antes de que la request llegue a cualquier handler.  
* **Seguimiento público** (sin autenticación, RF 25): no hay usuario logueado que declare el tenant. El enlace de seguimiento no es un ID incremental adivinable: es un **token firmado** que incluye el `OperadorId` — y el `ComercioId` cuando aplica — del envío al que apunta. El middleware de resolución de tenant valida la firma y toma el `OperadorId` **directamente del token**, antes de tocar cualquier repositorio. No se busca primero el envío ignorando el filtro de tenant para "descubrir" a qué operador pertenece: eso reintroduciría exactamente el vector de enumeración que la  6.3 pide evitar.

**Segundo nivel — comercios dentro del operador:** las entidades del ciclo de vida del envío (Envío, Bulto, etc.) llevan tanto `OperadorId` como `ComercioId`. El Global Query Filter de estas entidades es condicional: siempre filtra por `OperadorId`, y además filtra por `ComercioId` cuando `ICurrentTenant` trae uno cargado (sesión del portal del comercio); cuando no lo trae (personal del propio operador, que legítimamente ve todos los comercios de su operador), el filtro se aplica solo por `OperadorId`. Así el mismo `HasQueryFilter` sirve para los dos roles sin duplicar la entidad ni el filtro. Como un comercio puede operar con más de un operador ( 3.3), la relación comercio↔operador queda decidida como **muchos a muchos**, modelada mediante una entidad propia, `RelacionComercial` (vincula un comercio con un operador y guarda los datos propios de ese vínculo). La cuenta de usuario del comercio no tiene un único `OperadorId` fijo: tiene una o varias `RelacionComercial` activas, y el comercio elige con cuál operador está trabajando en cada sesión. El detalle de cómo se persiste esa selección de sesión queda para el modelo de dominio (ítem 3 de la entrega del 4/10); la forma de la relación ya está decidida.

**Identidad visual y reglas de negocio por tenant:** una vez resuelto el `OperadorId`, se carga la configuración de ese operador (colores, logo, cuadro tarifario, reglas operativas  RF 3, RF 4, RF 5\) desde la misma base, filtrada igual que el resto. No hace falta ningún mecanismo aparte: es una entidad de configuración más, sujeta al mismo filtro por fila.

**Migraciones y datos de inicialización:** al ser aislamiento por fila sobre una única base de datos, hay **una sola migración compartida** por cambio de esquema — no hay una migración por operador ni por esquema, a diferencia de lo que pasaría con `schema-per-tenant` o `database-per-tenant`. El seed de datos multitenant crea, desde el arranque, **al menos dos operadores** con configuración distinta (colores, cuadro tarifario, reglas operativas), y para cada uno sus propios comercios y relaciones comerciales (`RelacionComercial`) separadas. Así, la demostración del 15/10 ("dos operadores de configuración distinta") no depende de cargar nada a mano el día de la demo: alcanza con correr las migraciones y el seed.

## **3\. Alternativas consideradas y motivo de su descarte**

* **Por esquema (schema-per-tenant)**: cada operador tendría su propio conjunto de tablas dentro de la misma base de datos. Se descartó porque EF Core no tiene soporte cómodo para apuntar dinámicamente a un esquema distinto por request, complica las migraciones (una migración deja de ser "una operación", pasa a ser "una operación por cada esquema existente, incluyendo los que se crean después"), y agregar un operador nuevo requiere crear infraestructura en caliente — mucho más trabajo del que el equipo puede sostener con el tiempo disponible de este laboratorio.  
* **Por base de datos (database-per-tenant)**: cada operador con su propia base de datos completa. Es el aislamiento más fuerte de los tres, pero el más caro operativamente: implica N cadenas de conexión, N migraciones a mantener sincronizadas, y para la demo con "al menos dos operadores" habría que levantar dos bases en el mismo ambiente. Se descartó por complejidad y por costo de infraestructura desproporcionado para el alcance del laboratorio — sería la elección correcta si un operador exigiera aislamiento físico de datos por un requisito legal o contractual real, que no es el caso acá.  
* **Usar una librería de terceros para multitenancy (por ejemplo Finbuckle.MultiTenant)**: se descartó a favor de una implementación propia y simple. La letra evalúa comprensión individual y defensa oral del código propio ( 2,  9.4); apoyarse en una librería resuelve el problema pero deja una caja negra que después es más difícil de explicar y defender frente al docente que un `ICurrentTenant` y un `HasQueryFilter` escritos por el equipo.

## 

## 

## 

## **4\. Consecuencias**

**A favor:** una sola base de datos, un solo conjunto de migraciones, un `docker compose` simple. Demostrar "dos operadores con configuración distinta" es sencillo: alcanza con datos de inicialización que sean 2 filas de Operador con configuración distinta, no infraestructura separada. Es el camino con menor riesgo de no llegar a tiempo al monitoreo del 15/10.

**En contra / riesgos a mitigar:** el aislamiento depende por completo de que **toda** consulta pase por el filtro. Una consulta SQL cruda, un `IgnoreQueryFilters()` mal usado, una entidad nueva a la que alguien se olvide de ponerle el filtro, o una operación que pase por `ExecuteUpdate`/`ExecuteDelete` (que no pasan por el `SaveChangesInterceptor`, ver sección 2\) son fugas de datos reales entre operadores — exactamente lo que la letra pide demostrar que *no* pasa. Mitigación obligatoria, en dos capas:

1. La prueba automatizada de aislamiento que exige la  6.5: sembrar dos operadores, consultar como el operador A, verificar que ninguna fila del operador B aparece nunca  en lecturas y en escrituras.  
2. Una prueba estructural, por reflexión, que recorra todas las entidades del modelo que tienen `OperadorId` y verifique que cada una tiene un `HasQueryFilter` configurado  para que si mañana alguien agrega una entidad nueva y se olvida del filtro, la suite falle sola en vez de depender de que alguien se acuerde de revisarlo a mano.

Ambas pruebas tienen que existir desde el principio y correr en el pipeline, no agregarse al final.

**Qué dificulta a futuro:** si algún día un operador exigiera aislamiento físico de sus datos (por ejemplo, por un contrato o una norma que lo obligue), migrar de "por fila" a "por base de datos" para ese operador puntual no es un cambio incremental  es prácticamente extraer sus datos a una base nueva y reescribir la resolución del tenant. Es una limitación real de esta decisión: el equipo la eligió por ser la adecuada para el tamaño del equipo y el tiempo disponible de este laboratorio, no por ser la mejor opción en abstracto; frente a un requisito de aislamiento físico real, la decisión correcta habría sido `database-per-tenant` desde el principio.

## 

## 

## 

## **5\. Decisiones abiertas conectadas a este ADR**

| Tema | Por qué importa |
| ----- | ----- |
| Qué pasa con reportes/consultas administrativas que sí necesitan ver todos los operadores (si las hubiera) | Tienen que ser un camino explícito y auditado, nunca el comportamiento por defecto. Mientras no exista ese caso de uso en el alcance actual, no hay excepción adicional a `IgnoreQueryFilters()` más allá de las dos de la sección 2 (seed y pruebas). |

