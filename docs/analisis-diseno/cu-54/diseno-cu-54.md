# CU-54 · Iniciar la rendición

Propuesta de implementación del 19/10/2026. No se implementa código en esta etapa. Las decisiones propuestas deben acordarse antes de codificar, especialmente el contrato con Ezequiel para CU-55.

## Base documental

- [CU-54 y CU-55](../casos-de-uso.md): el repartidor declara, el depósito verifica. CU-54 exige una ruta EnCurso y admite registrar sin conexión.
- [Transiciones](../tabla-de-transiciones-de-estados.md): T8 corresponde al operario de depósito y sólo reintegra envíos EnTransito que no llegaron a intentarse. Un intento fallido ya aplica T7 y T9/T10 en CU-18.
- [Modelo de dominio](../modelo-de-dominio.md): Rendicion y LineaRendicion pertenecen a Ejecución; la rendición admite cero o más líneas. La devolución al comercio es otro proceso.
- [ADR-0001](../ADR-0001-estilo-arquitectonico.md): coordinación en Application; comunicación por I<Modulo>ModuleApi y resultados en Contracts/Results; persistencia propia por módulo.
- [ADR-0002](../ADR-0002-estrategia-de-multitenancy.md): identidad y operador de la sesión, con aislamiento en servidor; conservar también la separación local definida para la PWA en ADR-0004.
- [ADR-0003](../ADR-0003-outbox-y-mensajeria.md): eventos necesarios y cambios se confirman juntos.
- [ADR-0004](../ADR-0004-resolucion-de-conflictos-de-sincronizacion.md): IniciarRendicion es una operación sincronizable, cifrada, ordenada e idempotente; cada operación tiene su propia transacción.

## Reglas ya definidas

1. Sólo el repartidor activo asignado a la ruta de su operador puede iniciar la rendición; la ruta debe estar EnCurso.
2. El repartidor declara qué bultos devuelve y deja una observación cuando un bulto no vuelve.
3. La rendición queda pendiente de confirmación por depósito.
4. CU-54 no aplica T8, no registra recepción física, no declara extravíos y no finaliza la ruta. Tampoco libera las reservas del repartidor y del vehículo.
5. Sin conexión, la PWA conserva la declaración cifrada y la sincroniza después; guardada localmente y registrada en servidor son estados diferentes.
6. El servidor comprueba pertenencia y consistencia. El cliente no decide el estado final de los envíos ni de las líneas.

## Decisiones propuestas

| Situación | Propuesta para acordar |
| --- | --- |
| Un bulto vuelve | Declaración explícita por bulto, agrupada por envío. No hace falta escaneo en CU-54; CU-55 sí verifica mediante escaneo. |
| Un bulto que salió no vuelve | Marcar que no se devuelve y exigir observación por línea. Se conserva para depósito; no se convierte automáticamente en Extraviado. |
| Todos los envíos se entregaron | Permitir iniciar rendición sin devoluciones. CU-55 puede cerrar la ruta después de verificar que no hay bultos pendientes. |
| Carga parcial de CU-51 | Distinguir envíos completos que aplicaron T5, lecturas parciales y bultos nunca cargados. Un bulto nunca cargado no es un faltante al regreso. Las lecturas parciales requieren conciliación explícita con depósito, sin inventar una transición del envío. |
| Ya existe una rendición | Una rendición por ruta. Mismo OperacionId devuelve el resultado previo; otro identificador no crea una segunda. La confirmación enviada no se edita silenciosamente. |
| Rendición iniciada | La ruta permanece EnCurso, pero la PWA entra en modo de rendición y bloquea nuevas operaciones de jornada para esa ruta. Debe acordarse la guarda equivalente de servidor con CU-17/18/51/55. |
| Estados resueltos antes de rendir | Mostrar el resultado conocido; no pedir que se devuelva un envío Entregado o Devuelto ni tratar un Extraviado ya declarado como una nueva pérdida. Un cambio incompatible con la declaración exige revisión. |

La documentación no define todos estos casos límite. Son propuestas, no reglas ya aprobadas del ADR.

## Ajustes al modelo a acordar con CU-55

El modelo actual define declarada, recibida y estadoFinal, pero no representa completamente una línea pendiente ni una observación por bulto.

- Rendicion: mantener rutaId, iniciadaEn, finalizadaEn opcional y observaciones; agregar los datos de pertenencia y auditoría necesarios (operador, repartidor y operación de origen).
- LineaRendicion: identificar envío/bulto; conservar la declaración y su observación. El valor de recibida no debe confundirse con una verificación ya realizada: hasta CU-55 es información aún no confirmada.
- estadoFinal debe ser nulo mientras no exista una resolución; no usar por defecto EntregadoEnCalle ni agregar automáticamente Extraviado para representar una falta.
- Acordar si las líneas abarcan todo el manifiesto con su clasificación, o sólo los bultos a conciliar. La opción debe contemplar la comparación con entregas en calle de CU-55 y la carga parcial de CU-51.
- Definir cómo CU-55 registra discrepancias y conserva abierta la rendición cuando corresponde; no cerrar con diferencias sin una regla acordada.

La entidad y las tablas siguen siendo de Ejecución. Si el Backoffice de CU-55 se ubica en Depósito, consulta y confirma mediante IEjecucionModuleApi; no escribe tablas de Ejecución desde su infraestructura.

## Flujo propuesto de la PWA

1. En Mi ruta, ofrecer Rendir ruta para la ruta en curso.
2. Mostrar envíos y bultos a conciliar, distinguiendo no intentados, intento fallido y carga incompleta. Mostrar los resueltos como referencia.
3. Permitir declarar por bulto cuáles vuelven y exigir observaciones para los que deberían volver pero no vuelven.
4. Mostrar un resumen antes de confirmar: devueltos, no devueltos con observación y operaciones anteriores pendientes o rechazadas.
5. Guardar borrador y confirmación como datos cifrados, con control entre pestañas. Confirmar genera una operación inmutable IniciarRendicion.
6. Indicar Pendiente de sincronizar hasta recibir Aplicada/YaAplicada. Después mostrar Pendiente de confirmación del depósito.
7. Tras recargar, cambiar de conexión o recuperar la sesión, conservar el estado y evitar una segunda declaración de la misma ruta.

La hoja actual no contiene suficiente detalle sobre carga física e intentos de esta ruta. Ampliar su contrato/copia cifrada con la información mínima necesaria, sin inferirla del estado de la parada ni del número histórico de intentos del envío. Actualizar la versión de esquema y conservar la compatibilidad de las copias existentes.

## Sincronización y dependencias

Reutilizar IndexedDB y la cola existentes, sin Redis ni otra cola de cliente. Separar las claves de borrador de carga y rendición: el almacenamiento actual usa borradores de carga indexados por ruta y no se puede reutilizar esa clave sin distinguir el tipo.

La operación lleva OperacionId UUIDv7, DispositivoId, Secuencia, RutaId, EnvioId nulo, OcurridoEn y el CorrelacionId W3C 00-{32 hex}-{16 hex}-01 generado al confirmar. El payload contiene la base de conciliación y las declaraciones por bulto; los detalles se fijarán junto al modelo.

La vista de rendición debe combinar la hoja descargada con operaciones anteriores guardadas en el dispositivo. No puede declarar pendiente de devolución un envío entregado localmente que todavía no sincronizó.

Las operaciones anteriores se procesan antes de IniciarRendicion, incluso cuando ocupen varios lotes de 20. No iniciar una rendición definitiva sobre una entrega o carga previa rechazada sin revisión. El procesamiento actual sólo propaga rechazos a operaciones del mismo envío dentro del lote y rechazos de carga a la misma ruta: ampliar la dependencia de la rendición, que es de ruta, también para reintentos y límites entre lotes. No basta con aumentar la Secuencia.

Una revisión de ruta distinta no debe producir rechazos automáticos si el cambio lo causó una operación anterior válida de la misma cola. Definir una base de conciliación consistente y validar el manifiesto en servidor después de aplicar sus dependencias; no reutilizar ciegamente la revisión descargada de CU-51.

## Servidor y organización

- Domain/Rendiciones: Rendicion, LineaRendicion e invariantes.
- Application/Features/IniciarRendicion: validación y coordinación; Application/Features/ConsultarRendicion para recuperar el estado después de sincronizar o volver a ingresar.
- Http.Contracts: payload y respuestas consumidas por la PWA.
- IEjecucionModuleApi: consultas/acciones que necesite CU-55, implementadas internal en Application; Results inmutables.
- IPlanificacionModuleApi e IEnviosModuleApi: lectura del manifiesto y resultados de ejecución de la ruta, sin acceso cruzado a entidades o DbContext.
- Infrastructure/Persistence: repositorios, configuraciones y migración en schema ejecucion; unicidad de la rendición por ruta y filtro de operador.
- PWA: página de rendición, servicio y adaptación de persistencia/sincronización común. Evitar duplicar un segundo motor de cola o mezclar su estado con el borrador de carga.

En una transacción: comprobar ruta y vínculo activo, resolver idempotencia/unicidad, validar bultos y dependencias, persistir rendición/líneas y registrar la operación aplicada. Coordinar el bloqueo con la confirmación de CU-55 para que no compitan con un cierre o cambios de jornada. Un fallo revierte el conjunto.

No se agrega un evento de cambio de estado de envío en CU-54 porque no hay transición. Si se requiere aviso de rendición iniciada, acordar el consumidor antes de agregar un evento al outbox; el Backoffice puede listar pendientes mediante consulta.

## Pruebas de aceptación

- Ruta propia EnCurso; negar otra ruta, otro operador, vínculo inactivo y ruta finalizada o sólo despachada.
- Declaración completa, sin devoluciones, con observaciones por bulto y con devoluciones parciales.
- Carga parcial: distinguir no cargados de no devueltos y preservar lecturas de bultos que requieren conciliación.
- Intentos de esta ruta frente a intentos de jornadas anteriores; Reprogramado/EnDevolucion no vuelven a transicionar en CU-54.
- Rechazar duplicados, bultos ajenos y declaraciones incompatibles sin efectos parciales.
- Reenvío tras perder la respuesta, dos pestañas y dos operaciones distintas para la misma ruta: una sola rendición.
- Borrador y operación cifrados, recarga sin red, cuota agotada y recuperación de sesión; no exponer datos al cambiar de cuenta.
- Carga/entrega/intento pendientes antes de rendir, rechazo previo y dependencias que atraviesan varios lotes.
- Rollback antes del commit; no aplicar T8, finalizar ni liberar recursos al iniciar.
- Recuperar la rendición iniciada desde el servidor y dejar un contrato utilizable por CU-55.
- Arquitectura y regresión de CU-50/51/52.

## Orden de implementación

1. Acordar con Ezequiel modelo, conciliación de carga parcial, caso sin devoluciones y contrato CU-54/CU-55.
2. Implementar agregado, persistencia y operación del servidor con pruebas de transacción e idempotencia.
3. Ampliar la consulta/copia de la hoja con la base necesaria para rendir.
4. Generalizar almacenamiento/sincronización para varias operaciones y sus dependencias.
5. Implementar pantalla, borrador, confirmación y recuperación del estado pendiente.
6. Verificar offline y preparar ejemplos reproducibles para CU-55.
