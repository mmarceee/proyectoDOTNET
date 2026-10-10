# Diseño de CU-40 · Armar una hoja de ruta

Estado: base de CU-40 implementada para desarrollo; cierre pendiente de aislamiento y sesión autenticada. Fecha: 09/10/2026. Ver [avance y pendientes de integración](avance-cu-40.md).

Este documento desarrolla el [modelo de dominio](../modelo-de-dominio.md), la especificación de [CU-40](../casos-de-uso.md) y las reglas acordadas con Lucas. La [especificación técnica](especificacion-tecnica-cu-40.md) concreta contratos, esquema, concurrencia, pantallas, integraciones y pruebas. Las decisiones funcionales fueron acordadas durante el análisis; las elecciones técnicas siguen la arquitectura existente.

## 1. Base existente en el modelo

`Ruta` es la raíz del agregado de Planificación. Contiene sus `Parada`; se modifican a través de la ruta.

| Entidad | Atributos documentados |
| :--- | :--- |
| Ruta | Id, RepartidorId, VehiculoId, Fecha, Estado, CreadaEn, DespachadaEn opcional |
| Parada | Id, RutaId, EnvioId, Orden, Estado, LlegadaEn opcional |

Estados de ruta: `Planificada`, `Despachada`, `EnCurso`, `Finalizada`. No se agrega `Cancelada` ni `Borrador` sin revisar el modelo.

Estados de parada: `Pendiente`, `Completada`, `Fallida`.

El modelo contempla agregar/quitar paradas, ordenarlas, moverlas y despachar. CU-40 cubre crear, agregar y, por ampliación acordada, modificar fecha, repartidor y vehículo mientras la ruta esté Planificada; los demás comportamientos pertenecen a sus respectivos casos de uso.

## 2. Decisiones ya acordadas

* Candidatos en `EnDeposito` o `Reprogramado`, sin otra parada activa.
* Una fecha programada debe coincidir con la ruta; si no existe, el despachador elige el día operativo al asignar. La asignación no sobrescribe la fecha programada.
* Una franja comprometida debe ser válida para el día de la ruta. Sin franja no hay restricción horaria adicional. Una ruta puede combinar franjas.
* Se usan las medidas de recepción, con respaldo de lo declarado para cada valor no medido.
* Se validan peso y volumen totales y que cada bulto entre individualmente en la caja, permitiendo rotación. Igualar la capacidad está permitido. No se calcula la distribución física conjunta.
* Se anticipan entidades, persistencia, consultas y datos iniciales de Administración necesarios para CU-40. Sus pantallas de gestión quedan en CU-04, CU-06 y CU-08.
* La selección se mantiene en pantalla; la ruta se persiste al confirmar con al menos un envío. Abandonar el formulario no reserva recursos.
* Una ruta Planificada con paradas reserva repartidor y vehículo para su fecha. Si queda vacía por CU-41, conserva su estado pero libera la reserva; agregar envíos nuevamente exige comprobar y reservar los recursos otra vez.
* Las rutas Despachadas o EnCurso mantienen ocupados ambos recursos para su fecha y bloquean una nueva asignación si siguen sin finalizar desde una fecha anterior. Completar o fallar paradas no las elimina ni libera por sí solo los recursos: se requiere que la ruta pase a Finalizada.
* Una ruta Finalizada no reserva recursos. La excepción de ruta vacía sólo corresponde a Planificada.
* Se permite modificar fecha, repartidor y vehículo de una ruta Planificada. Se revalidan todos sus envíos (fecha/franja, carga y máximo de paradas), la pertenencia al operador, el estado activo y la disponibilidad de los recursos. Desde Despachada estos datos quedan bloqueados.
* La modificación y el cambio de reservas se confirman en una sola transacción, con control de concurrencia sobre la ruta. Si alguna validación o reserva falla, se conservan los datos y las reservas anteriores. Una ruta Planificada vacía sigue sin reservar recursos.
* Modificar estos datos conserva las paradas y los envíos asignados, sin repetir T3/T13 ni sobrescribir sus fechas programadas.
* Cada confirmación exitosa conserva una copia del peso y las dimensiones usados para cada bulto de las paradas validadas, vinculada a la ruta, los envíos y el momento de validación. El volumen se calcula con esas dimensiones.
* Al agregar envíos, modificar fecha/repartidor/vehículo o despachar, se consultan las medidas actuales de los módulos dueños y se revalida la carga completa, usando recepción y respaldo declarado según las reglas acordadas. La copia histórica no sustituye esa consulta.
* Una nueva confirmación conserva su propia evidencia sin sobrescribir las anteriores. La evidencia y el cambio de ruta se guardan juntos; una operación rechazada no registra una confirmación exitosa.
* Se usa la versión de reglas vigente al confirmar la operación, no necesariamente la mostrada en la prevalidación. Si cambió el máximo de paradas y la selección ya no cumple, se rechaza la operación y se muestra el nuevo límite.
* Cada confirmación exitosa conserva el identificador de la versión de reglas aplicada y el máximo de paradas usado junto con la evidencia. Crear, agregar envíos, modificar la planificación y despachar revalidan con la versión vigente en ese momento.
* Publicar nuevas reglas no modifica retroactivamente una ruta ya Despachada o EnCurso ni su evidencia de despacho. Este criterio de Planificación no altera las versiones de reglas propias de cada envío para otros casos de uso.
* Los cambios de franjas de una zona se programan con una fecha de entrada en vigencia, por una persona con perfil Administrador del operador. Antes de esa fecha se atienden las franjas anteriores; desde esa fecha se atienden las nuevas.
* Al programar el cambio se identifican los compromisos afectados. Para cada envío con día definido (fecha programada o fecha de ruta), deben faltar más de 24 horas hasta el inicio más temprano entre la franja anterior y la nueva. No se modifican rutas Despachadas o EnCurso. Si no se cumplen estas condiciones, se propone una fecha de vigencia posterior.
* Los envíos existentes con entrega desde la fecha de vigencia se reprograman a la nueva franja y se revalida su planificación. Se conserva el historial y se notifica al destinatario y al comercio. Los nuevos pedidos usan la franja correspondiente a su día de entrega y no pueden crear compromisos con una franja que dejó de atenderse para esa fecha.
* Para un envío sin fecha programada ni ruta que defina el día, la franja se resuelve al planificar según el día elegido. No se mantiene una franja que la zona dejó de atender. El cambio programado no se pospone automáticamente por nuevos pedidos.
* CU-40 consulta franjas y días aplicables a la fecha de la ruta. Esta vigencia por fecha se distingue de la versión de reglas operativas, que se consulta al confirmar.
* FranjaHoraria incorpora VigenteDesde y VigenteHasta opcional, como fechas inclusivas. Cada cambio crea una nueva franja y cierra la anterior el día previo al inicio de la nueva, conservando los horarios históricos.
* El Administrador selecciona explícitamente qué franja anterior reemplaza. La nueva franja conserva ReemplazaAId, que referencia una franja de la misma zona; no se deduce el reemplazo por similitud de horarios. Una franja inicial no tiene reemplazo anterior.
* Programar el cambio de franjas es una operación completa: se comprueban todos los envíos y rutas afectados antes de confirmar. Si alguno impide aplicarlo, se identifica el motivo y se conserva la configuración anterior sin reprogramaciones parciales.
* Las vigencias de las franjas, las reprogramaciones, los cambios necesarios en la planificación y los mensajes de notificación en Outbox se confirman en una misma transacción. Las comprobaciones incluyen conflictos con solicitudes simultáneas, no sólo los datos mostrados previamente.
* El envío de avisos ocurre después de confirmar. Un fallo de entrega de la notificación se reintenta mediante Outbox y Worker sin deshacer la operación ni sus compromisos; no se exige que el destinatario reciba el aviso para confirmar el cambio.
* Para los envíos afectados que están AsignadoARuta en una ruta Planificada, el cambio programado aplica T19: pasan a Reprogramado, reciben la nueva franja y salen de la ruta eliminando su parada. Conservan el día de entrega definido; si sólo lo determinaba la ruta, se conserva como FechaEntregaProgramada al reprogramar.
* Antes de confirmar se muestran los envíos que saldrán de sus rutas. Después quedan disponibles para que el despachador los reasigne mediante T13, sin reasignación automática. Si una ruta queda vacía, libera repartidor y vehículo conforme a las reglas acordadas. La evidencia histórica de sus confirmaciones se conserva aunque se elimine la parada.
* ValidacionRuta representa la evidencia inmutable de cada confirmación exitosa: referencia a Ruta, fecha/hora, responsable, tipo de operación (creación, agregado, modificación o despacho), versión de reglas, máximo de paradas, vehículo y sus capacidades y dimensiones de carga, y detalle de envíos/bultos con los pesos y dimensiones usados. También conserva la fecha de la ruta y el repartidor de esa confirmación para identificar la planificación evaluada.
* Cada ValidacionRuta pertenece a la ruta y contiene sus detalles de carga por bulto. No depende de la existencia de Parada ni se elimina en cascada al quitarla. Se agrega un nuevo registro por confirmación; no se modifica el historial para reflejar datos actuales. Los identificadores de envío y bulto son referencias a los módulos dueños, sin navegar sus entidades internas.
* Un operador puede tener varios usuarios con perfil Despachador, según CU-02. Son cuentas individuales que comparten los recursos del operador; el perfil no representa un puesto único. Las comprobaciones de disponibilidad se realizan dentro de ese operador.
* Comprobar disponibilidad y reservar repartidor/vehículo es una operación indivisible. Si dos confirmaciones compiten, la primera válida obtiene la reserva; la otra informa qué recurso dejó de estar disponible y conserva la selección del formulario para elegir otro y reconfirmar. No crea una ruta parcial ni asigna sus envíos. Se aplica a creación, cambio de recursos y recuperación de la reserva de una ruta vacía.

## 3. Responsabilidades

| Parte | Responsabilidad |
| :--- | :--- |
| Ruta | Proteger el estado editable, la colección de paradas, los duplicados dentro de la ruta y el orden de inserción. |
| Parada | Representar la asignación de un envío completo; nace Pendiente. No es un repositorio independiente. |
| Servicio de dominio de planificación | Validar fecha/franja, máximo de paradas y carga con datos de candidatos, vehículo y reglas, incluyendo las paradas existentes. |
| Handler de CU-40 | Obtener los datos por contratos, comprobar recursos, invocar las reglas y coordinar la transacción. |
| Infraestructura | Persistir, aplicar restricciones de unicidad y detectar conflictos de concurrencia. |
| Backoffice | Mostrar candidatos y límites, conservar la selección y gestionar la reconfirmación después de un conflicto. |

`OperadorId` en Ruta, Parada y ValidacionRuta aplica el aislamiento previsto por el modelo general. Los hijos lo copian de Ruta. Los identificadores de Envíos y Administración son referencias entre módulos; no implican navegar sus entidades internas.

El servicio recibe datos de carga y franjas propios de Planificación, construidos a partir de los contratos. No recibe la entidad interna `Vehiculo` de Administración ni `Envio` de Envíos. La firma conceptual `Vehiculo.admiteCarga(bulto: Bulto, ...)` del modelo se concreta sin dependencias entre dominios de módulos distintos, según la especificación técnica.

## 4. Capacidades de los contratos

Estas son las capacidades necesarias. Las firmas objetivo y los DTO se definen en la sección 3 de la especificación técnica; su implementación queda pendiente.

| Dueño | Capacidades necesarias |
| :--- | :--- |
| Administración | Consultar repartidores y vehículos activos del operador; capacidades y dimensiones; franjas y sus días aplicables a una fecha de entrega; versión de reglas vigente y máximo de paradas. |
| Envíos | Consultar candidatos con estado, zona, fecha, franja y bultos; consultar los envíos de una ruta existente para revalidar; asignar un conjunto mediante T3/T13 y registrar sus eventos. |
| Depósito | Consultar las medidas numéricas registradas de los bultos autorizados. Hoy deben agregarse a la persistencia. |
| Planificación | Crear ruta, agregar envíos, modificar fecha/repartidor/vehículo de una ruta Planificada, obtener detalle y prevalidar selección o modificación para Backoffice. Sus contratos públicos para otros módulos se definen según consumidores concretos. |

Los contratos deben distinguir datos inexistentes o no accesibles y conflictos de asignación. El operador y el responsable proceden de la sesión autenticada.

## 5. Garantías exigidas por CU-40

* Índice único parcial sobre EnvioId para paradas Pendiente (RF 15).
* Concurrencia optimista sobre Ruta. Cambiar sus paradas debe actualizar también la fila de la raíz para que su versión detecte modificaciones simultáneas.
* Protección frente a reservas simultáneas de repartidor o vehículo según las reglas de ocupación acordadas, incluyendo rutas Despachadas o EnCurso de fechas anteriores y la recuperación de reservas de una ruta Planificada vacía. La especificación técnica define exclusión breve por operador e índices de reserva por fecha.
* Ruta, paradas, transiciones de Envíos y mensajes Outbox se confirman juntos o se revierten juntos.
* Un conflicto no confirma automáticamente un subconjunto. Se revierte la solicitud, se identifican los envíos indisponibles y se solicita reconfirmar el resto.
* La relectura después del rollback debe hacerse con un contexto limpio, evitando reutilizar entidades cuyo estado en memoria quedó modificado por el intento fallido.
* La autorización exige perfil Despachador y acceso al operador de la ruta.

## 6. Cierre del diseño técnico

La especificación técnica resuelve los puntos que estaban abiertos:

1. Reserva indivisible: bloqueo breve de la fila del operador mediante Administración dentro de IUnidadDeTrabajo; índices únicos para reservas por día y comprobación de rutas de días anteriores.
2. Modificación y errores: contratos, RevisionEsperada, revalidación completa y cambio atómico de reservas.
3. Evidencia: tablas de ValidacionRuta y detalles por bulto, independientes de Parada, con consultas paginadas y operación adicional CambioFranjaProgramado para la revalidación de CU-04.
4. Reglas: instante de evaluación y versión consultados después de obtener exclusión; datos reconsultados en la transacción, con nuevo límite informado ante rechazo.
5. Franjas: reemplazos encadenados, incompatibilidades sin cambio automático de fecha, operaciones por lote, T18/T19 y notificación también de cambios de oferta a envíos aún sin día definido.
6. Aislamiento y arquitectura: OperadorId, contratos de módulos, políticas de sesión y participación de los contextos en la transacción antes de leer/bloquear.
7. Interfaz y pruebas: páginas, endpoints, errores estructurados y matriz de aceptación con concurrencia sobre PostgreSQL real.

La guía de aislamiento compartida por Ezequiel el 09/10/2026 modifica la dependencia de infraestructura: CU-40 reutiliza su filtro global Tenant, TenantSaveChangesInterceptor, InquilinoFijo y pruebas, sin duplicar filtros manuales. En este checkout los cambios aún no están integrados; se verifican antes de conectar PlanificacionDbContext. La especificación técnica detalla esta integración, el aislamiento de tablas hijas, la traducción de TenantMismatchException a 404 y la protección explícita del SQL de bloqueo sobre Operador, que es global.

No se consideran implementados estos puntos por estar definidos. La ampliación completa de CU-04, la autenticación, Outbox/publicador y los CU de ejecución conservan los entregables identificados en la especificación técnica.

## 7. Criterios para cerrar la definición

El ciclo de vida relevante, los atributos, responsabilidades, contratos, errores, esquema, flujo y pruebas están definidos en este documento, el modelo general y la especificación técnica. Se puede comenzar la implementación de CU-40 siguiendo el orden de la sección 10 de dicha especificación. Su cierre funcional exige las verificaciones e integraciones indicadas allí.
