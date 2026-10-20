# CU-63 · Configurar las suscripciones de avisos

Referencias: [caso de uso](../casos-de-uso.md#cu-63--configurar-las-suscripciones-de-avisos),
[modelo de dominio](../modelo-de-dominio.md) y [multitenancy](../ADR-0002-estrategia-de-multitenancy.md).

## Primer paso: dominio

Se incorpora `SuscripcionAviso` en Seguimiento como raíz de agregado. Conserva los atributos del modelo:
`RelacionComercialId`, `UrlDestino`, `TiposEvento`, `SecretoFirma` y `Activa`, además de `Id`, `OperadorId`
y `ComercioId` según las convenciones generales. Referencia la relación comercial por identificador,
sin depender del dominio de Administración.

La creación exige identificadores no vacíos, una URL HTTPS absoluta, al menos un tipo de evento válido
y un secreto no vacío. Nace activa. Los tipos son `CambioDeEstado`, `EntregaFallida` y `Devolucion`;
se conservan sin duplicados y no se pueden modificar desde afuera. Desactivar conserva la configuración;
regenerar sustituye el secreto sin modificar la activación.

El dominio recibe el secreto ya cifrado. Generarlo, cifrarlo y resolver la relación comercial pertenecen
a la aplicación mediante contratos de infraestructura y Administración. La entidad no verifica que una
cadena sea un cifrado válido ni expone una operación para descifrarla.

Este primer paso incluye la entidad, el enumerado y sus pruebas. No se agregan edición, reactivación
ni eliminación, que no figuran en CU-63.

## Segundo paso: persistencia y relación comercial

Se incorpora la tabla `seguimiento.suscripciones_aviso` mediante la migración `SuscripcionesAviso`.
Los tipos de evento se guardan como un arreglo de enteros de PostgreSQL, usando el campo privado de la
entidad para materializarlos. Los índices cubren operador, comercio y activación, y la referencia a la
relación comercial. No hay unicidad por dirección ni navegación o clave foránea hacia otro módulo.

`ISuscripcionesAvisoRepository` permite agregar, recuperar, listar y guardar. Las consultas usan el filtro
de inquilino de `ModuleDbContext`; las escrituras pasan por `TenantSaveChangesInterceptor`. El repositorio
trabaja con entidades internas, incluido el secreto cifrado: las respuestas públicas deben
proyectar sólo los datos permitidos, sin serializar directamente esas entidades.

`IAdministracionModuleApi.ObtenerRelacionComercialActualAsync` devuelve el identificador del vínculo
del comercio autenticado con el operador seleccionado. No recibe identificadores del cliente y devuelve
`null` si falta el operador, el comercio o la relación. Es una consulta de identidad del vínculo: no agrega
una condición de relación activa al CU-63 ni reemplaza la autorización del endpoint.

Las pruebas de integración aplican las migraciones sobre PostgreSQL y verifican recuperación de los
eventos, actualización de secreto y activación, aislamiento en lista y detalle, rechazo de escrituras
ajenas y resolución del vínculo para los dos operadores de demostración.

La integración de esta consulta con la creación de la suscripción corresponde al paso siguiente.

## Tercer paso: generación, cifrado y flujos de aplicación

`CrearSuscripcionAvisoHandler` toma operador y comercio de `ICurrentTenant`, obtiene el vínculo desde
Administración y crea la suscripción dentro de la unidad de trabajo. `RegenerarSecretoAvisoHandler`
recupera la suscripción mediante el repositorio filtrado, reemplaza el secreto y mantiene la activación.
Devuelve `null` para una suscripción inexistente o ajena. Ambos exigen una sesión con operador y comercio.

El adaptador `SecretosAviso`, detrás de `ISecretosAviso`, genera 32 bytes aleatorios con
`RandomNumberGenerator` y los entrega en Base64 estándar. Ésta es la codificación del secreto que recibirá
el comercio: para HMAC se usarán los 32 bytes decodificados, no los caracteres del Base64.

Se guarda únicamente su cifrado autenticado AES-256-GCM. Cada operación genera un nonce aleatorio de
12 bytes y una etiqueta de autenticación de 16 bytes. El formato de almacenamiento es
`v1.` seguido del Base64 de `nonce || secreto cifrado || etiqueta`. Los datos asociados autentican el
propósito `Logistica.Avisos.v1` y los identificadores de operador y comercio: mover el cifrado a otro
inquilino impide descifrarlo. La clave maestra nunca se guarda en la tabla.

Creación y regeneración devuelven `SecretoSuscripcionAviso`, que contiene identificador y secreto en claro,
sólo después de que termine la unidad de trabajo. Un error al guardar o confirmar impide devolver ese
resultado y revierte los cambios. El resultado no tiene un `ToString` que incluya el secreto. No existe
una consulta de aplicación para volver a mostrarlo; el descifrado interno queda disponible para el futuro
emisor. La API añade `Cache-Control: no-store` y no registra estos resultados.

El cifrado guardado actúa también como token de concurrencia de EF Core. Una regeneración con un valor
anterior falla con `DbUpdateConcurrencyException`, que el host ya convierte en HTTP 409. La migración
`ConcurrenciaSecretoAviso` actualiza los metadatos sin cambiar columnas. No se modifica el agregado para
introducir un atributo de versión ajeno al modelo.

### Configuración compartida

- Clave: `Seguimiento:Avisos:ClaveCifrado`, exactamente 32 bytes en Base64.
- Docker Compose: `AVISOS_CLAVE_CIFRADO` en `.env`, compartida por API y Worker.
- Visual Studio: `scripts/configurar-secretos.sh` copia ese valor a los user-secrets compartidos.
- Generar una clave exclusiva con `openssl rand -base64 32`; no reutilizar la del seguimiento público.

Se valida al cifrar o descifrar, para que los demás casos de uso arranquen sin ella. Ausencia, Base64
inválido o longitud incorrecta impiden guardar el nuevo secreto con un mensaje que indica qué configurar.
Los despliegues y reinicios deben conservar la misma clave. Debe guardarse y respaldarse fuera de la base
de datos y del repositorio, con acceso limitado a API y Worker. Reemplazarla sin migrar los cifrados
existentes los vuelve ilegibles; la rotación de la clave maestra no está implementada. Regenerar el secreto
de una suscripción es una operación distinta y sí está implementada.

Las pruebas verifican descifrado desde otra instancia con la misma clave, rechazo de claves distintas,
alteraciones y cambios de inquilino; creación y regeneración sobre PostgreSQL, reversión ante fallas,
concurrencia y rechazo de suscripciones ajenas.

## Cuarto paso: consultas, desactivación y API

`ConsultarSuscripcionesAvisoHandler` devuelve vistas explícitas sin secreto. Su lector proyecta únicamente
identificador, dirección, eventos y activación: no selecciona el cifrado. `DesactivarSuscripcionAvisoHandler`
guarda la desactivación en la unidad de trabajo y conserva la configuración; repetirla es idempotente.
Ambos requieren operador y comercio en el contexto y usan los filtros de inquilino existentes.

Todas las rutas están bajo `/api/avisos/suscripciones`:

| Método | Sufijo | Resultado |
| --- | --- | --- |
| GET | vacío | 200 con la lista, sin secretos |
| GET | `/{id}` | 200 con el detalle, sin secreto; 404 si no existe o es ajena |
| POST | vacío | 201 con `id` y `secretoFirma`; `Location` apunta al detalle |
| POST | `/{id}/desactivacion` | 204; 404 si no existe o es ajena |
| POST | `/{id}/secreto` | 200 con `id` y el nuevo `secretoFirma`; 404 si no existe o es ajena |
| POST | `/{id}/prueba` | 200 con identificador, éxito, código HTTP o error; 404 si no existe o es ajena; 400 si está inactiva |
| GET | `/antifalsificacion` | 200 con `token` para las escrituras con cookie |

El alta recibe `urlDestino` y `tiposEvento` (nombres exactos: `CambioDeEstado`, `EntregaFallida`,
`Devolucion`). Cuerpos incompletos, valores nulos, eventos vacíos/desconocidos y direcciones no HTTPS
responden 400. Los identificadores de inquilino, relación comercial, activación y secreto no forman parte
del contrato de entrada; enviarlos no permite modificar los valores resueltos por el servidor.

Se exige autenticación real, política `Comercio`, operador seleccionado y claim de comercio. El respaldo
provisorio de desarrollo no habilita estas rutas. La respuesta es 401 sin sesión y 403 para un perfil
incorrecto o un comercio sin operador seleccionado. Las respuestas de los endpoints llevan
`Cache-Control: no-store`, especialmente las que contienen el secreto de creación o regeneración.

Las escrituras POST validan antifalsificación. El cliente obtiene primero `/antifalsificacion`, conserva
la cookie y manda el valor de `token` en la cabecera `RequestVerificationToken`. Debe obtener un token
después del ingreso o cambio de sesión. Token ausente o inválido responde 400 sin modificar datos.

Los DTO HTTP están en `Logistica.Http.Contracts/Avisos`, sin dependencias del servidor. Los tipos de evento
viajan como cadenas, y las respuestas de consulta no tienen propiedad de secreto. Crear y regenerar usan
una clase específica sin `ToString` que incluya el valor sensible.

## Quinto paso: Portal del comercio

La pantalla `/portal/integraciones/avisos` se accede desde «Integraciones» en la navegación y el inicio
del Portal; `/portal/avisos` se conserva como acceso compatible. Permite consultar las
suscripciones, crear una con URL HTTPS y uno o varios eventos, desactivar y regenerar el secreto.
No incorpora edición, reactivación ni eliminación. La desactivación y la regeneración piden una
confirmación en pantalla; regenerar informa que el secreto anterior deja de ser válido inmediatamente.

El secreto se conserva únicamente en memoria del componente tras crear o regenerar, con instrucciones
de verificación de firma. No se escribe en almacenamiento del navegador, no se incluye en direcciones
y no se recupera desde consultas. «Ya guardé la clave» lo quita de la pantalla y del estado del
componente. Navegar fuera también descarta la referencia y cancela peticiones en curso. La lista puede
actualizarse sin borrar el secreto recién recibido, incluso si falla la recarga posterior a un cambio.

Cada escritura obtiene un token antifalsificación y lo envía sólo en su petición, sin modificar las
cabeceras compartidas de `HttpClient`. Se bloquean acciones duplicadas mientras se guarda y nuevas
mutaciones mientras está visible el secreto. Los errores de validación se muestran en español; una
sesión vencida lleva al ingreso y un 403 ofrece elegir operador. Ante un resultado de red incierto se
indica actualizar la lista antes de repetir una mutación, sin reintentos automáticos.

La validación del formulario replica HTTPS y la selección mínima para avisar antes del envío; las reglas
definitivas siguen estando en el servidor. La interfaz usa los componentes y estilos Bootstrap existentes,
con etiquetas de formulario, mensajes accesibles y una tabla adaptable mediante desplazamiento horizontal.
«Enviar prueba» está disponible en suscripciones activas; el resultado aparece en la misma pantalla.

La interfaz presenta el secreto como «clave de firma», sin cambiar el modelo ni el contrato HTTP.
El listado ofrece «Configurar seguridad», que abre la explicación de la firma y la desactivación.
Cambiar la clave es una acción secundaria dentro del desplegable «Cambiar la clave de firma», con
motivos de uso y una confirmación que explica la invalidez inmediata de la clave anterior y la necesidad
de actualizar el sistema receptor. Se verificó en navegador el acceso, el alta, la configuración, el
despliegue de la opción y la cancelación de la confirmación; la compilación Release no presenta advertencias.

Verificación del Portal: compilación Release sin advertencias y recorrido en navegador con PostgreSQL
desechable, sesión real del comercio y operador seleccionado. Se comprobó alta, aparición del secreto,
retiro del campo al confirmar que se guardó, confirmación y ejecución de regeneración y desactivación,
y persistencia del estado inactivo tras recargar sin que reaparezca el secreto. El entorno temporal se
detuvo al terminar; no se modificaron los datos de desarrollo.

## Sexto paso: enviar prueba y cierre funcional de CU-63

`EnviarPruebaAvisoHandler` verifica pertenencia y activación, descifra el secreto y envía un mensaje
ficticio mediante `IEmisorAvisos`. Devuelve un identificador nuevo, éxito, código HTTP cuando existe
y error acotado. No crea envíos, entregas de historial ni mensajes de cola. No reintenta automáticamente.
Una prueba fallida conserva la suscripción y su activación.

El emisor serializa una sola vez, firma esos bytes con HMAC-SHA256 y envía un POST HTTPS con
`X-Aviso-Fecha` y `X-Aviso-Firma`. Considera exitoso cualquier 2xx, informa otros códigos y limita la
espera a 10 segundos. No lee el cuerpo de respuesta ni sigue redirecciones; tampoco utiliza proxies o cookies.
El conector rechaza destinos internos o reservados, valida todas las IP de la resolución DNS y se
conecta a una IP ya validada manteniendo la validación TLS del host original.

El Portal obtiene el token antifalsificación, bloquea dobles envíos y muestra el resultado con la URL
receptora y el identificador de la prueba. Las suscripciones inactivas tienen el botón deshabilitado.
Si se pierde la respuesta no se vuelve a enviar automáticamente.

La [guía de integración para Ezequiel](integracion-cu64.md) fija el contrato implementado y detalla las
piezas reutilizables, un vector de firma independiente y las responsabilidades pendientes de CU-64.
La entrega automática, sus reintentos, cola de fallidos e historial no forman parte de CU-63.

Pruebas de cierre: unitarias de firma, tiempo de espera, cancelación, códigos HTTP y bloqueo de destinos;
integración con sesiones y PostgreSQL que usa un receptor en memoria para verificar el HMAC y simular
éxito, error, redirección, desconexión y demora. El receptor de demostración desplegado para la defensa
es una integración del equipo todavía pendiente, separada de la funcionalidad de configuración y prueba
manual ya implementada aquí.

Verificación de cierre: 83 pruebas unitarias de Seguimiento, 28 de integración de CU-63 y 56 de
arquitectura aprobadas. En navegador, con datos desechables, se comprobó el botón y la presentación
del error esperado para un destino interno bloqueado, conservando la suscripción activa. Los escenarios
exitosos y con códigos HTTP se verifican mediante el receptor automatizado independiente.
