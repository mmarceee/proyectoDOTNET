# **ADR-0006 · Estrategia de caché e invalidación**

| Estado | Propuesto — borrador pendiente de revisión por el responsable y el equipo |
| :---- | :---- |
| **Versión** | 0.3 |
| **Fecha** | 19 de octubre de 2026 |
| **Responsable** | Cristian Reyes |
| **Equipo** | Equipo 1 - Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Estrategia de caché e invalidación (obligatorio, letra 6.7 y 8.2) |
| **Relacionado con** | ADR-0001 (sección 2.5); ADR-0002 (sección 4); ADR-0003; Propuesta de stack y entorno de desarrollo (sección 3.6) |

**Este registro define qué se guarda en la caché distribuida (Valkey), con qué clave, cuánto dura, cuándo se invalida y cómo se mide. Se cachean dos casos de uso con perfiles de acceso opuestos: el seguimiento público, que se lee mucho y cambia seguido, y la configuración del operador (tarifario, reglas, zonas e identidad visual), que se lee mucho y casi no cambia. La clave siempre incluye el operador, para que la caché no se convierta en una fuga entre inquilinos.**

> Las marcas **[a confirmar]** señalan decisiones que el borrador propone con una recomendación, pero que el equipo tiene que validar antes de pasar a *Aceptado*.

# **1\. Contexto y restricciones**

- **La letra (6.7)** exige caché distribuida sobre **al menos dos casos de uso con perfiles de acceso diferentes**, con expiración e invalidación **coherentes con el dominio** en cada caso, y con **métrica de aciertos y fallos visible en el tablero técnico**. Sugiere el seguimiento público y la configuración tarifaria.
- **El monitoreo del 22/10** pide la caché funcionando con su métrica de aciertos (plan por hito, sección 2.3). Los casos que la usan son CU-60 (seguimiento, caché con métrica) y CU-05 (tarifario, caché con invalidación); CU-04, CU-06 y CU-07 piden invalidar la caché al guardar (requerimiento especial de cada ficha).
- **Stack ya elegido** (Propuesta de stack, sección 3.6): Valkey como caché distribuida, accedida mediante `IDistributedCache` para que los casos de uso no dependan del proveedor. La telemetría va por OpenTelemetry al Aspire Dashboard.
- **Multitenancy (ADR-0002, sección 4):** el filtro "Tenant" no protege lo que ya está en caché. Toda clave de datos de un operador incluye su `OperadorId`.
- **Comunicación (ADR-0001, sección 2.5):** invalidar la caché es una reacción que *puede perderse sin dejar datos inconsistentes*: se hace con un evento en memoria, y si falla, la entrada expira sola.
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
| Clave | `seguimiento:{OperadorId}:{EnvioId}`. El operador y el envío salen del token firmado, después de validar la firma; nunca de un valor sin verificar. *(Confirmado el 19/10 al implementar CU-60.)* |
| Expiración | **60 segundos, absoluta.** **[a confirmar el valor]** |
| Invalidación | Cada cambio de estado del envío borra su clave, con un evento en memoria publicado por Envíos después de confirmar (ADR-0001, sección 2.5). |
| Por qué así | La invalidación cubre los cambios del propio envío. Pero la ventana estimada en tránsito incluye *cuántas entregas quedan antes de la suya* (CU-60, requerimiento 5), que cambia cuando se entregan **otros** envíos de la ruta. No se invalidan todos los envíos de la ruta en cada entrega: la expiración corta acota ese dato a un minuto de antigüedad. Es el componente de más tráfico: aunque dure poco, absorbe las recargas repetidas del destinatario. |

**Sobre el ADR-0002, sección 4.** Allí se dice que el seguimiento usa *el token como clave*. Este borrador propone usar `OperadorId` y `EnvioId` (que el token contiene), porque Envíos conoce el `EnvioId` al cambiar el estado, pero no necesariamente el token. Se aceptó al implementar CU-60 (19/10/2026) y se actualizó esa línea del ADR-0002.

## **2.4 Caso 2 · Configuración del operador: se lee mucho y casi no cambia**

Incluye el cuadro tarifario (CU-05), las reglas operativas (CU-06), las zonas y franjas (CU-04) y la identidad visual (CU-07). Se leen en cada alta de envío, en cada planificación y en cada página con la marca del operador, y cambian sólo cuando el administrador publica o guarda.

| Dato | Clave | Expiración | Invalidación |
| :---- | :---- | :---- | :---- |
| **Una versión publicada** de tarifario o reglas | `tarifario:{OperadorId}:v{Numero}` / `reglas:{OperadorId}:v{Numero}` | **24 horas** | **Nunca hace falta:** una versión publicada no cambia (6.6). La expiración sólo libera memoria. |
| **Cuál es la versión vigente** | `tarifario:{OperadorId}:vigente` / `reglas:{OperadorId}:vigente` | **10 minutos**, y **nunca más allá del inicio de la próxima versión programada** | Se borra al publicar (`TarifarioPublicado`, `ReglasPublicadas`, en memoria; CU-05 y CU-06). |
| **Zonas y franjas** | `zonas:{OperadorId}` | 10 minutos | Se borra al guardar una zona o una franja (CU-04, paso 7). |
| **Identidad visual** | `identidad:{OperadorId}` | 10 minutos | Se borra al guardar (CU-07). |

**Coherencia con el dominio.**
- **Separar "la versión" de "cuál está vigente"** aprovecha que las versiones son inmutables: el contenido grande se cachea mucho tiempo sin riesgo, y lo único que se invalida es un puntero chico.
- **La vigencia futura no se resuelve con un evento:** una versión publicada hoy para regir el 1/11 no dispara nada el 1/11. Por eso el puntero `vigente` se guarda con una expiración que **no supera la fecha de inicio de la próxima versión programada**: ese día la caché vence sola y se lee la nueva.
- **Un envío ya admitido no consulta el puntero:** usa la versión que guardó (`VersionTarifarioId`), así que nunca le afecta una publicación posterior.

**Implementado para el 22/10:** el tarifario (CU-05), las reglas operativas (CU-06, también por Id de versión, porque cada envío guarda la suya) y el seguimiento (CU-60). Zonas e identidad visual pueden sumarse después, con el mismo mecanismo.

## **2.5 Invalidación: después de confirmar, y con un límite de tiempo siempre**

- La clave se borra **después** de que la transacción se confirma. Si se borrara antes, otra lectura podría volver a cargar el dato viejo mientras la transacción todavía no terminó.
- Aun así queda una carrera posible: una lectura que leyó la base justo antes de la confirmación y escribe en la caché justo después del borrado. Por eso **toda entrada tiene expiración**, incluso las que se invalidan: el dato viejo, en el peor caso, dura lo que su expiración (1 minuto en el seguimiento, 10 minutos en un puntero de configuración).
- Si borrar la clave falla (Valkey caído), se registra en el log y no se deshace la operación: la entrada expira sola (ADR-0001, sección 2.5).

## **2.6 Si Valkey no responde: se sigue sin caché**

La caché acelera, no es la fuente de verdad. Si Valkey no responde, la pieza común **registra el error, cuenta la falla y lee de PostgreSQL**, sin intentar guardar. La conexión no espera a Valkey para arrancar (`AbortOnConnectFail` desactivado), no encola pedidos mientras está caído (`BacklogPolicy.FailFast`) y espera **como máximo 1 segundo**, para que una caché caída no vuelva lento todo el sistema. Es el comportamiento que verificarán las pruebas de resiliencia (7.4); `CacheDistribuidaTests` ya comprueba que, con Valkey inalcanzable, se responde desde la fuente en menos de 5 segundos.

## **2.7 Métrica de aciertos y fallos**

La pieza común publica, con OpenTelemetry (`System.Diagnostics.Metrics`), un medidor `Logistica.Cache` con tres contadores, exportados al Aspire Dashboard cada 10 segundos:

| Contador | Cuándo suma | Etiqueta |
| :---- | :---- | :---- |
| `logistica.cache.aciertos` | La clave estaba en la caché | `cache` (`seguimiento`, `tarifario`, `reglas`, `zonas`, `identidad`) |
| `logistica.cache.fallos` | No estaba y se leyó de la fuente | `cache` |
| `logistica.cache.errores` | Leer, guardar o invalidar en Valkey falló | `cache` |

El Aspire Dashboard muestra los tres por grupo y, con ellos, la tasa de aciertos (`aciertos / (aciertos + fallos)`). **Nunca se etiqueta con el `OperadorId` ni con el envío**: no aporta a la métrica y multiplicaría las series del tablero.

# **3\. Alternativas consideradas y motivo de su descarte**

- **Caché en memoria del proceso (`IMemoryCache`):** es la más simple y la más rápida, pero no es distribuida (6.7 lo exige) y con dos instancias de la API cada una tendría su propia copia: invalidar en una no invalida la otra. Además, el Worker no la vería.
- **`HybridCache` de .NET (memoria local más caché distribuida):** reduce la latencia y evita que muchas lecturas simultáneas carguen el mismo dato a la vez. Se descarta por ahora porque agrega una caché local por instancia, y entonces invalidar exige coordinar todas las instancias (el mismo problema anterior), y porque la métrica de aciertos por caso es más difícil de exponer. Si el tráfico del seguimiento lo justificara, es la evolución natural, porque usa el mismo `IDistributedCache` por debajo. **[a confirmar]**
- **Que cada módulo use `IDistributedCache` directamente:** se descarta por el mismo motivo que los filtros por módulo en el ADR-0002: serían varias copias del armado de claves, y una sola que olvide el `OperadorId` es una fuga entre inquilinos. Tampoco habría una métrica uniforme.
- **Invalidar sólo por expiración, sin borrar claves:** obliga a elegir entre expiraciones largas, con datos viejos después de cada cambio, y cortas, con pocos aciertos. La letra pide además una invalidación coherente con el dominio.
- **Invalidar por Outbox y RabbitMQ:** garantiza la entrega, pero es desproporcionado para una reacción que puede perderse sin dejar datos inconsistentes (ADR-0001, sección 2.5): la expiración ya cubre ese caso.
- **Usar el token de seguimiento como clave:** ver sección 2.3.

# **4\. Consecuencias**

**A favor**
- Los dos perfiles de la letra quedan cubiertos con políticas distintas y explicables: expiración corta más invalidación para lo que cambia seguido, e inmutabilidad más un puntero invalidable para lo que casi no cambia.
- El aislamiento entre operadores no depende de cada caso de uso: la clave la arma una sola pieza, con el operador de `ICurrentTenant`.
- Una caída de Valkey degrada el rendimiento, pero no la disponibilidad.
- Las métricas por caso permiten mostrar en la demostración el efecto de la caché (escenario 5: un cambio de tarifa en caliente invalida el puntero y la lectura siguiente es un fallo).

**En contra / riesgos**
- **Datos viejos acotados:** hasta 1 minuto en el seguimiento (por ejemplo, la cantidad de entregas que faltan) y, en una carrera poco probable, hasta 10 minutos en un puntero de configuración.
- **Invalidaciones olvidadas:** un caso de uso nuevo que cambie un dato cacheado y no borre su clave mostrará datos viejos hasta que expiren. Se mitiga con una prueba de integración por cada dato cacheado (cambiar y leer de nuevo).
- **Serialización:** un cambio en un DTO cacheado puede dejar entradas con la forma vieja. Se mitiga incluyendo una versión del formato en la clave cuando el DTO cambie, o vaciando la caché al desplegar.
- **Un servicio más que operar:** Valkey en desarrollo (Docker Compose) y en producción.

**Qué dificulta a futuro:** con varias instancias de la API, el diseño sigue funcionando (la caché es compartida), pero si se agregara una caché local para ganar latencia (`HybridCache`), habría que distribuir las invalidaciones a todas las instancias.

**Pruebas que acompañan la decisión** (las de la pieza común ya están en `CacheDistribuidaTests`; las de cada dato se agregan con su caso de uso)
1. **Aislamiento:** con la misma clave lógica, el operador A y el operador B leen entradas distintas; sin inquilino, usar la caché falla cerrado.
2. **Invalidación:** después de publicar un tarifario, de cambiar el estado de un envío y de guardar una zona, la lectura siguiente devuelve el dato nuevo.
3. **Vigencia futura:** el puntero `vigente` no se guarda más allá del inicio de la próxima versión programada.
4. **Resiliencia:** con Valkey detenido, el seguimiento y el alta de envíos siguen funcionando y `cache.errores` aumenta.
5. **Métrica:** una lectura repetida suma un fallo y después un acierto.

# **5\. Decisiones abiertas**

| Tema | Por qué importa |
| :---- | :---- |
| Valores de expiración (60 s, 10 min, 24 h) | Son propuestas razonables, no medidas. Se pueden ajustar con las métricas del tablero. |
| Alcance para el 22/10 | Mínimo exigido: seguimiento y tarifario. Reglas, zonas e identidad visual con el mismo mecanismo, en el mismo hito o después. |
| Quién publica el evento de cambio de estado para el seguimiento | Lo emite Envíos (Ezequiel); lo consume Seguimiento (Cristian). Hay que acordar el nombre y dónde se suscribe. |

# **6\. Referencias**

- Letra del laboratorio: 6.6 (configuración versionada), 6.7 (caché distribuido), 6.15 (escalado), 7.4 (pruebas de resiliencia), 8.2 (registros de decisión).
- ADR-0001, sección 2.5 (eventos en memoria para invalidar la caché).
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
