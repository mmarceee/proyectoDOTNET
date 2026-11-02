# Propuesta de diseño · API pública para comercios (CU-80 a CU-83)

| Dato | Valor |
| --- | --- |
| Fecha | 02/11/2026 |
| Responsable previsto | Lucas Ottonello |
| Estado | Propuesta para decidir con Lucas; no registra aceptación del equipo ni implementación. |
| Base inspeccionada | `337b222`; revisión estática, sin ejecución de pruebas. |
| Objetivo | Opcional 7.4 completo, demostrable el 12/11. No se declara obtenido ningún punto. |

## 1. Fuentes y precedencia

- [Casos de uso, sección 2.7](casos-de-uso.md): comportamiento de CU-80 a CU-83 y decisión de pruebas mediante `EsPrueba` en sección 3.
- [Plan por hito, sección 4](plan-casos-de-uso-por-hito.md): las cinco condiciones son obligatorias; una implementación parcial no suma.
- [ADR-0001](ADR-0001-estilo-arquitectonico.md): monolito modular, vertical slices, Contracts entre módulos y único host web.
- [ADR-0002, secciones 2.4 y 2.5](ADR-0002-estrategia-de-multitenancy.md): clave por relación comercial, búsqueda excepcional por hash, claims, filtros y escrituras protegidas.
- [ADR-0003](ADR-0003-outbox-y-mensajeria.md): eventos por Outbox, Worker e idempotencia de consumidores.
- [ADR-0004](ADR-0004-resolucion-de-conflictos-de-sincronizacion.md): la idempotencia offline existente tiene otro identificador y alcance; no reemplaza la referencia comercial de CU-81.
- [ADR-0005](ADR-0005-modo-de-renderizado-de-blazor.md): Portal en Blazor WebAssembly; el registro conserva estado Propuesto.
- [ADR-0006](ADR-0006-estrategia-de-cache.md): caché e invalidación; no usarla para demorar la revocación de credenciales.
- [Revisión del cambio anunciado para el 29](revision-cambio-requerimientos-y-aclaraciones.md): contexto, aclaraciones y evidencia histórica. El documento docente fecha el cambio el 30/10.
- [Decisión cerrada de entrega parcial](decisiones-entrega-parcial.md) y [ADR-0007](ADR-0007-adaptacion-a-entrega-parcial.md): prevalecen sobre el modelo histórico del estado del envío. Diseño aceptado, implementación pendiente.

Esta propuesta desarrolla el alcance ya documentado. Los valores operativos, rutas auxiliares y políticas nuevas que aparecen a continuación son recomendaciones, no requisitos docentes adicionales.

## 2. Qué existe y qué falta

| Pieza | Hallazgo en el código inspeccionado |
| --- | --- |
| Aislamiento | `ModuleDbContext`, interceptor y tenant desde claims reutilizables. Falta conectar autenticación por clave. |
| CU-80 | No se encontraron entidad `ClaveApi`, persistencia, autenticación por clave ni pantalla de gestión. |
| Alta | `CrearEnvioHandler` ya valida reglas, cobertura, tarifas y relación activa; guarda envío y evento por Outbox. |
| Idempotencia comercial | El agregado y el command de alta no contienen referencia comercial. La referencia de la dirección es otra cosa. Falta persistencia y unicidad para CU-81. |
| Pruebas | No se encontró `EsPrueba` en el código fuente. Falta aislamiento de consultas, procesos y efectos externos. |
| Consultas | Hay listado y detalle reutilizables, pero sus DTOs internos incluyen GUID de comercio, bulto y responsable. No sirven directamente como contrato público. |
| Errores | El tratamiento general de `DomainException` devuelve 400. CU-81 requiere distinguir 400 de 422. |
| Límites | `Program.cs` tiene límite local por IP para seguimiento público. No implementa límites de la API por ambiente ni `Retry-After` específico de este alcance. |
| Documentación | No se encontró configuración OpenAPI ni interfaz navegable de la API pública. |
| Entrega parcial | No asumirla implementada: el código inspeccionado mantiene estado operativo por envío y el diseño nuevo exige adaptar consultas. |

## 3. CU-80: decisiones recomendadas

1. **Propiedad:** Administración posee `ClaveApi`, asociada a `RelacionComercial`, con `OperadorId` y `ComercioId` y ambos marcadores de aislamiento. Los endpoints de gestión usan la cookie y el perfil UsuarioComercio; requieren operador seleccionado. La autenticación por clave vive en `Logistica.Api` y consulta Administración mediante Contracts, sin acceder a su DbContext.
2. **Secreto:** generar 32 bytes aleatorios criptográficos y un prefijo visible de ambiente; guardar hash SHA-256, prefijo y metadatos, nunca el secreto recuperable. Índice único por hash. El secreto sólo sale en la respuesta de creación; ni logs ni almacenamiento persistente del navegador deben conservarlo. El prefijo no autoriza ni determina por sí solo el tipo: vale el registro encontrado.
3. **Pantalla:** nombre, tipo, prefijo, creación, último uso y estado/revocación. Mostrar el secreto una vez, con copia; confirmar revocación. La rotación consiste en crear otra clave y después revocar la anterior, permitiendo solapamiento.
4. **Revocación:** lógica e irreversible; repetir la operación conserva el resultado. Consultar vigencia en la base en cada autenticación, sin caché positiva. Una solicitud autenticada después de confirmar la revocación debe recibir 401; una operación ya admitida puede terminar. Si se pretende cancelar también las operaciones en curso, es una exigencia adicional a especificar.
5. **Relación suspendida:** proponer impedir nuevas claves y el acceso público mientras esté suspendida, permitiendo listar y revocar desde el Portal. No eliminar credenciales automáticamente; al reactivar, sólo las no revocadas vuelven a ser utilizables. Esta política amplía lo explícito en CU-80 y debe confirmarse.
6. **Último uso:** después de fijar tenant, con entidad rastreada y `SaveChanges`, como exige ADR-0002. Actualizar sólo ese campo y proteger concurrencia para que nunca deshaga una revocación ni retroceda la fecha.

La búsqueda previa al tenant sólo ignora el filtro `Tenant`, es sin tracking y está encapsulada en el camino de autenticación autorizado. El control posterior de relación activa se realiza ya con tenant. Las credenciales de API no dan acceso a las pantallas ni a los endpoints internos que requieren cookie, y una cookie no sustituye la clave en `/api/v1`.

## 4. Ambiente de pruebas y documentación ejecutable

Mantener la decisión existente: mismo despliegue y base, envíos marcados con `EsPrueba`. El ambiente se toma de la credencial autenticada; el body, los parámetros y los headers arbitrarios no pueden cambiarlo.

- Clave de producción ve sólo producción; clave de prueba ve sólo pruebas. Portal operativo, Backoffice, depósito, rutas, reportes y liquidaciones excluyen pruebas. Verificar también consultas de bultos, SQL y procesos automáticos que no atraviesen el filtro del envío.
- Filtro de ambiente separado de `Tenant`, con selección explícita del ambiente permitido. Nunca ignorar `Tenant` para acceder a pruebas. Ampliar esta regla a proyecciones o datos derivados que lo requieran.
- Propagar el carácter de prueba de forma durable a eventos/proyecciones y reintentos. El Worker puede generar avisos al sistema del comercio, pero no correos al destinatario ni efectos financieros u operativos reales. No cambiar silenciosamente la semántica de eventos existentes.
- El enlace de seguimiento de prueba también necesita aislamiento: no debe abrir el seguimiento público real. Resolverlo con una consulta de prueba autenticada y documentada antes de cerrar CU-81.
- La simulación de visitas no forma parte explícita de CU-80 a CU-83. No agregar un motor de simulación como condición inventada; usar fixtures para verificar parcial y avisos. Acordar si se desea simulación manual como ampliación.

**CU-83 tiene una dificultad concreta:** rechazar claves de producción sólo con JavaScript no garantiza el rechazo del servidor; tampoco sirve confiar en `Origin`, `Referer` o un header que el cliente pueda omitir.

Propuesta: documentación en `/api/docs`, especificación pública en `/api/openapi/v1.json` y ejecución interactiva contra un grupo de pruebas, por ejemplo `/api/v1/pruebas/envios`, que exija claves de prueba en el servidor y reutilice los mismos casos de uso. `/api/v1/envios` conserva el comportamiento por tipo de clave ya previsto. El cliente de documentación sólo configura el grupo de pruebas como destino y no persiste la credencial. Detallar las rutas definitivas y comprobar el rechazo de producción antes de aceptar CU-83.

## 5. CU-81: contrato e idempotencia

- `POST /api/v1/envios`, header recomendado `X-Api-Key`. Operador, comercio, origen API y ambiente se obtienen en el servidor; no se aceptan como autoridad desde el body.
- Reutilizar `CrearEnvioHandler` y sus reglas, ampliando el caso de uso para referencia y ambiente. No hacer una llamada HTTP al endpoint interno ni copiar reglas en el endpoint público.
- Referencia obligatoria, recortada, con longitud y comparación documentadas. Propuesta inicial: distinguir mayúsculas y minúsculas. Unicidad en base por relación comercial y ambiente, para que una referencia de prueba no bloquee producción. Formalizar esta extensión de alcance en CU-10/11/81 y el modelo.
- Buscar el envío existente antes de recalcular tarifas o exigir reglas actuales de admisión, una vez autenticada y autorizada la solicitud. Repetir una referencia devuelve 200 con el recurso existente, sin modificarlo, aunque cambie el resto del body: es la semántica documentada actualmente, no se introduce 409 sin cambiarla.
- Resolver también solicitudes simultáneas: restricción única y tratamiento del conflicto en una transacción válida; una consulta previa por sí sola no alcanza. Deben quedar un envío y un evento de admisión.
- Nuevo: 201 con número, tarifa, resumen, referencia y enlace; existente: 200. `Location` identifica el número público. Clave inválida/revocada: 401; validación estructural: 400; cobertura/tarifa: 422; tasa: 429 con `Retry-After`.
- Diferenciar errores por códigos tipados o resultados de aplicación, no por comparación de textos de excepciones. Mantener coherencia con los consumidores actuales del handler.

## 6. CU-82 y cambio de entrega parcial

Publicar listado paginado con filtros por resumen, fechas y referencia, y detalle por número. Mantener 404 para recurso inexistente, otro operador, otro comercio u otro ambiente.

Definir DTOs públicos propios, sin entidades EF ni GUID internos. Identificar bultos por su código público y filtrar el historial: no publicar automáticamente responsable interno, coordenadas, notas o referencias a evidencia sólo porque el DTO del Backoffice las contiene.

El contrato v1 nuevo debe nacer con el diseño aceptado del ADR-0007:

- `estado`: Creado, En operación, Entregado parcialmente o Entregado, con valores serializados estables por concretar.
- `terminado`, `entregados`, `total` y desglose de resultados.
- Detalle por bulto e historial relevante; expresar Finalizado sin entrega mediante el cierre y desglose acordados, sin inventar un quinto estado persistido.
- De 1/3 a 2/3 entregados debe cambiar la respuesta aunque el resumen siga siendo parcial. Si se cachean consultas, la invalidación debe cubrir ese hecho.

No trasladar directamente el enum histórico al contrato público para cambiarlo después. Acordar la proyección con Cristian, propietario del dominio, y los hechos/historial con Ezequiel. El versionado HTTP `/api/v1` y las versiones v1/v2 de avisos son contratos distintos; coordinar semántica sin confundir sus números.

## 7. Limitación de tasa

Propuesta inicial configurable, pendiente de validar con el uso esperado: producción 120 solicitudes/minuto y pruebas 30 solicitudes/minuto, con cuota compartida por relación comercial y tipo. Así crear más claves no multiplica la cuota. Documentar si se cambia a cuota por clave.

Aplicar además un límite por IP antes de consultar credenciales para frenar intentos inválidos, sin crear particiones ilimitadas por cada secreto enviado. La cuota autenticada corre después de autenticar. Ambos rechazos devuelven 429 y `Retry-After`.

Para la entrega con una instancia puede usarse contador local, dejando explícito su alcance; el análisis previo no exige balanceo al equipo de tres. Con varias réplicas se necesita contador compartido o un punto de entrada que imponga una cuota global: no multiplicar silenciosamente los límites publicados. La estrategia distribuida y el comportamiento ante caída se decidirán si ése es el despliegue real, sin confundir disponibilidad de caché con autorización.

## 8. Orden de trabajo y evidencia de cierre

1. Acordar contrato público, alcance de idempotencia, política de relación suspendida y ejecución de pruebas de CU-83; registrar la decisión de arquitectura cuando se acepte. Esta propuesta no se autoproclama ADR aceptado.
2. Administración + Portal + autenticación: CU-80 completo y pruebas de aislamiento/revocación. Se puede avanzar sin esperar la implementación de parcial.
3. Ambiente de pruebas de extremo a extremo, incluido Outbox, Worker, enlaces y exclusiones operativas.
4. CU-81 con idempotencia concurrente y adaptación común del alta. Coordinar cambios del agregado/migraciones con Cristian.
5. CU-82 con proyección de parcial acordada; CU-83 se redacta desde el contrato y se verifica al integrar, no se deja para el último día.
6. Antes del 12/11, ejecutar demostración integrada y registrar resultados reales. No hay estimación de horas validada en esta revisión.

| Condición obligatoria | Evidencia mínima para declarar completo |
| --- | --- |
| API documentada y versionada | POST y GET públicos en `/api/v1`; contratos y todos los códigos de respuesta presentes en OpenAPI. |
| Claves por comercio | Dos operadores y dos comercios: listado, alta, revocación, acceso por clave y 404 ajenos; secreto sólo una vez; revocación y uso concurrentes sin reactivar la clave. |
| Ambiente de pruebas | Misma referencia en ambos ambientes sin colisión; ninguna lectura cruzada; pruebas ausentes en rutas/liquidaciones/Backoffice; Worker sin correo al destinatario y con aviso al comercio, también al reintentar. |
| Tasa diferenciada | Cuotas distintas comprobadas, 429 con espera, intentos inválidos limitados y rotación de claves sin multiplicar cuota. Aclarar número de instancias probado. |
| Documentación navegable | Acceso anónimo, sólo operaciones públicas, ejemplos ejecutables con prueba, producción rechazada por el servidor en el destino interactivo. |
| Coherencia con RF 31 a 36 | Consulta de 2/3 entregados y posterior cierre mixto: resumen, Terminado, total y estados de bulto correctos, sin falsas entregas completas. |

Además: dos POST simultáneos de la misma referencia crean un solo envío y un solo evento; errores 400/422 diferenciados; cookie sola rechazada en API pública y clave sola rechazada en gestión del Portal.

## 9. Decisiones que conviene cerrar en la conversación

La recomendación es mantener el opcional completo, implementar primero CU-80 y el aislamiento de pruebas, y diseñar ahora CU-82 con el resumen nuevo. La mayor dependencia no está en generar el secreto: está en evitar efectos reales de pruebas y estabilizar el contrato por bulto.

Quedan para aceptación de Lucas las propuestas nuevas de este documento, especialmente: suspensión bloquea toda la API pero permite listar/revocar; referencia única por relación y ambiente; grupo de pruebas impuesto por el servidor para documentación; límites iniciales configurables. Ninguna se presenta como acuerdo ya tomado por el equipo.
