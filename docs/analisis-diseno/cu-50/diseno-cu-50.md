# Diseño de CU-50 · Descargar la hoja de ruta

Fecha: 16/10/2026. Implementación prevista: 17/10/2026.
Estado: diseño implementado el 17/10/2026. Evidencia y ajustes finales en [README](README.md).

## 1. Decisión de alcance

El repartidor autenticado descarga la hoja de ruta de su día operativo, ve sus paradas y los detalles necesarios, y puede volver a abrirla sin conexión. No se cambian estados de rutas ni envíos al consultar o descargar. Ampliación acordada el 19/10/2026: desde Mis rutas también puede seleccionar una ruta propia pendiente de días anteriores, conservando las hojas por separado.

**Decisión del usuario:** usar las reglas de entrega actuales que valida el servidor, preparando su sustitución por las reglas versionadas de CU-06. CU-50 no implementa la gestión ni el versionado de CU-06 y no se presentará como prueba de que esa dependencia está completa.

Referencias:
- [CU-50, CU-51 y CU-52](../casos-de-uso.md).
- [ADR-0001](../ADR-0001-estilo-arquitectonico.md): comunicación entre módulos por Contracts.
- [ADR-0002](../ADR-0002-estrategia-de-multitenancy.md): identidad y tenant desde sesión, acceso sólo a la ruta propia.
- [ADR-0004](../ADR-0004-resolucion-de-conflictos-de-sincronizacion.md): servidor como autoridad; la PWA registra operaciones, no impone estados.
- [ADR-0005](../ADR-0005-modo-de-renderizado-de-blazor.md): Blazor WebAssembly y comprobación de cierre/reapertura sin conexión.
- [CU-43](../cu-43/README.md): el aviso interno y la descarga son capacidades distintas.

## 2. Base existente y dependencias verificadas

- PWA Blazor WebAssembly en /repartidor/, con barra superior, avisos de CU-43 y service worker publicado.
- Claims operador_id y repartidor_id, identificador del usuario y política Politicas.Repartidor.
- Planificación conserva Ruta.Revision, fecha, repartidor, vehículo y paradas ordenadas que pueden agrupar varios envíos.
- El índice de reserva impide dos rutas activas del mismo repartidor, operador y fecha. No se elige arbitrariamente la primera si aparece una inconsistencia.
- Ejecución ya recibe lotes de CU-52 en el servidor. La cola y los disparadores en la PWA siguen perteneciendo a CU-52.
- Envíos valida CU-17 y CU-18 mediante IReglasDeEntrega, con ReglasDeEntregaProvisorias. Actualmente no hay una versión real de reglas operativas vinculada a cada envío.

Reglas actuales:
- Firma siempre obligatoria.
- Foto de entrega y nombre/documento del receptor no exigidos.
- Tres intentos máximos; 24 horas entre intentos.
- AUSENTE: reprogramable, sin evidencia adicional.
- SIN_ACCESO: reprogramable, con evidencia.
- DIRECCION_INEXISTENTE y RECHAZADO: no reprogramables, sin evidencia adicional.

CU-50 debe reutilizar este proveedor y su catálogo. No duplicará valores o identificadores de motivos en el cliente. Estas reglas no sustituyen las reglas versionadas futuras de CU-06.

## 3. Pantallas y navegación

El inicio es Mis rutas: tarjetas por fecha y vehículo, con estado actual y acciones por ruta. La hoja se abre en /repartidor/hoja-de-ruta?rutaId={id}. Los avisos internos de CU-43 y su procesamiento se conservan; las tarjetas consultan las rutas actuales sin depender de que se haya procesado el aviso.

Al abrir Mi ruta:
1. Recuperar la identidad local previamente verificada y su copia cifrada, si existe.
2. Con red, verificar la sesión y consultar la hoja del día. No confiar en navigator.onLine como prueba de que la API está disponible.
3. Validar el contrato recibido y guardarlo antes de anunciar que está disponible sin conexión.
4. Mostrar la hoja nueva o, si falló la actualización, conservar la copia anterior e indicar el problema.
5. Sin red, mostrar sólo la copia de la identidad local activa. Nunca mezclar usuarios, operadores o repartidores.

Encabezado:
- Ruta del dd/MM/yyyy, estado, zona y matrícula del vehículo.
- Cantidad de paradas y envíos.
- Última descarga confirmada y aviso de modo sin conexión.
- Botón Actualizar descarga, bloqueado mientras haya otra actualización en curso.
- Los GUID quedan para contratos y navegación interna; no se muestran como nombre de ruta.

Listado:
- Paradas por Orden, con dirección, franja cuando exista y cantidad de envíos.
- Cada parada se puede desplegar para ver sus envíos.
- Por envío: número, destinatario, teléfono, dirección/referencia, franja propia, códigos de sus bultos y estado informado por el servidor.
- El teléfono puede ser un enlace tel:. La dirección se muestra como texto; no se integra un proveedor de mapas en CU-50.
- Reglas y motivos viajan en el contrato para CU-51/CU-17/CU-18, sin botones operativos ficticios en esta entrega.
- Varios envíos en una parada conservan su identidad y sus propias reglas/franjas; no se reemplazan por los datos del primer envío.

Mis rutas ofrece Ver ruta y Cargar vehículo para Despachada; para EnCurso agrega Rendir ruta y, si faltan envíos por cargar, Continuar carga. Una declaración pendiente ofrece sincronizar; una registrada indica que el recorrido terminó y espera recepción del depósito. Las rutas finalizadas se muestran como resumen sin acciones operativas, y las futuras esperan su fecha.

## 4. Selección del día y estados

El servidor calcula el día con TimeProvider y la zona horaria del operador. No usa DateTime.Today del host ni la fecha del celular para elegir una ruta.

Se devuelve la única ruta del repartidor para ese día en estado Despachada o EnCurso:
- Despachada: primera descarga, lista para la carga de CU-51.
- EnCurso: permite recuperar/actualizar la misma hoja durante la jornada.
- Planificada o Finalizada: no se ofrece como hoja activa.

La consulta predeterminada sigue devolviendo la ruta de hoy. Dos rutas elegibles producen un error de integridad, no una selección silenciosa. La consulta explícita por ID admite también rutas anteriores Despachada/EnCurso asignadas al repartidor autenticado. No acepta operador ni repartidor del navegador y rechaza las futuras, planificadas, finalizadas y ajenas.

Descargar no aplica T5, no inicia ni finaliza la ruta, no marca un aviso como leído y no genera un evento Outbox.

Las copias muestran su fecha y, sin red, se identifican como información guardada pendiente de verificar. Una ruta anterior que continúa abierta puede retomarse desde su tarjeta; la antigüedad no equivale a cierre. La cola conserva las operaciones por ruta y el servidor valida su estado al sincronizar.

## 5. Contratos HTTP

GET /api/repartidor/sesion
- Política Repartidor.
- 200: UsuarioId, OperadorId, RepartidorId, FechaOperativa, ZonaHorariaOperador y VerificadaEn UTC.
- 401 sin sesión; 403 por perfil o vínculo inválido.
- No devuelve contraseña, cookie, token de acceso ni datos de destinatarios.
- Se consulta para establecer el espacio local incluso cuando no hay ruta.

GET /api/repartidor/hoja-de-ruta
- Política Repartidor; usuario, operador y repartidor salen exclusivamente de la sesión.
- 200: hoja completa.
- 204: no hay ruta elegible para hoy.
- 401: iniciar sesión de nuevo.
- 403: el perfil o vínculo no habilitan la PWA.
- 503 con ProblemDetails: no se puede construir una hoja completa por datos o reglas faltantes. No devolver una descarga parcial como exitosa.

GET /api/repartidor/rutas?pagina=1 devuelve la fecha operativa y hasta 20 resúmenes de rutas propias, primero abiertas por fecha y luego finalizadas de más reciente a más antigua. Incluye matrícula, cantidad de paradas/envíos y los indicadores de carga/rendición actuales.

GET /api/repartidor/rutas/{rutaId}/hoja-de-ruta devuelve la hoja completa de una ruta propia abierta cuya fecha ya llegó; responde 204 cuando no está disponible, sin revelar rutas ajenas. Conserva los demás controles y errores de la descarga predeterminada.

Todas estas respuestas usan Cache-Control: no-store. No se guardan respuestas personales en Cache Storage ni localStorage. El único almacenamiento durable de la hoja es el repositorio cifrado.

No se implementa ETag/304 en esta primera versión: Ruta.Revision sólo describe la estructura de la ruta, no todos los cambios posteriores de los envíos. Cada actualización válida consulta de nuevo el contenido completo.

## 6. Contenido de HojaDeRutaResponse

- SchemaVersion = 1.
- UsuarioId, OperadorId, RepartidorId: identifican el espacio local; nunca amplían permisos.
- RutaId, RevisionRuta, FechaRuta, FechaOperativa, ZonaHorariaOperador, EstadoRuta, GeneradaEn UTC.
- Zonas[]: Id, Codigo, Nombre. Ajuste de implementación: una ruta puede reunir envíos de varias zonas; el modelo no guarda una zona única en Ruta.
- Vehiculo: Id, Matricula.
- Paradas[]: Id, Orden, Estado, Direccion, Desde/Hasta opcionales, Envios[].
- Envio[]: Id, Numero, Estado, DestinatarioNombre, DestinatarioTelefono.
- Direccion: Calle, Numero, Localidad, Departamento, CodigoPostal, Referencia opcional.
- Franja opcional: Id, Desde, Hasta.
- Bultos[]: Id, Codigo.
- ReglasEntrega: Origen, VersionId opcional, FirmaObligatoria, ExigeFoto, ExigeNombreYDocumento, MaxIntentos, PlazoEntreIntentos.
- MotivosNoEntrega[]: Id, Codigo, NombreParaMostrar, Reprogramable, ExigeEvidencia.

Para el proveedor actual: Origen = Provisorias y VersionId = null. No inventar una versión persistida o reutilizar la versión de planificación como si fuera la de entrega. El adaptador de CU-06 llenará la versión real correspondiente al envío sin cambiar la forma del contrato.

No descargar correo/documento del destinatario, importes, liquidaciones, cuentas de otros comercios, historial completo ni archivos de pruebas anteriores.

La PWA guarda DescargadaEn sólo al terminar la escritura durable. GeneradaEn y DescargadaEn son datos distintos.

## 7. Responsabilidades del servidor

Ejecución es dueño del endpoint y del handler DescargarHojaDeRutaHandler. Coordina contratos:
- Planificación: nueva consulta por día y repartidor que devuelve la estructura de la ruta, su revisión, vehículo, zona y envíos de cada parada.
- Envíos: nueva consulta por lote para los IDs autorizados, con datos mínimos de entrega, bultos y reglas/catálogo del mismo proveedor que utiliza la validación.
- Administración: contexto del operador y zona horaria a través de sus contratos existentes.

No se consultan tablas ajenas desde Ejecución ni se referencia Infrastructure de otro módulo. Los DTO HTTP viven en Logistica.Http.Contracts.Repartidor, y los contratos internos en los proyectos Contracts de sus dueños.

La lectura usa una instantánea consistente REPEATABLE READ y READ ONLY en la unidad de trabajo compartida, mediante IConsultaConsistente, un puerto independiente de EF en SharedKernel. Conserva las transacciones propias del publicador/consumidor y la semántica de Sumar. Una lectura anidada en otra transacción se rechaza explícitamente.

Antes de responder:
- Comprobar pertenencia y estado de la ruta en Planificación, además del filtro Tenant.
- Resolver envíos por lote, no una consulta por parada.
- Exigir que todos los envíos y bultos esperados estén presentes y que cada parada conserve el orden correcto.
- Una ausencia o incompatibilidad rechaza la descarga completa; no ocultar silenciosamente envíos.
- Registrar duración y resultado sin nombres, teléfonos, direcciones ni el cuerpo del documento en logs.

## 8. Persistencia y cifrado local

Implementación: un servicio C# de almacenamiento más un módulo JS por JS interop, con IndexedDB y Web Crypto del navegador.

- Base versionada, por ejemplo logistica-repartidor-v1.
- Object stores: espacios, claves y hojas.
- Espacio lógico compuesto por UsuarioId + OperadorId + RepartidorId.
- Una clave AES-GCM de 256 bits por espacio, generada con Web Crypto, no exportable, persistida como CryptoKey en IndexedDB.
- IV aleatorio de 96 bits nuevo para cada cifrado; etiqueta de autenticación de 128 bits.
- Datos asociados autenticados: identidad del espacio, SchemaVersion, RutaId y FechaRuta.
- Registro durable: metadatos mínimos, IV y ciphertext. Nombres, teléfonos, direcciones, referencias y códigos de bultos quedan dentro del contenido cifrado.
- No persistir claves como texto/JWK, ni usar contraseña de Identity como clave, ni incorporar un PIN adicional en este alcance.
- Verificar soporte real de IndexedDB y Web Crypto. Si no se puede guardar, mostrar que la descarga offline falló, conservando la copia anterior.

El cifrado impide guardar el documento personal en claro y detecta alteraciones del ciphertext. No protege frente a código malicioso del mismo origen, XSS ni una persona que utiliza el mismo perfil de navegador ya desbloqueado. Una CryptoKey no exportable no equivale a una clave protegida por hardware.

Fuente técnica: [W3C Web Cryptography API, almacenamiento de claves y AES-GCM](https://www.w3.org/TR/WebCryptoAPI/).

## 9. Escritura atómica y fallos

Descargar → validar esquema/identidad/contenido → cifrar en memoria → transacción IndexedDB que sustituye la copia activa → mostrar éxito.

- Corte de red, timeout, JSON inválido, cuota agotada o error de cifrado/escritura: la copia anterior permanece.
- Si falla una primera descarga, indicar No hay una hoja guardada para usar sin conexión.
- Si el servidor responde 204, mostrar No tenés una ruta despachada para hoy. Conservar la copia anterior identificada como consulta anterior, sin confundirla con disponibilidad actual.
- No borrar el almacenamiento por un error transitorio de red.
- Cerrar y reabrir el navegador sin conexión debe recuperar la copia completa ya confirmada.
- El service worker guarda únicamente el shell y recursos estáticos; no intercepta API o login para responder documentos personales desde caché.
- Fuera del contexto seguro requerido por Web Crypto, no declarar disponible el modo offline. localhost es el entorno de desarrollo; el despliegue utiliza HTTPS.

## 10. Identidad, sesión y cierre

La cookie sigue siendo HttpOnly: la PWA no intenta leerla.

- Primer acceso en un navegador: requiere conexión e identidad confirmada por el endpoint de sesión.
- Reapertura sin red: se permite leer la copia del último espacio activo previamente verificado. Esto es continuidad local, no un nuevo inicio de sesión ni una validación online de que la cuenta siga activa.
- 401/403 confirmados por el servidor: bloquear la vista y el espacio activo, pedir autenticación y conservar los datos cifrados.
- Después de autenticarse: comparar los tres identificadores antes de cargar una copia. Una cuenta nueva no hereda datos visibles de la anterior.
- Al confirmar cierre de sesión: bloquear el espacio activo, retirar contenido descifrado de memoria e invalidar el selector local de cuenta antes de abandonar la PWA.
- Integrar esa invalidación con el formulario de cierre del repartidor y con las páginas de inicio de sesión; no depender sólo del evento unload. Las otras pestañas se enteran con BroadcastChannel.
- El retorno a login y un cambio de cuenta no eliminan una futura cola pendiente de CU-52. Los datos cifrados quedan asociados a su propietario y sólo se recuperan tras reautenticar esa misma identidad.
- Conservar una sola hoja confirmada por espacio, sin acumular historial personal. Al sustituirla se elimina la hoja anterior; la futura cola de operaciones va en un almacén distinto y no se borra por actualizar una hoja.

## 11. Interfaz con CU-51 y CU-52

CU-50 sólo entrega una instantánea descargada. Las operaciones locales futuras se guardarán por separado y se superpondrán para la presentación; actualizar la hoja nunca deberá borrar carga/entregas/intentos pendientes.

Después de sincronizar, CU-52 podrá invocar el mismo servicio de descarga. Ruta.Revision no autoriza al cliente a imponer estados ni resuelve conflictos. Esa decisión sigue en el servidor con ADR-0004.

No se implementan en CU-50: escaneo/carga, firma/fotos, registro de entregas, intentos fallidos, cola, sincronización, posiciones, rendición, reasignaciones urgentes o mapas.

## 12. Pruebas y aceptación

Servidor:
1. Sólo Repartidor con vínculo y tenant válidos: 401/403 según corresponda.
2. Un repartidor no descarga rutas ajenas, de otro operador o Planificadas, aunque conozca un GUID.
3. Fecha correcta cerca de medianoche y con host/dispositivo en otra zona horaria.
4. Despachada y EnCurso descargables; sin ruta y Finalizada devuelven 204.
5. Varias paradas, varios envíos por parada, orden, direcciones, franjas y todos los bultos.
6. Reglas y catálogo idénticos a los del proveedor usado por CU-17/CU-18; proveedor provisorio declarado, versión real no inventada.
7. Sin escrituras ni cambios de estado, revisiones, eventos o avisos por descargar.
8. Datos faltantes rechazan el documento completo; lectura consistente ante cambios concurrentes.
9. Ausencia de datos personales extra y cabeceras no-store.

Cliente/almacenamiento:
10. Primera descarga y lectura posterior idénticas.
11. IndexedDB no contiene nombres, teléfonos o direcciones en claro; clave no exportable; IV diferente al reescribir.
12. Ciphertext alterado o espacio incorrecto no se puede abrir.
13. Escritura fallida, cuota agotada, timeout y documento incompleto conservan la copia anterior.
14. Reabrir la app sin red muestra la hoja y su fecha de descarga.
15. Cambio de usuario/operador/repartidor, cierre y 401/403 no exponen la hoja anterior.
16. 204 y copia de otro día tienen mensajes distintos de la ruta vigente.
17. Actualizar no elimina almacenes de operaciones pendientes futuros.

Verificación final:
- Pruebas unitarias y de integración relevantes; arquitectura; regresión completa por el nuevo contrato y la lectura transaccional.
- Pruebas JS/IndexedDB en navegador, además de las existentes del service worker.
- Docker publicado: descargar → cerrar → quitar red → reabrir → ver paradas y detalle.
- Probar los dos operadores y dos repartidores del mismo operador.
- Ningún botón para CU-51/CU-17/CU-18 se presenta como funcional antes de implementarlo.

## 13. Orden de implementación mañana

1. Contratos internos y HTTP, consultas por lote y lectura consistente; pruebas de permisos, día operativo y proveedor.
2. Endpoint y handler en Ejecución; integrar reglas/catálogo del proveedor actual.
3. Repositorio IndexedDB cifrado, escritura atómica y pruebas de fallo/persistencia.
4. Identidad local, sesión vencida y cierre/cambio de cuenta.
5. Mi ruta, paradas y detalle, actualización y acceso desde aviso del día.
6. Publicar en Docker y comprobar cierre/reapertura sin conexión; regresión.
7. Registrar evidencia y la limitación explícita de reglas provisorias hasta CU-06.

No hace falta crear una nueva tabla de rutas o envíos para CU-50. Si la implementación revela una necesidad de esquema no prevista, documentarla antes de introducirla.
