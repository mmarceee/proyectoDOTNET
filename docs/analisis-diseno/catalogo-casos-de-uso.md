# **Catálogo de casos de uso**

| Estado | Borrador — para discutir con el equipo |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Fuentes** | Letra del laboratorio (secciones 4, 5 y 6), modelo de dominio, tabla de transiciones, ADR-0001 y addenda, ADR-0002, ADR-0003 |

Este documento lista los casos de uso que se desprenden de los requerimientos funcionales (RF 1 a 30) y de los no funcionales que implican funcionalidad visible (secciones 6.6, 6.8 y 6.9). Para cada uno indica el módulo propietario, el actor, la aplicación desde la que se usa, las transiciones de estado que dispara y los eventos que publica. Es la base para detallar cada caso y para repartir el trabajo por hito.

# **1\. Convenciones**

| Columna | Significado |
| :---- | :---- |
| **Tipo** | **C**: command, modifica estado. **Q**: query, sólo lectura. |
| **App** | **BO**: Backoffice (Razor Pages). **PC**: Portal del comercio. **SP**: Seguimiento público. **PWA**: aplicación del repartidor. **API**: integración de sistemas de comercios. **W**: Worker. |
| **Transición** | Identificador de la tabla de transiciones (T1 a T19; T11 se eliminó). |
| **Eventos** | Eventos que publica. **(M)**: en memoria. **(O)**: por Outbox. Criterio del addendum 3: si perder la reacción deja datos inconsistentes, o la procesa el Worker, va por Outbox. |
| **Hito** | Monitoreo en el que debe estar funcionando, según la sección 8.3 de la letra. |

Por el RF 28 (avisos a los comercios ante cada cambio de estado) y el RF 27 (notificaciones al destinatario), **toda transición de estado del envío publica un evento por Outbox**, que procesa el Worker.

# **2\. Casos de uso por módulo**

## **2.1 Administración y configuración**

| ID | Caso de uso | Actor | App | Tipo | Transición | Eventos | RF | Hito |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| CU-01 | Dar de alta, modificar, suspender y consultar comercios (y su `RelacionComercial` con el operador) | Administrador | BO | C/Q | — | — | 1 | 15/10 |
| CU-02 | Gestionar usuarios del operador y de los comercios, con perfiles y permisos | Administrador | BO | C/Q | — | — | 1, 6.3 | 15/10 |
| CU-03 | Seleccionar el operador con el que trabaja un comercio en la sesión | Usuario de comercio | PC | C | — | — | 3.3 | 15/10 |
| CU-04 | Definir zonas de cobertura y sus franjas horarias | Administrador | BO | C/Q | — | — | 2 | 15/10 |
| CU-05 | Configurar y publicar una versión del cuadro tarifario | Administrador | BO | C/Q | — | TarifarioPublicado (M): invalida caché | 3, 6.6, 6.7 | 15/10 |
| CU-06 | Configurar y publicar una versión de las reglas operativas, con su catálogo de motivos de no entrega | Administrador | BO | C/Q | — | ReglasPublicadas (M): invalida caché | 4, 6.6 | 15/10 |
| CU-07 | Gestionar la identidad visual del operador | Administrador | BO | C/Q | — | — | 5 | 15/10 |
| CU-08 | Gestionar repartidores y vehículos con su capacidad de carga | Administrador | BO | C/Q | — | — | 6 | 22/10 |
| CU-09 | Consultar los mensajes fallidos y reintentarlos | Administrador | BO | C/Q | — | — | 6.8 | 29/10 |

**Contratos que publica para otros módulos** (`IAdministracionModuleApi`): resolver la zona de un código postal, calcular la tarifa con la versión vigente, obtener la versión de reglas vigente, validar un motivo de no entrega, obtener la identidad visual del operador.

## **2.2 Envíos y entregas**

| ID | Caso de uso | Actor | App | Tipo | Transición | Eventos | RF | Hito |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| CU-10 | **Crear un envío individual** con sus bultos, calculando la tarifa | Usuario de comercio | PC | C | T1 | EnvioAdmitido (O) | 7, 8, 9, 11, 12 | **8/10** |
| CU-11 | Importar envíos desde un archivo, de forma idempotente | Usuario de comercio | PC | C | T1 | EnvioAdmitido (O) por envío | 7 | 12/11 |
| CU-12 | Crear envíos por API desde el sistema del comercio | Sistema del comercio | API | C | T1 | EnvioAdmitido (O) | 7 | 12/11 — ver sección 2.7 |
| CU-13 | **Consultar envíos** (listado con filtros) | Personal del operador / usuario de comercio | BO / PC | Q | — | — | 7, 25 | **8/10** |
| CU-14 | Consultar el detalle de un envío con su historial de eventos | Personal del operador / usuario de comercio | BO / PC | Q | — | — | 12 | 15/10 |
| CU-15 | Imprimir la etiqueta de un envío | Usuario de comercio | PC | Q | — | — | 4 | 12/11 |
| CU-16 | Cancelar un envío antes de su recepción en depósito | Usuario de comercio | PC | C | T17 | EnvioCancelado (O) | 11 | 15/10 |
| CU-17 | Registrar una entrega con su prueba | Repartidor | PWA | C | T6 | EnvioEntregado (O) | 20 | 22/10 |
| CU-18 | Registrar un intento fallido con motivo y evidencia | Repartidor | PWA | C | T7 | IntentoFallidoRegistrado (O) | 21 | 22/10 |
| CU-19 | Reprogramar un envío no entregado | Despachador / Sistema | BO / W | C | T9 | EnvioReprogramado (O) | 4, 11 | 15/10 |
| CU-20 | Iniciar la devolución de un envío | Despachador / Sistema / usuario de comercio | BO / W / PC | C | T10, T12, T14 | DevolucionIniciada (O) | 4, 22 | 15/10 |
| CU-21 | Declarar un envío como extraviado | Administrador / Despachador | BO | C | T16 | EnvioExtraviado (O) | 11 | 15/10 |
| CU-22 | Registrar, seguir y resolver incidencias de un envío | Despachador | BO | C/Q | — | — | 4 | 12/11 |

Todas las transiciones pasan por `Envio.Transicionar`, que aplica la tabla de transiciones y registra el `EventoEnvio` (RF 11 y 12). Las transiciones propias de otros módulos (T2, T3, T4, T5, T8, T15) las solicitan esos módulos mediante `IEnviosModuleApi`.

## **2.3 Depósito y liquidaciones**

| ID | Caso de uso | Actor | App | Tipo | Transición | Eventos | RF | Hito |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| CU-30 | Recibir bultos en depósito por escaneo, registrando discrepancias | Operario de depósito | BO | C | T2 | EnvioRecibidoEnDeposito (O) | 10 | 15/10 |
| CU-31 | Confirmar la devolución de un envío al comercio | Operario de depósito | BO | C | T15 | EnvioDevuelto (O) | 22 | 15/10 |
| CU-32 | Generar la liquidación de un comercio por período | Sistema (cierre diario) / Administrador | W / BO | C | — | LiquidacionEmitida (O) | 30 | 12/11 |
| CU-33 | Consultar la cuenta corriente y las liquidaciones | Usuario de comercio | PC | Q | — | — | 4, 30 | 12/11 |

## **2.4 Planificación de rutas**

| ID | Caso de uso | Actor | App | Tipo | Transición | Eventos | RF | Hito |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| CU-40 | Armar una hoja de ruta: asignar envíos a un repartidor y un vehículo, validando restricciones | Despachador | BO | C | T3, T13 | EnvioAsignadoARuta (O) | 13, 14, 15 | 15/10 |
| CU-41 | Quitar un envío de una ruta no despachada | Despachador | BO | C | T4 | EnvioDesasignadoDeRuta (O) | 13 | 15/10 |
| CU-42 | Ordenar las paradas de una ruta | Despachador | BO | C | — | — | 16 | 22/10 |
| CU-43 | Despachar una ruta y ponerla a disposición del repartidor | Despachador | BO | C | — | RutaDespachada (O): aviso al repartidor | 17 | 22/10 |

## **2.5 Ejecución de rutas**

| ID | Caso de uso | Actor | App | Tipo | Transición | Eventos | RF | Hito |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| CU-50 | Descargar la hoja de ruta del día para operar sin conexión | Repartidor | PWA | Q | — | — | 18 | 22/10 |
| CU-51 | Escanear los bultos al cargar el vehículo y confirmar la carga | Repartidor | PWA | C | T5 | EnvioEnTransito (O) | 19 | 22/10 |
| CU-52 | Sincronizar las operaciones registradas sin conexión | Repartidor (automático) | PWA | C | T5 a T7 | Los de CU-17, CU-18 y CU-51 | 24 | 22/10 |
| CU-53 | Reportar la posición del vehículo durante la jornada | Repartidor (automático) | PWA | C | — | PosicionReportada (M): tablero | 23 | 29/10 |
| CU-54 | Iniciar la rendición: declarar qué envíos y bultos vuelven al depósito | Repartidor | PWA | C | — | — | 22 | 22/10 |
| CU-55 | Confirmar la rendición escaneando lo que efectivamente llegó al depósito | Operario de depósito | BO | C | T8 | EnvioReintegradoADeposito (O) | 22 | 22/10 |

## **2.6 Seguimiento y notificaciones**

| ID | Caso de uso | Actor | App | Tipo | Transición | Eventos | RF | Hito |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| CU-60 | Consultar el seguimiento de un envío por enlace público | Destinatario | SP | Q | — | — | 25, 6.7 | 22/10 |
| CU-61 | Solicitar la reprogramación de un envío, también antes del primer intento | Destinatario | SP | C | T18, T19 (en Admitido, sin transición) | EnvioReprogramado (O) | 26 | 29/10 |
| CU-62 | Notificar al destinatario los cambios de estado relevantes | Sistema | W | C | — | — | 27 | 29/10 |
| CU-63 | Configurar las suscripciones de avisos del comercio | Usuario de comercio | PC | C/Q | — | — | 28, 6.9 | 29/10 |
| CU-64 | Entregar los avisos a los sistemas de los comercios, firmados y con reintentos | Sistema | W | C | — | — | 28, 6.9 | 29/10 |
| CU-65 | Consultar el historial de avisos y reenviar los fallidos | Usuario de comercio | PC | C/Q | — | — | 6.9 | 29/10 |
| CU-66 | Ver el tablero de operación en vivo | Despachador | BO | Q | — | — | 29, 6.10 | 29/10 |

**Reprogramación antes del primer intento (CU-61).** Decisión del equipo: el destinatario puede pedir la reprogramación antes del primer intento. Esto amplió la tabla de transiciones con T18 y T19, confirmadas por el responsable de la máquina de estados (Cristian Reyes):

| Estado actual | ¿Se puede reprogramar? | Efecto |
| :---- | :---- | :---- |
| Admitido | Sí | Sin cambio de estado: se registra la nueva fecha o franja, porque el envío todavía no llegó al operador. |
| EnDeposito | Sí | **T18**: EnDeposito → Reprogramado. |
| AsignadoARuta, con la ruta no despachada | Sí | **T19**: AsignadoARuta → Reprogramado, y el envío sale de la ruta. |
| EnTransito | No | El repartidor ya salió con el envío. |
| NoEntregado | No aplica | Es un estado transitorio: CU-18 reprograma o devuelve en la misma operación. Por eso se eliminó T11. |
| EnDevolucion y estados terminales | No | — |

En todos los casos se aplican las reglas del operador (RF 26), por ejemplo el plazo mínimo de anticipación.

## **2.7 API pública para comercios (opcional 7.4, 3 puntos)**

Decisión del equipo: se implementa la API pública documentada. La letra exige API documentada y versionada, claves por comercio, ambiente de pruebas, limitación de tasa diferenciada y documentación navegable. Sus endpoints se publican bajo `/api/v1/`, separados de los que usan las aplicaciones propias con la cookie de Identity.

| ID | Caso de uso | Actor | App | Tipo | Transición | Eventos | RF | Hito |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| CU-80 | Generar, listar y revocar claves de API, de producción y de prueba | Usuario de comercio | PC | C/Q | — | — | 7.4 | 12/11 |
| CU-81 | Crear envíos por API (implementa CU-12, reutilizando el caso de uso de CU-10) | Sistema del comercio | API | C | T1 | EnvioAdmitido (O) | 7, 7.4 | 12/11 |
| CU-82 | Consultar envíos y su historial por API | Sistema del comercio | API | Q | — | — | 7.4 | 12/11 |
| CU-83 | Consultar la documentación navegable de la API | Desarrollador del comercio | API | Q | — | — | 7.4 | 12/11 |

Ambiente de pruebas: los envíos creados con una clave de prueba se marcan con `EsPrueba` y nunca llegan a la operación real (casos de uso, sección 3).

## **2.8 Procesos del Worker y reportes**

| ID | Caso de uso | Actor | App | Tipo | Transición | Eventos | RF | Hito |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| CU-70 | Cierre diario: iniciar la devolución de reprogramados vencidos y generar liquidaciones | Sistema | W | C | T14 | DevolucionIniciada (O) | 4, 30 | 12/11 |
| CU-71 | Recalcular los indicadores de cumplimiento | Sistema | W | C | — | — | 29, 30 | 12/11 |
| CU-72 | Consultar reportes de gestión (los de cumplimiento los resuelve Seguimiento; la liquidación, Depósito y liquidaciones) | Administrador / Despachador | BO | Q | — | — | 30 | 12/11 |

# **3\. Prioridad para el monitoreo del 8/10**

La letra (sección 8.3) exige para el 8/10: *"docker compose up levanta el entorno completo, un envío dado de alta desde el portal del comercio se ve en el backoffice, y el pipeline de integración continua corre"*.

Eso requiere, como mínimo:

1\.  **CU-10 Crear un envío individual**, en su versión mínima: un bulto, sin validar franjas, con la tarifa calculada sobre un cuadro tarifario cargado por datos de inicialización.

2\.  **CU-13 Consultar envíos**, en el Backoffice.

3\.  Datos de inicialización: al menos un operador, un comercio con su `RelacionComercial`, una zona y una versión de cuadro tarifario publicada.

4\.  Persistencia real: `DbContext` de Envíos y de Administración con sus migraciones.

5\.  Un `ICurrentTenant` provisorio, porque Identity recién está comprometido para el 15/10. Por ejemplo, un operador y un comercio fijos tomados de la configuración, sólo en desarrollo.

6\.  `docker compose up` sin perfiles, levantando la API y el Worker.

Es el flujo de extremo a extremo que el ADR-0001 (sección 5, punto 5) pide para verificar la arquitectura.

# **4\. Preguntas abiertas**

| Tema | Estado | Detalle |
| :---- | :---- | :---- |
| Reprogramación pedida por el destinatario antes del primer intento | **Resuelta** | Se permite. Se agregaron T18 y T19 a la tabla de transiciones (sección 2.6). |
| Alta de envíos por API (CU-12) | **Resuelta** | Se implementa la API pública documentada del opcional 7.4 (sección 2.7). El ambiente de pruebas se resuelve con claves de prueba, que marcan los envíos con `EsPrueba` (casos de uso, sección 3). |
| Módulo propietario del tablero en vivo y de los reportes | **Resuelta** | El tablero (RF 29) y los reportes de cumplimiento (RF 30) combinan datos de Envíos, Planificación y Ejecución. Los resuelve **Seguimiento**, que ya escucha todos los cambios de estado para las notificaciones y los avisos (RF 27 y 28), manteniendo su propia tabla de lectura alimentada por esos eventos. La liquidación por comercio queda en Depósito y liquidaciones. |
| Quién registra la entrega y el intento fallido | **Resuelta** | **Envíos** registra la entrega y el intento (es dueño de `IntentoEntrega` y el RF 11 exige un único punto de cambio de estado). **Ejecución** recibe la sincronización de la PWA (CU-52), aplica la política de conflictos y llama a Envíos mediante `IEnviosModuleApi`. **Planificación** marca la parada como completada o fallida reaccionando al evento de Envíos por Outbox. |
| Actor de la rendición | **Resuelta** | Se divide en dos casos: el repartidor la inicia desde la PWA declarando qué vuelve (CU-54) y el operario de depósito la confirma escaneando lo que llegó (CU-55); recién la confirmación dispara T8. Queda un control cruzado entre lo declarado y lo recibido. |

# **5\. Próximos pasos**

Los tres pasos previstos se completaron: las preguntas abiertas del catálogo están resueltas (sección 4), las del detalle también ([casos-de-uso.md](casos-de-uso.md), sección 3), y cada caso de uso tiene hito, nivel y responsable en el [plan de casos de uso por hito](plan-casos-de-uso-por-hito.md).

# **6\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Borrador inicial a partir de la letra. | Ezequiel Marcenal |
| 0.2 | 30/09/2026 | Se resuelven la reprogramación antes del primer intento (T18 y T19 propuestas) y la API pública (sección 2.7); sugerencias para el tablero, los reportes y el registro de entregas. | Ezequiel Marcenal |
| 0.3 | 30/09/2026 | Se aceptan las sugerencias sobre el tablero, los reportes y el registro de entregas; la rendición se divide en CU-54 y CU-55. | Ezequiel Marcenal |
| 0.4 | 02/10/2026 | T18 y T19 confirmadas y T11 eliminada en la tabla de transiciones; se actualiza CU-61. | Ezequiel Marcenal |
| 0.5 | 03/10/2026 | Se actualizan las preguntas abiertas (T18 y T19 confirmadas, ambiente de pruebas de la API pública) y los próximos pasos. | Ezequiel Marcenal |
