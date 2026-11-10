# RF 32 · Análisis de alcance y faltantes antes de nuevos cambios

> **Decisión posterior de cierre:** Lucas confirmó persistir toda la captura cifrada antes de confirmar (D9 del [cierre técnico](../cierre-tecnico-entrega-parcial.md)). Las consultas y la elección de advertencia registradas en este análisis son antecedentes; la decisión vigente requiere recuperación de firma/foto/textos y nuevas pruebas. No se declara implementada aquí.

Fecha: 09/11/2026. Base revisada: `549c1ae`, con árbol de trabajo inicialmente limpio.

Este documento conserva el diagnóstico inicial. La continuación autorizada por Lucas se registra en la sección 8; la especificación vigente y los resultados posteriores están enlazados allí.

Este análisis contrasta la tarjeta con documentación, código y pruebas existentes. No implementa funcionalidades, no modifica acuerdos aceptados ni declara cerradas las tarjetas de servidor o RF 31 a 36. La fecha objetivo y el responsable propuestos en la tarjeta requieren confirmación de planificación; el README registra trabajo previo de Lucas, que no sustituye esa confirmación.

## 1. Conclusión

RF 32 ya tiene implementación en esta base. No corresponde comenzar otra implementación desde cero. El núcleo de la tarjeta está representado por `Entrega.razor`, `EntregaService`, `hoja-ruta.js`, `evidencia.js` y los contratos de repartidor. Falta consolidar su especificación y evidencia de aceptación, y aclarar los límites de recuperación del formulario antes de confirmar.

La afirmación anterior se basa en inspección del código y en 72 pruebas JavaScript aprobadas en esta revisión. Las pruebas de integración y las comprobaciones de navegador anteriores se encuentran documentadas en el [README](README.md); **no se volvieron a ejecutar aquí**. No se acredita una prueba actual de cámara física ni del circuito desplegado completo.

## 2. Reglas que ya están cerradas

Fuentes: [decisión funcional, secciones 4 y 5](../decisiones-entrega-parcial.md), [ADR-0004, sección 2.11](../ADR-0004-resolucion-de-conflictos-de-sincronizacion.md) y [ADR-0007, sección 2](../ADR-0007-adaptacion-a-entrega-parcial.md).

1. Una operación representa una visita de un envío dentro de una parada. Otros envíos de esa parada tienen operaciones independientes.
2. Su alcance son los bultos que viajaron en esa ruta y corresponden a la visita. Los no cargados y los ya resueltos quedan fuera. Un faltante de carga no es un intento fallido en domicilio.
3. Cada bulto del alcance tiene un resultado: entregado por escaneo o no entregado con un motivo individual del catálogo de la versión de reglas del envío. No escanear no asigna un motivo implícito.
4. La firma es obligatoria cuando se entrega alguno y respalda exclusivamente el conjunto mostrado de entregados. Cambiar ese conjunto exige volver a firmar. Sin entregas no se pide firma.
5. Una sola foto de presencia puede respaldar resultados exitosos y fallidos de esa visita. Es obligatoria cuando lo requieren las reglas de entrega o alguno de los motivos. No se necesitan dos fotos de entrega/rechazo.
6. Sin parcial habilitada se entregan todos los pendientes que corresponde entregar o ninguno. Entregar todos los cargados no permite omitir otro pendiente. Los entregados de visitas anteriores no vuelven a cargarse ni firmarse.
7. El servidor aplica o rechaza toda la visita. El rechazo conserva evidencia y diagnóstico por bulto, no consume intentos y no genera efectos de entrega. No se aplica automáticamente el subconjunto válido.
8. Un reenvío conserva `OperacionId` y contenido. No genera otra entrega, evidencia aplicada, evento, aviso ni recargo. Una nueva visita real es otra operación; no se crea para sortear un rechazo.
9. Una visita posterior utiliza evidencia propia y conserva la anterior. La prohibición local de registrar otra visita del mismo envío en la misma ruta no debe confundirse con la prohibición de una revisita en otra ruta.

## 3. Matriz de cobertura observada

Las rutas de código de esta tabla son relativas a la raíz del repositorio. «Existe» indica inspección, no aceptación completa en un teléfono desplegado.

| Necesidad | Evidencia existente | Situación |
| --- | --- | --- |
| Escanear entregados y detectar código repetido/ajeno | `src/Logistica.Repartidor.Pwa/Pages/Entrega.razor`, `wwwroot/js/camara-carga.js`, `resultadoVisita` | Existe cámara y entrada de código; lector incluido localmente. Falta ensayo físico. |
| Motivos individuales y alcance cargado | `contextoVisita`, `abrirVisita`, `resultadoVisita`, `registrarVisita` en `wwwroot/js/hoja-ruta.js` | Existe; no cargados excluidos y resultado obligatorio por cada bulto del alcance. |
| Firma y foto | `Entrega.razor`, `wwwroot/js/evidencia.js`, validación de `registrarVisita` | Existe; la pantalla reconstruye el canvas al cambiar entregados. El ensayo anterior de navegador está descrito en el README. |
| Parcial prohibida | Validación de carga y de `registrarVisita`; `Rf32PwaTests` y `VisitaPorBultoTests` | Existe validación local y servidor. |
| Guardado offline | `escribirBorrador` y `registrarVisita` | Borrador confirmado y operación con evidencia se escriben en una transacción de IndexedDB; revisión contra doble confirmación. |
| Aislamiento y cifrado local | `key`, `activo`, `cifrar`, `descifrar` y comprobación del token local | Se reutiliza el espacio por usuario/operador/repartidor y AES-GCM; no se necesita otro almacén. |
| Reintento y respuesta perdida | `sincronizarCargas`, `enviarPendientes`, `aceptarSinBorrador` | Se reenvía la operación persistida; Aplicada/YaAplicada dejan recibo, rechazos conservan operación y evidencia. |
| Estado y rechazo visible | `estadosEnvios`, `Entrega.razor`, `HojaDeRuta.razor` | Existe distinción pendiente/aplicada/rechazada y listado de códigos/motivos rechazados. |
| Reapertura sin red | `service-worker.published.js` y hoja local | Requiere instalación previa del shell publicado, sesión/hoja disponibles y mismo origen. Development no demuestra esta capacidad. |
| Hoja y compatibilidad | `HojaDeRutaResponse`, `validarHoja` | Schema 3 exige estado individual y regla explícita; se leen schemas 1/2 sin borrar datos. |
| Evidencia aplicada por bulto e idempotencia de negocio | `Rf32PwaTests`, `VisitaPorBultoTests`, contratos de Envíos | Hay pruebas de integración existentes; su ejecución actual pertenece a la verificación conjunta con servidor. |
| Rendición coherente | Proyección `baseRendicion` e integración documentada | Entregados no se solicitan como retorno físico; no cargados no se convierten en intentos. |

## 4. Contrato que debe quedar consolidado

No se detectó necesidad de crear un endpoint adicional. Se reutiliza `POST /api/repartidor/sincronizacion`, definido en `src/Logistica.Http.Contracts/Repartidor`.

| Parte | Semántica vigente |
| --- | --- |
| Envoltorio | `OperacionId`, dispositivo, secuencia, tipo `RegistrarVisita`, ruta, envío, fecha del dispositivo, ubicación y correlación. El tenant se resuelve en servidor; no lo decide el payload. |
| `Visita.Bultos` | Código por bulto y `MotivoId`. Motivo nulo significa entregado; motivo informado significa intento fallido. No incluir los que no viajaron. |
| Evidencia | Firma/foto de visita, receptor y observaciones. La firma se asocia al subconjunto sin motivo; la foto puede compartirse con resultados fallidos. El servidor materializa los vínculos a `BultoIds`. |
| Archivos | PNG/JPEG, hasta 1 MB por archivo; la foto se comprime en el cliente. |
| Respuesta | `Aplicada`, `YaAplicada`, `Conflicto` o `Invalida`, más motivo, detalle y diagnóstico de bultos cuando corresponda. No existe aplicación parcial. |
| Compatibilidad | Las operaciones antiguas conservan identidad y payload. El servidor traduce entrega/intento por envío a visita según ADR-0004; la pantalla nueva produce `RegistrarVisita`. |
| Hoja | Schema 3, estados de bulto y `AdmiteEntregaParcial` de la versión del envío. La hoja y el manifiesto de rendición deben coincidir. |
| Límites | Hasta 20 operaciones por lote. Sin firma si no hay entregados; nombre/documento según reglas; observaciones hasta 1000 caracteres. Actualmente se exige ubicación para confirmar. |

Los métodos antiguos de entrega/intento siguen existiendo en el servicio/JavaScript por compatibilidad. Su presencia no demuestra que la pantalla nueva los invoque; no corresponde eliminarlos sin revisar las colas y sus pruebas.

Las fronteras se mantienen según ADR-0001/0002/0003: la PWA referencia `Http.Contracts`; los módulos se comunican por sus contratos, sin acceso al DbContext ajeno. La transacción del servidor y Outbox garantizan efectos atómicos; la cola local por sí sola no puede garantizar ausencia de avisos o recargos duplicados.

## 5. Faltantes y decisiones para el cierre

| Prioridad | Punto | Acción concreta | Ámbito |
| --- | --- | --- | --- |
| Antes de aceptar | Documentación contradictoria | Consolidar una especificación técnica de RF 32 enlazada desde la tarjeta, con alcance, contrato, recuperación y criterios de aceptación. Este análisis aporta la base; no se encontró un documento separado titulado especificación técnica de RF 32. | Documentación de RF 32 |
| Antes de aceptar | Estado desactualizado en ADRs | Actualizar el estado de implementación, preservando la decisión y el historial. ADR-0004 sección 7 y varias filas de ADR-0007 sección 4.4 aún dicen «Falta la PWA»; ADR-0007 sección 4.5 conserva retrospectiva pendiente. La decisión 4.1/6.2 conserva pendientes que el README ya reporta implementados. | Documentación compartida |
| Antes de aceptar | Prueba integrada real | Ejecutar el circuito publicado en teléfono, con pérdida y recuperación de conectividad, evidencia y conflicto. Registrar versión, dispositivo, pasos, resultado y operación reenviada. | PWA + servidor; coordinación con avisos |
| Por explicitar | Durabilidad antes de confirmar | Hoy sólo persisten los resultados por bulto del borrador. Firma/foto/receptor/observaciones no confirmados permanecen en pantalla. Definir si se acepta recapturarlos al salir o si se requiere persistir también ese formulario. | Decisión de producto RF 32 |
| Por verificar | Errores operativos | Comprobar en el dispositivo almacenamiento no disponible/lleno, ubicación denegada o sin posición, cámara denegada, sesión vencida y actualización del shell con operaciones pendientes. No mostrar guardado correcto si falló la persistencia. | PWA, con regresión de sesión |
| Por completar fuera de RF 32 | Custodia y exclusividad individual | Conciliación de faltantes, reserva activa por bulto, liberación y reasignación concurrente según decisión 4.2. La recuperación de envíos completos no cargados no resuelve la reserva individual. | RF 34 / Planificación / Depósito |
| Por verificar fuera del núcleo PWA | Progreso público y tablero | Comprobar de punta a punta que 1/3 → 2/3 actualiza consultas/caché/tablero aun sin cambiar el resumen. Avisos v2 tienen implementación documentada, pero eso no prueba todo el recorrido. | RF 35/36 y ADR-0006 |

La primera fila no agrega una decisión funcional nueva: reunir los contratos existentes evita implementar basándose en estados documentales incompatibles.

La durabilidad del formulario es el único cambio de comportamiento propuesto para evaluación en este análisis. No se da por aprobado ni se deriva automáticamente del requisito de operación offline: lo confirmado ya se conserva. Recomendación: informar claramente qué queda guardado y evitar abandonar inadvertidamente una captura sin confirmar; si se exige recuperación completa tras recarga, diseñar su persistencia cifrada y la invalidación de firma antes de tocar código.

## 6. Criterios verificables de aceptación conjunta

1. Tres bultos cargados, parcial habilitada: dos escaneados y uno con motivo individual; sin red se confirma una visita y se reabre el resultado con la misma operación y evidencia.
2. Sólo dos de tres cargados: el tercero queda fuera de la visita, no consume intento y no recibe motivo de domicilio. Entregar ambos conserva el envío parcialmente entregado.
3. Parcial deshabilitada: no sale un envío incompleto ni se registra una entrega mixta; otro envío completo de la ruta puede continuar.
4. Entrega con firma obligatoria: cambiar entregados invalida la firma incluso si se vuelve al conjunto inicial. La foto única satisface las reglas de la visita sin firmar los fallidos.
5. Cero entregados: todos los bultos del alcance tienen motivo, no se pide firma y se exige foto si alguno de los motivos la requiere.
6. API aplica y se pierde la respuesta: al reenviar llega el mismo `OperacionId` y contenido; se obtiene YaAplicada y no aumentan intentos, evidencia aplicada, Outbox, avisos ni recargo.
7. Un bulto cambia en servidor antes de sincronizar: se rechaza toda la visita, se conserva evidencia, se muestra diagnóstico y no se crea otra operación para eludirlo. Otro envío independiente se procesa.
8. Reapertura del build publicado en modo avión: shell, hoja y resultados confirmados disponibles. Separar esta prueba del simple fallo de la API y del modo Development.
9. Dos pestañas y cambio de sesión: no se confirman dos veces la misma visita ni se mezclan hojas, evidencia o resultados entre identidades.
10. Nueva visita en otra ruta: sólo se incluyen los pendientes elegibles; evidencia nueva, anteriores preservadas y recargo idempotente según RF 33.
11. Rendición: entregados no vuelven a solicitarse como paquetes físicos y el no cargado no se transforma en intento fallido.
12. Cola anterior y hoja nueva: se conservan operaciones pendientes e identidad; no se borra IndexedDB ni se reescriben operaciones confirmadas para actualizar el esquema.

Los puntos 6 y 7 requieren evidencias del servidor; el conteo de avisos requiere el consumidor correspondiente. No se trasladan esas implementaciones a la tarjeta PWA.

## 7. Próximo paso recomendado

Usar como estado de trabajo «implementación existente, pendiente de consolidación documental y aceptación integrada». Primero acordar el límite de recuperación del formulario y registrar los criterios anteriores en la tarjeta/especificación. Después actualizar los estados documentales y ejecutar la aceptación conjunta. Sólo los defectos o diferencias que resulten de esa revisión deben convertirse en cambios funcionales de RF 32.

Verificación realizada en este análisis: `npm test` en `scripts/tests`, **72/72 aprobadas**, sin cambios funcionales. No se ejecutó compilación .NET, integración con PostgreSQL, navegador ni cámara física en esta revisión.

## 8. Continuación autorizada el 09/11/2026

Lucas solicitó completar lo restante de RF 32 y eligió **mantener el comportamiento y advertir antes de salir**, sin persistir firma, foto ni textos antes de confirmar. La pantalla incorpora explicación, estado de captura pendiente y confirmación al navegar; recarga/cierre usan la advertencia nativa del navegador. Una visita confirmada conserva su evidencia y deja de advertir.

La [especificación técnica](especificacion-tecnica.md) consolida alcance, contratos, recuperación y aceptación. La actualización concurrente de ADR-0007 también corrigió los estados históricos de ADR-0004 y del documento de decisiones; no se sobrescribió ese trabajo.

El [registro de verificación](verificacion-cierre.md) recoge compilación, pruebas e inspección del build publicado, con sus límites. Continúan fuera de RF 32 custodia/reservas individuales, consumidores financieros y tablero. La aceptación física en teléfono y el recorrido con receptor externo deben registrarse antes de declarar demostrada la operación en el dispositivo objetivo.
