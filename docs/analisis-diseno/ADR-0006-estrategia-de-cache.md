# **ADR-0006 · Estrategia de caché e invalidación**

| Estado | Propuesto — versión alineada con la implementación, pendiente de revisión por el equipo |
| :---- | :---- |
| **Versión** | 0.4 |
| **Fecha** | 24 de octubre de 2026 |
| **Responsable** | Cristian Reyes |
| **Equipo** | Equipo 1 - Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Estrategia de caché e invalidación (obligatorio, letra 6.7 y 8.2) |
| **Relacionado con** | ADR-0001 (sección 2.5); ADR-0002 (sección 4); ADR-0003; Propuesta de stack y entorno de desarrollo (sección 3.6) |

**Este registro define qué se guarda en la caché distribuida (Valkey), con qué clave, cuánto dura, cuándo se invalida y cómo se mide. Se cachean dos casos de uso con perfiles de acceso opuestos: el seguimiento público, que se lee mucho y cambia seguido, y la configuración del operador (tarifario, reglas, zonas e identidad visual), que se lee mucho y casi no cambia. La clave siempre incluye el operador, para que la caché no se convierta en una fuga entre inquilinos.**

> Las decisiones que siguen abiertas están en la sección 5. Con la revisión del equipo, el registro pasa a *Aceptado* (versión 1.0).

# **1\. Contexto y restricciones**

- **La letra (6.7)** exige caché distribuida sobre **al menos dos casos de uso con perfiles de acceso diferentes**, con expiración e invalidación **coherentes con el dominio** en cada caso, y con **métrica de aciertos y fallos visible en el tablero técnico**. Sugiere el seguimiento público y la configuración tarifaria.
- **El monitoreo del 22/10** pide la caché funcionando con su métrica de aciertos (plan por hito, sección 2.3). Los casos que la usan son CU-60 (seguimiento, caché con métrica) y CU-05 (tarifario, caché con invalidación); CU-04, CU-06 y CU-07 piden invalidar la caché al guardar (requerimiento especial de cada ficha).
- **Stack ya elegido** (Propuesta de stack, sección 3.6): Valkey como caché distribuida, accedida mediante `IDistributedCache` para que los casos de uso no dependan del proveedor. La telemetría va por OpenTelemetry al Aspire Dashboard.
- **Multitenancy (ADR-0002, sección 4):** el filtro "Tenant" no protege lo que ya está en caché. Toda clave de datos de un operador incluye su `OperadorId`.
- **Comunicación (ADR-0001, sección 2.5):** invalidar la caché es una reacción que *puede perderse sin dejar datos inconsistentes*: si falla, la entrada expira sola. Por eso no necesita garantías propias; se hace donde ocurre el cambio (configuración) o reutilizando los eventos que ya viajan al Worker (seguimiento).
- **Configuración versionada (letra 6.6):** las versiones publicadas de tarifario y reglas no se modifican; un envío conserva la versión con la que fue admitido. Una versión puede publicarse con vigencia futura.
- **Una sola instancia de la API** (el equipo está eximido del escalado horizontal, 6.15), pero la letra pide caché *distribuida*: la decisión no debe depender de que haya una sola instancia.
- **Resiliencia (opcional 7.4, elegido por el equipo):** se probará interrumpir la caché; el sistema tiene que seguir funcionando sin ella.

# **2\. Decisión adoptada**

## **2.1 Patrón: cache-aside sobre `IDistributedCache` y Valkey**

Cada lectura cacheada sigue el mismo recorrido:

1. Se busca la clave en Valkey. Si está (**acierto**), se devuelve.
2. Si no está (**fallo**), se lee de PostgreSQL —con el filtro "Tenant", como siempre—, se guarda en Valkey con su expiración y se devuelve.
3. Al cambiar el dato, después de confirmar la transacción, se **borra** la clave. La próxima lectura la vuelve a cargar.

Se cachean **DTOs de lectura serializados en JSON**, nunca entidades del dominio: lo que sale de la caché no se puede modificar ni guardar por error.

## **2.2 Una pieza común en BuildingBlocks, con el operador en la clave**

El acceso a la caché se concentra en **una sola pieza** común a todos los módulos, igual que el filtro y el interceptor del ADR-0002. **Ya está implementada** (Ezequiel Marcenal, 19/10/2026, PR #36); este registro fundamenta su diseño y define cómo la usan los casos de uso:

- **Interfaz** `ICacheDistribuida` en `Logistica.SharedKernel` (C# puro), con `ObtenerOCrearAsync(cache, clave, crear, duracion)` e `InvalidarAsync(cache, clave)`. `cache` nombra el grupo de datos (`seguimiento`, `tarifario`…) y es la etiqueta de la métrica. Application la usa sin conocer Valkey.
- **Implementación** `CacheDistribuida` en `Logistica.BuildingBlocks.Infrastructure`, sobre `IDistributedCache`: Valkey cuando hay cadena de conexión (`ConnectionStrings:Valkey`) y la memoria del proceso cuando no la hay, para depurar sin Valkey y en las pruebas.
- **La implementación arma la clave completa** con el `OperadorId` de `ICurrentTenant`: `{cache}:{OperadorId}:{clave}`. El caso de uso pasa sólo su parte (por ejemplo, `vigente`), así **no puede olvidarse del operador**. Es la misma idea que el filtro "Tenant": la protección no depende de la disciplina de cada desarrollador.
- **Si el dato depende también del comercio**, el comercio lo pone el caso de uso en su parte de la clave. No se agrega automáticamente: una invalidación desde el Backoffice, que no tiene comercio, no borraría lo guardado desde el Portal.
- **Sin inquilino, falla cerrado:** si `ICurrentTenant.OperadorId` es nulo, usar la caché lanza `TenantMismatchException`, igual que agregar un mensaje al Outbox. Un caso de uso nunca debería llegar ahí sin operador; si llega, es un error de programación y se ve, en lugar de pasar desapercibido.
- **Valores `null`:** no se guardan; la próxima lectura vuelve a la fuente.

## **2.3 Caso 1 · Seguimiento público (CU-60): se lee mucho y cambia seguido**

| Aspecto | Decisión |
| :---- | :---- |
| Qué se guarda | La vista pública del envío: estado, ventana estimada, historial sin datos del personal, nombre de pila y localidad (CU-60, paso 3). Nunca la dirección completa ni el teléfono. |
| Clave | `seguimiento:{OperadorId}:{EnvioId}`. El operador y el envío salen del token firmado, después de validar la firma; nunca de un valor sin verificar. |
| Expiración | **60 segundos, absoluta.** |
| Invalidación | Los eventos de cambio de estado que Envíos ya publica por Outbox y RabbitMQ (ADR-0003) llegan al Worker, donde el consumidor `InvalidarSeguimiento` borra la clave del envío. El consumidor corre con el operador del sobre del mensaje como inquilino, así arma la misma clave que guardó la API. |

**Eventos que invalidan:** `EnvioAsignadoARuta`, `EnvioDesasignadoDeRuta`, `EnvioEnTransito`, `EnvioEntregado`, `IntentoFallidoRegistrado`, `EnvioReprogramado`, `EnvioCancelado`, `EnvioReintegradoADeposito`, `EnvioExtraviado` y `DevolucionIniciada`. Cada caso de uso nuevo que publique un cambio de estado visible para el destinatario se suma a esta lista.

- `EnvioAdmitido` no se incluye: es el estado inicial y, antes de crearse el envío, nadie pudo guardar su seguimiento.
- **La recepción en el depósito (T2) no publica un evento** (CU-30). Hasta que exista, el destinatario puede ver el estado anterior durante, como máximo, la expiración de un minuto. Se propone agregar `EnvioRecibidoEnDeposito` al módulo de Envíos, que también serviría para los avisos al comercio (CU-64).

**Por qué la invalidación va por mensajería y no por un evento en memoria.** El ADR-0001 (sección 2.5) prevé eventos en memoria para invalidar la caché, y así se hace con la configuración (sección 2.4). En el seguimiento no alcanza: el cambio de estado ocurre en la API, pero el módulo Seguimiento procesa los hechos de Envíos en el Worker (ADR-0003, sección 2.4), y un evento en memoria de la API no llega a otro proceso. Además, esos eventos ya existen y viajan por RabbitMQ para las notificaciones (CU-62) y los avisos (CU-64): la invalidación es un consumidor más de los mismos mensajes, sin infraestructura nueva.

- **No necesita inbox:** borrar dos veces la misma clave no hace daño, así que procesar un mensaje repetido no cambia el resultado.
- **Si el mensaje se demora o se pierde,** la entrada vence sola: la invalidación acelera la actualización, y la expiración garantiza el límite.

**Por qué 60 segundos.** La expiración acota lo que la invalidación no cubre: los cambios sin evento (T2), la demora de la mensajería y la carrera de la sección 2.5. Un minuto es poco para el destinatario, y alcanza para absorber las recargas repetidas de la página, que es lo que más tráfico genera. *(La versión 0.3 justificaba el valor con la cantidad de entregas que faltan antes del envío; ese dato no se implementó en CU-60.)*

**Sobre el ADR-0002, sección 4.** Allí se decía que el seguimiento usa *el token como clave*. Se usan `OperadorId` y `EnvioId` (que el token contiene), porque el consumidor que invalida recibe el `EnvioId` en el evento, pero no el token. Se aceptó al implementar CU-60 (19/10/2026) y se actualizó esa línea del ADR-0002.

## **2.4 Caso 2 · Configuración del operador: se lee mucho y casi no cambia**

Incluye el cuadro tarifario (CU-05) y las reglas operativas (CU-06). Se leen en cada alta de envío (la tarifa de cada bulto), en cada entrega e intento fallido (las reglas del envío) y en la PWA, y cambian sólo cuando el administrador publica una versión.

| Dato | Clave | Expiración | Invalidación |
| :---- | :---- | :---- | :---- |
| **Una versión publicada** del tarifario (la tabla de precios ya calculada, `TablaTarifaria`) | `tarifario:{OperadorId}:v{Numero}` | **24 horas** | **Nunca hace falta:** una versión publicada no cambia (6.6). La expiración sólo libera memoria. |
| **Una versión publicada** de las reglas, por número o por Id | `reglas:{OperadorId}:v{Numero}` / `reglas:{OperadorId}:id:{VersionId}` | **24 horas** | Nunca hace falta. La clave por Id es la que usa Envíos: cada envío guarda la versión con la que fue admitido (`VersionReglasId`). |
| **Cuál es la versión vigente** (el puntero) | `tarifario:{OperadorId}:vigente` / `reglas:{OperadorId}:vigente` | **10 minutos** | El handler de publicar la borra después de guardar (CU-05 y CU-06). Además, guarda hasta cuándo es válida (ver abajo). |

**Coherencia con el dominio.**
- **Separar "la versión" de "cuál está vigente"** aprovecha que las versiones son inmutables: el contenido grande se cachea mucho tiempo sin riesgo, y lo único que se invalida es un puntero chico.
- **La vigencia futura no se resuelve con un evento:** una versión publicada hoy para regir el 1/11 no dispara nada el 1/11. Por eso el puntero guarda, junto con el número vigente, **`ValidoHasta`: el inicio de la próxima versión programada**. Cada lectura lo compara con la hora actual y, si ya pasó, borra el puntero y lo vuelve a cargar: ese mismo instante se empieza a leer la nueva versión, sin esperar a que venzan los 10 minutos.
- **Un envío ya admitido no consulta el puntero:** usa la versión que guardó (`VersionTarifarioId`, `VersionReglasId`), así que nunca le afecta una publicación posterior.

**Invalidación directa, sin evento.** El cambio (publicar) y la lectura cacheada ocurren en el mismo módulo y en el mismo proceso (la API). El handler de publicar, después de confirmar, llama a `InvalidarAsync` de la misma clase que lee (`TarifarioVigente`, `ReglasVigentes`). Un evento en memoria (ADR-0001, sección 2.5) agregaría un paso más sin desacoplar nada: emisor y receptor serían el mismo módulo. Si otro módulo cacheara esta configuración, ahí sí correspondería un evento.

**Fuera del alcance actual: zonas e identidad visual.** También se leen mucho y cambian poco, y el mismo mecanismo serviría (`zonas:{OperadorId}`, `identidad:{OperadorId}`, 10 minutos, invalidación al guardar en CU-04 y CU-07). No se cachean porque la letra pide dos casos de uso y no hay una necesidad medida. Mientras no se cacheen, el requerimiento de CU-04 y CU-07 de invalidar al guardar no aplica: cada lectura va a la base.

## **2.5 Invalidación: después de confirmar, y con un límite de tiempo siempre**

- La clave se borra **después** de que la transacción se confirma. Si se borrara antes, otra lectura podría volver a cargar el dato viejo mientras la transacción todavía no terminó.
  - **Configuración:** el handler de publicar llama a `InvalidarAsync` después de `GuardarCambiosAsync` (sección 2.4).
  - **Seguimiento:** el evento se publica desde el Outbox, que sólo existe si la transacción se confirmó, y el Worker lo procesa después (sección 2.3).
- Aun así queda una carrera posible: una lectura que leyó la base justo antes de la confirmación y escribe en la caché justo después del borrado. Por eso **toda entrada tiene expiración**, incluso las que se invalidan: el dato viejo, en el peor caso, dura lo que su expiración (1 minuto en el seguimiento, 10 minutos en un puntero de configuración).
- Si borrar la clave falla (Valkey caído), la pieza común registra una advertencia, suma un error a la métrica y no deshace la operación: la entrada expira sola (ADR-0001, sección 2.5).

## **2.6 Si Valkey no responde: se sigue sin caché**

La caché acelera, no es la fuente de verdad. Si Valkey no responde, la pieza común **registra el error, cuenta la falla y lee de PostgreSQL**, sin intentar guardar. La conexión no espera a Valkey para arrancar (`AbortOnConnectFail` desactivado), no encola pedidos mientras está caído (`BacklogPolicy.FailFast`) y espera **como máximo 1 segundo**, para que una caché caída no vuelva lento todo el sistema. Es el comportamiento que verificarán las pruebas de resiliencia (7.4); `CacheDistribuidaTests` ya comprueba que, con Valkey inalcanzable, se responde desde la fuente en menos de 5 segundos.

## **2.7 Métrica de aciertos y fallos**

La pieza común publica, con OpenTelemetry (`System.Diagnostics.Metrics`), un medidor `Logistica.Cache` con tres contadores. Lo registran **los dos procesos que usan la caché**: la API (lecturas del seguimiento, el tarifario y las reglas, e invalidaciones al publicar) y el Worker (invalidaciones del seguimiento). Se exportan al Aspire Dashboard cada 10 segundos (`OTEL_METRIC_EXPORT_INTERVAL`, en Docker Compose), cada uno con su servicio (`logistica-api`, `logistica-worker`).

| Contador | Cuándo suma | Etiqueta |
| :---- | :---- | :---- |
| `logistica.cache.aciertos` | La clave estaba en la caché | `cache` (`seguimiento`, `tarifario`, `reglas`) |
| `logistica.cache.fallos` | No estaba y se leyó de la fuente | `cache` |
| `logistica.cache.errores` | Leer, guardar o invalidar en Valkey falló | `cache` |

El Aspire Dashboard muestra los tres por grupo y, con ellos, la tasa de aciertos (`aciertos / (aciertos + fallos)`). **Nunca se etiqueta con el `OperadorId` ni con el envío**: no aporta a la métrica y multiplicaría las series del tablero.

**Cómo se ve en la demostración** (escenario 5): en Aspire Dashboard → *Metrics* → `logistica-api` → `logistica.cache.*`, filtrando por `cache = tarifario`. Se calcula un precio dos veces (un fallo y después un acierto), se publica una versión nueva y se vuelve a calcular: aparece otro fallo, porque la publicación borró el puntero.

# **3\. Alternativas consideradas y motivo de su descarte**

- **Caché en memoria del proceso (`IMemoryCache`):** es la más simple y la más rápida, pero no es distribuida (6.7 lo exige) y con dos instancias de la API cada una tendría su propia copia: invalidar en una no invalida la otra. Además, el Worker no la vería.
- **`HybridCache` de .NET (memoria local más caché distribuida):** reduce la latencia y evita que muchas lecturas simultáneas carguen el mismo dato a la vez. Se descarta porque agrega una caché local por instancia, y entonces invalidar exige coordinar todas las instancias (el mismo problema anterior), y porque la métrica de aciertos por caso es más difícil de exponer. Si el tráfico del seguimiento lo justificara, es la evolución natural, porque usa el mismo `IDistributedCache` por debajo.
- **Que cada módulo use `IDistributedCache` directamente:** se descarta por el mismo motivo que los filtros por módulo en el ADR-0002: serían varias copias del armado de claves, y una sola que olvide el `OperadorId` es una fuga entre inquilinos. Tampoco habría una métrica uniforme.
- **Invalidar sólo por expiración, sin borrar claves:** obliga a elegir entre expiraciones largas, con datos viejos después de cada cambio, y cortas, con pocos aciertos. La letra pide además una invalidación coherente con el dominio.
- **Invalidar la configuración por Outbox y RabbitMQ:** garantiza la entrega, pero es desproporcionado para una reacción que puede perderse sin dejar datos inconsistentes (ADR-0001, sección 2.5), y el cambio y la lectura ocurren en el mismo proceso: alcanza con invalidar directamente (sección 2.4). **En el seguimiento sí se usa la mensajería**, pero no por la garantía de entrega, sino porque Seguimiento procesa los hechos en el Worker y los eventos ya viajan por RabbitMQ para las notificaciones y los avisos (sección 2.3).
- **Usar el token de seguimiento como clave:** ver sección 2.3.

# **4\. Consecuencias**

**A favor**
- Los dos perfiles de la letra quedan cubiertos con políticas distintas y explicables: expiración corta más invalidación para lo que cambia seguido, e inmutabilidad más un puntero invalidable para lo que casi no cambia.
- El aislamiento entre operadores no depende de cada caso de uso: la clave la arma una sola pieza, con el operador de `ICurrentTenant`.
- Una caída de Valkey degrada el rendimiento, pero no la disponibilidad.
- Las métricas por caso permiten mostrar en la demostración el efecto de la caché (escenario 5: un cambio de tarifa en caliente invalida el puntero y la lectura siguiente es un fallo).

**En contra / riesgos**
- **Datos viejos acotados:** hasta 1 minuto en el seguimiento (por ejemplo, la cantidad de entregas que faltan) y, en una carrera poco probable, hasta 10 minutos en un puntero de configuración.
- **Invalidaciones olvidadas:** un caso de uso nuevo que cambie un dato cacheado y no borre su clave mostrará datos viejos hasta que expiren. En el seguimiento, cada evento nuevo de cambio de estado tiene que sumarse a `InvalidarSeguimiento.Tipos`, como se hizo con `EnvioExtraviado` (CU-21) y `DevolucionIniciada` (CU-20). Se mitiga con una prueba de integración por cada dato cacheado (cambiar y leer de nuevo).
- **Cambios de estado sin evento:** la recepción en el depósito (T2) no publica un evento, así que el seguimiento puede mostrar el estado anterior hasta un minuto (sección 2.3).
- **Serialización:** un cambio en un DTO cacheado puede dejar entradas con la forma vieja. Se mitiga incluyendo una versión del formato en la clave cuando el DTO cambie, o vaciando la caché al desplegar.
- **Un servicio más que operar:** Valkey en desarrollo (Docker Compose) y en producción.

**Qué dificulta a futuro:** con varias instancias de la API, el diseño sigue funcionando (la caché es compartida), pero si se agregara una caché local para ganar latencia (`HybridCache`), habría que distribuir las invalidaciones a todas las instancias.

**Pruebas que acompañan la decisión**

| # | Qué se prueba | Dónde | Estado |
| :-: | :---- | :---- | :---- |
| 1 | **Aislamiento:** con la misma clave lógica, el operador A y el operador B leen entradas distintas; sin inquilino, usar la caché falla cerrado. | `CacheDistribuidaTests`; `ConfigurarTarifarioTests` y `ConfigurarReglasTests` (cada operador ve su versión vigente) | Hecha |
| 2 | **Invalidación:** después de publicar un tarifario o unas reglas, y de un evento de cambio de estado del envío, la lectura siguiente devuelve el dato nuevo. | `ConfigurarTarifarioTests`, `ConfigurarReglasTests` (recorrido completo hasta el precio o la regla nueva); `CacheSeguimientoTests` | Hecha |
| 3 | **Vigencia futura:** una versión programada no rige antes de su fecha, y el puntero se recarga cuando llega su inicio (`ValidoHasta`). | `ConfigurarTarifarioTests`, `ConfigurarReglasTests` (la versión programada no rige todavía) | Parcial: falta probar el paso de la fecha con un reloj fijo |
| 4 | **Resiliencia:** con Valkey caído, se lee de la fuente sin esperar y `cache.errores` aumenta. | `CacheDistribuidaTests` (Valkey inalcanzable, caché que falla) | Hecha en la pieza común; falta la prueba de punta a punta con Valkey detenido (opcional 7.4) |
| 5 | **Métrica:** una lectura repetida suma un fallo y después un acierto. | `CacheDistribuidaTests` | Hecha |

# **5\. Decisiones abiertas**

| Tema | Estado |
| :---- | :---- |
| Valores de expiración (60 s, 10 min, 24 h) | **Cerrada (v0.4):** se confirman. Si las métricas del tablero mostraran pocos aciertos, se revisan. |
| Alcance | **Cerrada:** seguimiento, tarifario y reglas. Zonas e identidad visual quedan como extensión posible (sección 2.4). |
| Quién publica el evento de cambio de estado para el seguimiento | **Cerrada:** Envíos lo publica por Outbox; Seguimiento lo consume en el Worker (`InvalidarSeguimiento`, sección 2.3). |
| Evento de la recepción en el depósito (T2) | **Abierta:** proponer a Ezequiel (CU-30) un `EnvioRecibidoEnDeposito`, que también serviría para los avisos al comercio (CU-64). |
| Aprobación del equipo | **Abierta:** revisión de Lucas y Ezequiel para pasar a *Aceptado* (v1.0). |

# **6\. Referencias**

- Letra del laboratorio: 6.6 (configuración versionada), 6.7 (caché distribuido), 6.15 (escalado), 7.4 (pruebas de resiliencia), 8.2 (registros de decisión).
- ADR-0001, sección 2.5 (reacciones que pueden perderse, como la invalidación de la caché).
- ADR-0003, sección 2.4 (consumidores en el Worker).
- ADR-0002, secciones 2.5 y 4 (token de seguimiento; claves de caché con `OperadorId`).
- Propuesta de stack y entorno de desarrollo, sección 3.6; Guía de decisiones tecnológicas, sección 6.1.
- Casos de uso CU-04, CU-05, CU-06, CU-07 y CU-60.
- Microsoft Learn: *Distributed caching in ASP.NET Core* e *`HybridCache` library in ASP.NET Core*.

# **7\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 18/10/2026 | Borrador inicial, escrito con IA a pedido del responsable, para revisar con el equipo. | Cristian Reyes |
| 0.2 | 19/10/2026 | Se alinea con la pieza común implementada por Ezequiel Marcenal (PR #36): `ICacheDistribuida`, falla cerrado con excepción, memoria del proceso sin Valkey, espera máxima de 1 s, nombres de los contadores y comercio en la clave a cargo del caso de uso. | Cristian Reyes |
| 0.3 | 19/10/2026 | Se confirma la clave del seguimiento (EnvioId) y se registra qué usa la caché para el 22/10: tarifario, reglas y seguimiento. En el seguimiento, los eventos de Envíos se consumen en el Worker sólo para borrar la entrada. | Cristian Reyes |
| 0.4 | 24/10/2026 | Se alinea el texto con lo implementado: el seguimiento se invalida con los eventos de Envíos que consume el Worker (lista de eventos, `EnvioExtraviado` incluido, y T2 sin evento); la configuración, directamente en el handler de publicar, con el puntero `ValidoHasta`; zonas e identidad visual quedan fuera del alcance; el Worker también exporta la métrica; pruebas con su clase y estado; decisiones abiertas cerradas o actualizadas. Escrito con IA a pedido del responsable. | Cristian Reyes |
