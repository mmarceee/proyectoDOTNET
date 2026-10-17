# CU-50 · Descargar la hoja de ruta

Actualización: 17/10/2026. Implementado sobre el [diseño acordado](diseno-cu-50.md).

## Comportamiento

- La PWA conserva Avisos de rutas y agrega Mi ruta en /repartidor/hoja-de-ruta.
- El aviso del día permite abrir la hoja. Los avisos antiguos no habilitan descargas de otras fechas.
- Descarga automática al abrir Mi ruta y actualización manual, con una sola descarga en curso.
- Se muestra la ruta por fecha, vehículo, zonas, paradas ordenadas, envíos, destinatario/teléfono, dirección/referencia, franja y bultos. Los GUID no se usan como nombre visible.
- La descarga se confirma recién al terminar el guardado cifrado. Se puede abandonar la página y volver a abrirla sin servidor.
- Ante fallos de red, cuota, escritura, datos incompletos o cifrado se conserva la copia anterior.
- Sin ruta hoy (204) se informa expresamente. Una copia anterior se conserva como consulta anterior y nunca se presenta como ruta vigente confirmada.
- Descargar no cambia estados de rutas/envíos, revisiones, avisos ni outbox. Los envíos quedan AsignadoARuta hasta CU-51.

## Contratos y arquitectura

GET /api/repartidor/sesion y GET /api/repartidor/hoja-de-ruta, con Politicas.Repartidor, sin parámetros de operador/repartidor/fecha. El día se calcula con TimeProvider en la zona horaria del operador.

Ejecución coordina IHojaRutaPlanificacion (Planificación), IEnviosHojaRuta (Envíos) e IContextoHojaRuta (Administración). Sólo utiliza Contracts de los otros módulos. Se vuelve a verificar el vínculo activo del usuario con el repartidor, incluso si ya tenía cookie.

La lectura usa IConsultaConsistente, implementada por la misma UnidadDeTrabajo: transacción PostgreSQL REPEATABLE READ y READ ONLY. Cada contexto se suma antes de consultar; al terminar se desengancha. Una consulta anidada en otra transacción se rechaza. No se alteró la protección de las transacciones propias del publicador/inbox.

Una ruta puede contener envíos de varias zonas. Se devuelve Zonas[]; el modelo no guarda una zona única de ruta. Las zonas y vehículos ya usados se consultan aunque dejen de estar activos; desactivarlos no elimina la hoja despachada.

Se admiten Despachada y EnCurso del día operativo; Planificada y Finalizada no se descargan como hoja activa. Las consultas de otros operadores y otros repartidores quedan fuera del alcance. Datos faltantes producen 503, sin documento parcial.

## Reglas de entrega

Decisión del usuario: se descarga el mismo proveedor provisorio que valida CU-17 y CU-18, sin duplicar reglas o IDs de motivos en la PWA.

Origen = Provisorias, VersionId = null. Firma obligatoria, foto y nombre/documento de receptor no exigidos, tres intentos y 24 horas entre intentos; catálogo AUSENTE, SIN_ACCESO, DIRECCION_INEXISTENTE y RECHAZADO, con sus indicadores de reintento/evidencia.

IReglasDeEntrega.ParaHojaAsync prepara la sustitución por CU-06. Esto no acredita la implementación ni el versionado real de CU-06.

## Almacenamiento e identidad local

IndexedDB logistica-repartidor-v1: espacios, claves y hojas. El espacio se identifica por usuario + operador + repartidor. Se conserva una hoja por espacio, no un historial de direcciones.

El módulo JS cifra el documento con AES-GCM de 256 bits, IV aleatorio de 96 bits y etiqueta de 128 bits. La clave se guarda como CryptoKey no exportable; los identificadores, esquema, ruta y fecha se autentican como datos asociados. No hay contraseñas ni claves en texto/JWK, y no se usan localStorage o Cache Storage para documentos personales.

La escritura en IndexedDB es atómica. Dos pestañas no crean claves diferentes para el mismo espacio. El token del espacio impide que una descarga iniciada antes del cierre de sesión reactive la copia.

Las páginas de ingreso y la confirmación de cierre bloquean el espacio activo mediante el recurso compartido repartidor-cuenta.js. BroadcastChannel invalida las vistas abiertas en otras pestañas. Se conservan los documentos cifrados para el mismo propietario; no se elimina una futura cola pendiente de CU-52. 401/403 bloquean la vista y requieren autenticación. Una cuenta distinta no hereda la hoja de la anterior.

La reapertura sin red utiliza la identidad local verificada anteriormente; no representa una comprobación online de vigencia de la cuenta. El cifrado no protege de XSS ni de otra persona que usa el mismo perfil del navegador desbloqueado; no es una clave respaldada por hardware.

## Verificación

- Regresión completa: 397 pruebas .NET (90 unitarias, 54 de arquitectura y 253 de integración).
- Se agregaron posteriormente dos casos de seguridad: otro repartidor del mismo operador y deshabilitación del vínculo con cookie existente. Verificación final dirigida: 25 pruebas de CU-50/unidad de trabajo, todas correctas.
- JavaScript: 12 pruebas correctas, con Web Crypto real de Node y fake-indexeddb como dependencia exclusiva de pruebas. Incluyen cifrado, clave no exportable, IV distinto, alteraciones del ciphertext, documento incompleto, abortar escritura, cerrar/cambiar cuenta, descarga en curso al cerrar, 204 y concurrencia entre guardados, más las tres pruebas existentes del service worker.
- Navegador real, Docker publicado: alta del envío ENV-000009 en Envíos del Este → recepción en depósito → ruta del 17/10/2026 → despacho → aviso interno → Ver mi ruta → hoja descargada.
- Se detuvo sólo la API, se abandonó la página y se reabrió Mi ruta: la PWA recuperó la hoja y abrió el destinatario, teléfono, dirección/referencia y bulto sin servidor.
- Se restauró la API y se verificó la actualización de la copia.
- Se cerró la sesión y se volvió a desconectar la API: no se mostró la hoja personal.
- API y Worker quedaron restablecidos después de las comprobaciones.

Las pruebas con fake-indexeddb no sustituyen la comprobación en navegador; se realizaron ambas.

## Ejecutar pruebas

Para comprobar la reapertura sin conexión, usar la versión publicada de Docker en http://127.0.0.1:8080/repartidor/. El service worker de desarrollo (dotnet run / Visual Studio, puertos 5221 o 7279) no cachea la aplicación: puede guardar la hoja en IndexedDB, pero no permite recargar toda la PWA sin servidor. Además, localhost y 127.0.0.1, así como los distintos puertos, tienen almacenamientos independientes; la prueba debe mantener la misma dirección usada para descargar. Esperar la instalación del service worker y la confirmación «Guardada en este dispositivo» antes de desconectar; no cerrar sesión ni borrar los datos del sitio.

El manifiesto web es público porque el navegador lo solicita sin credenciales por defecto. Sólo contiene metadatos de instalación; las páginas de la aplicación y las APIs siguen protegidas por sesión y perfil.

Desde la raíz:

    dotnet test Logistica.slnx --no-restore -c Release

Para JavaScript, entrar en scripts/tests, ejecutar npm ci y luego npm test. Node/fake-indexeddb se necesitan sólo para las pruebas, no para ejecutar la PWA publicada.

## Fuera de alcance

CU-51 (escaneo/carga), CU-17/CU-18 (pruebas y resultados en la PWA), cola y disparadores de CU-52, posiciones, rendición, mapas y reasignaciones. CU-50 deja el contrato y el almacenamiento listos para integrarlos, sin mostrar botones operativos sin implementación.
