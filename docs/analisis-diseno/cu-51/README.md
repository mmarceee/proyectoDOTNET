# CU-51 · Carga del vehículo

Implementación local del 19/10/2026, sobre el [diseño acordado](diseno-cu-51.md).

## Flujo disponible

La PWA agrega Cargar vehículo en la navegación y en Mi ruta. Usa la hoja propia descargada y admite códigos por cámara o por entrada manual. Ambos pasan por la misma validación y persistencia.

El avance distingue esperados, repetidos y sobrantes. Cada lectura se guarda inmediatamente como borrador cifrado; repetir un código no aumenta el avance. Los faltantes se calculan por diferencia, no se guardan como escaneos.

Confirmar con faltantes requiere una casilla de confirmación expresa y muestra los envíos que seguirán AsignadoARuta. La confirmación local crea una operación pendiente e inmutable; sólo el resultado del servidor muestra la carga como aplicada. El progreso de envíos incompletos se conserva para una nueva confirmación sobre la hoja actualizada.

El servidor admite ConfirmarCarga por POST /api/repartidor/sincronizacion, junto a los tipos previos. En una transacción registra los escaneos, aplica T5 sólo a los envíos completos, agrega EnvioEnTransito.v1 al outbox, avanza la ruta a EnCurso si corresponde y registra la operación aplicada. Una incompatibilidad rechaza toda la confirmación, conservando su payload. Reenviar el mismo OperacionId no repite efectos. El bloqueo por operador es el mismo que usan las confirmaciones de planificación y se vuelve a comprobar la revisión de ruta dentro de él.

Ejecución coordina IPlanificacionModuleApi e IEnviosModuleApi mediante sus implementaciones internal en Application (ADR-0001 §2.2). Los resultados inmutables de carga están en Contracts/Results. Cada API usa repositorios de su propio módulo; Infrastructure implementa persistencia, sin coordinar transiciones de negocio. Se eliminaron los contratos paralelos ICargaRuta e ICargaEnvios y sus implementaciones de infraestructura. IContextoHojaRuta conserva la lectura existente de CU-50 para comprobar nuevamente el vínculo activo usuario/repartidor. Sólo se reconocen códigos del operador de la sesión; un código desconocido o de otro operador queda sin BultoId y no revela información ajena.

## Almacenamiento y sincronización

IndexedDB mantiene el nombre logistica-repartidor-v1, pero su versión de esquema pasa a 2. Se agregan borradores, operaciones e instalacion conservando espacios, hojas y claves. El script de ingreso/cierre abre la versión existente, sin forzar la anterior.

Borrador y operación se cifran con AES-GCM usando la clave no exportable de su espacio usuario/operador/repartidor. El tipo y la clave del registro se autentican como datos asociados. Una revisión local impide que dos pestañas sobrescriban silenciosamente el borrador. Confirmar guarda el borrador confirmado y su operación en una sola transacción; una escritura abortada conserva el avance previo.

DispositivoId es estable para la instalación. Secuencia se reserva atómicamente entre pestañas (puede tener huecos si falla una confirmación). OperacionId es UUIDv7. CorrelacionId se genera al confirmar como traceparent W3C 00-{32 hex}-{16 hex}-01 y se cifra con el payload. No cambia durante los reintentos.

Se intenta sincronizar al confirmar, al abrir la app con identidad local, tras verificar la sesión, al recuperar conectividad y con el botón manual. Un mutex coordina el servicio C# y Web Locks serializa los envíos entre pestañas donde está disponible; sin Web Locks, la idempotencia del servidor sigue evitando efectos duplicados. Se envían hasta 20 operaciones por solicitud. Pendiente y Enviando se recuperan tras una interrupción. Un 401/403 bloquea el espacio pero conserva las operaciones cifradas para su propietario. Aplicada/YaAplicada retira la operación de la cola; Conflicto/Invalida conserva la evidencia y permite actualizar la hoja y revisar expresamente.

La migración EF EscaneosCarga agrega ejecucion.escaneos_carga. No modifica envíos ni rutas existentes al aplicarse.

## Cámara

Se incluye @zxing/browser 0.1.5 con sus dependencias fijadas en el lock y licencias en wwwroot/lib/zxing. No se usa CDN. El service worker publicado incluye el lector en el shell offline. La cámara se detiene al salir de la pantalla, confirmar o invalidar la sesión; denegar el permiso permite continuar manualmente. En un celular real se necesita HTTPS: localhost del celular no es el equipo de desarrollo.

Para reproducir el recurso incluido: ejecutar npm ci y npm run vendor desde scripts/tests. Node no se necesita en producción.

## Verificación

- Regresión completa después de corregir los contratos: 428 pruebas .NET correctas, sin omisiones (98 unitarias, 56 de arquitectura y 274 de integración), ejecutadas con `dotnet test Logistica.slnx --no-restore -c Release --verbosity quiet`.
- Arquitectura: las reglas comprueban también que los lectores públicos existentes de Envíos y Planificación no expongan acciones de escritura fuera de ModuleApi. Las implementaciones de las API deben seguir siendo internal en Application.
- Transacción compartida: un fallo provocado después de guardar envíos, ruta, escaneos, ambos outbox y la operación, antes del commit, revierte los tres módulos. Reintentar la misma operación aplica una sola vez.
- Correlación: un ActivityListener escucha Logistica.Ejecucion y comprueba que operacion.procesar es un span Consumer exportable, con el trace-id y padre guardados en la PWA y sampled activo; los eventos EnvioEnTransito del outbox llevan exactamente el contexto de ese span. Esta prueba no acredita exportación al visor ni consumo del Worker.
- La regresión del servicio comprueba que abrir la carga espera a la sincronización inicial y recupera la copia con la API detenida.
- JavaScript: 23 pruebas correctas; cifrado, migración v1→v2, borrador concurrente, doble confirmación, corte de red, sesión vencida, cierre de sesión mientras llega la respuesta, rechazo, escritura abortada, continuidad de carga parcial, preservación de correlación y decodificación real de una imagen CODE-128 con el lector incluido.
- Navegador y Docker publicados: ruta del 19/10, dos envíos de dos bultos. Primer bulto, repetido y recuperación tras cerrar/recargar con la API detenida.
- Se registraron tres bultos esperados y un sobrante desconocido sin API. Se confirmó con un faltante y se volvió a abrir la PWA sin servidor: la operación continuaba pendiente.
- Al restablecer la API y sincronizar, ENV-000014 pasó a EnTransito, ENV-000037 permaneció AsignadoARuta y la ruta pasó a EnCurso, revisión 3. PostgreSQL contiene tres escaneos Esperado y uno Sobrante. El outbox de Envíos conserva el mismo trace-id iniciado por la PWA, con sampled=01.
- Revisar carga restante conservó ENV-000037-1 como cargado; ENV-000037-2 quedó disponible para completar la prueba.

Falta la comprobación física de cámara en el celular de Lucas; la decodificación del lector sí está automatizada. La versión de desarrollo 5221/7279 no cachea el shell: probar reapertura sin red en la publicación 8080, siempre en el mismo origen.

## Datos demo locales

Para evitar modificar las rutas anteriores, se crearon datos de prueba separados en la base local, no como datos iniciales automáticos del producto:

- Usuario: repartidor-cu51@envios-del-este.uy; contraseña pública de demo: Demo-2026!.
- Repartidor: Repartidor prueba CU51. Vehículo: CU51019.
- Ruta: 01a1541f-d5c3-73f5-ab43-b1c17e86ab88, 19/10/2026.
- ENV-000014 y ENV-000037 se prepararon EnDeposito con dos bultos cada uno. La ruta se creó, validó y despachó desde el Backoffice.

El material de prueba se preparó en PostgreSQL exclusivamente para la demostración local. No sustituye altas reales ni modifica las cuentas demo anteriores.

## Límites y coordinación

CargaConDiscrepancias.v1 queda en outbox como aviso durable para CU-66. EnvioEnTransito.v1 también queda disponible para sus consumidores. En esta rama el publicador sólo envía tipos con SuscripcionIntegracion: no se acredita visualización del aviso al despachador ni consumo en Worker hasta integrar esos consumidores.

ProcesadorEnAmbitoPropio continúa CorrelacionId por operación y ofrece ActivitySource Logistica.Ejecucion. El outbox captura Activity.Current. Esta rama no tiene la configuración OpenTelemetry anunciada por Ezequiel: falta integrar su instrumentación/exportadores para acreditar visualización completa PWA→API→Worker en el visor. No se duplicó esa implementación.

El ajuste de CU-04 sobre franjas con fecha heredada de ruta sigue a cargo de Cristian. CU-51 no finaliza rutas ni libera repartidor/vehículo (CU-54/55), no implementa la interfaz de entregas/intentos ni todas las métricas y operaciones de CU-52.
