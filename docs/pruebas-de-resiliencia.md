# Pruebas de resiliencia (opcional 7.4)

| | |
| :---- | :---- |
| **Estado** | Implementadas, en la suite de integración y el CI |
| **Fecha** | 08/11/2026 |
| **Equipo** | Equipo 1: Ezequiel Marcenal (responsable), Lucas Ottonello y Cristian Reyes |

La letra (sección 7.4) pide interrumpir deliberadamente la caché, la cola, la base de datos y un servicio externo, y documentar para cada caso el comportamiento degradado **esperado** y el **observado**. Este documento cubre los cuatro casos.

---

## 1. Cómo se prueba

Cada caso es una prueba de integración en `tests/Logistica.IntegrationTests/Resiliencia/`. Todas siguen tres pasos:

1. **Antes:** con la dependencia arriba, se comprueba que el flujo funciona.
2. **Durante:** se detiene el contenedor de la dependencia (`docker stop`, a través de Testcontainers) con la API y, si corresponde, el Worker corriendo. Se comprueba el comportamiento degradado.
3. **Después:** se vuelve a iniciar el contenedor y se comprueba que el sistema se recupera solo, sin reiniciar la API ni el Worker.

La interrupción es real: el proceso de la dependencia se detiene y su puerto deja de aceptar conexiones. No se usan dobles ni conexiones simuladas.

Cada prueba registra sus mediciones en la salida (tiempos de respuesta, errores contados, intentos y tiempo de recuperación). Los valores observados de este documento salen de la corrida del 08/11/2026 en Docker; en otra máquina los tiempos cambian, pero las pruebas comprueban los límites (por ejemplo, el 503 en menos de 5 s y el envío sin caché en menos de 3 s).

**Detalle técnico:** Testcontainers publica cada puerto del contenedor en un puerto libre del host, y ese puerto puede cambiar al detener y volver a iniciar el contenedor. La API ya tiene la dirección vieja, así que estas pruebas publican en un puerto fijo elegido al azar (`ContenedorInterrumpible`).

Se corren con el resto de la suite (`bash scripts/test-en-docker.sh`) y en el CI. Para correr sólo estas:

```bash
bash scripts/test-en-docker.sh Resiliencia
```

| Caso | Dependencia | Prueba |
| :---: | :---- | :---- |
| 1 | Caché (Valkey) | `ValkeyCaidoTests` |
| 2 | Cola (RabbitMQ) | `RabbitMqCaidoTests` |
| 3 | Base de datos (PostgreSQL) | `PostgresCaidoTests` |
| 4 | Servicio externo (servidor de correo SMTP) | `SmtpCaidoTests` |

---

## 2. Caso 1: caché (Valkey) detenida

**Flujo probado:** crear un envío desde el Portal (CU-10). Lee el tarifario y las reglas vigentes, que están en la caché (ADR-0006).

| | Comportamiento |
| :---- | :---- |
| **Esperado** | La caché acelera pero no es la fuente de verdad (ADR-0006, sección 2.6). Sin Valkey, cada lectura falla enseguida (`BacklogPolicy.FailFast`, espera máxima de 1 s), se lee de PostgreSQL y se suma un error en `logistica.cache.errores`. Los envíos se siguen creando sin demora visible. Cuando Valkey vuelve, la conexión se restablece sola y la caché se vuelve a llenar. |
| **Observado** | Con Valkey detenido, los dos envíos se crearon (201) en **86 ms** y **69 ms**, y se contaron **8 errores** de caché: las lecturas del tarifario y las reglas fueron a PostgreSQL. Al volver a iniciar Valkey, la API volvió a guardar el tarifario y las reglas **0,4 s** después, sin reiniciarse. Coincide con lo esperado. |

**Efecto en el tablero en vivo (CU-66):** SignalR usa Valkey como backplane entre la API y el Worker. Sin Valkey:
- la foto inicial del tablero se sigue cargando por HTTP;
- las novedades en vivo no llegan;
- las novedades que manda el Worker fallan y se reintentan por la mensajería.

Con estas pruebas se configuró el backplane en FailFast con 1 s de espera, igual que la caché. Antes, cada aviso esperaba hasta 5 s en la cola del cliente de Valkey, y cada posición que sincroniza la PWA demoraba esos 5 s. Esto se verificó revisando el código; no tiene una prueba propia.

---

## 3. Caso 2: cola (RabbitMQ) detenida

**Flujo probado:** crear un envío desde el Portal. Su evento `EnvioAdmitido.v1` pasa por el outbox de Envíos y RabbitMQ, y lo procesa el Worker (ADR-0003).

| | Comportamiento |
| :---- | :---- |
| **Esperado** | La API no depende del broker para atender: el evento se guarda en el outbox en la misma transacción que el envío. Sin RabbitMQ, el envío se crea igual y su evento queda pendiente (`PublicadoEn` nulo), sin perderse. El publicador de la API y el consumidor del Worker registran una advertencia y reintentan la conexión cada 5 s. Cuando el broker vuelve, se reconectan solos, el evento se publica y el Worker lo procesa una sola vez (inbox). Lo que depende del Worker (avisos, correos, indicadores) se atrasa, pero no se pierde. |
| **Observado** | Con el broker detenido, el envío se creó (201) en **52 ms**. A los 5 s su evento seguía pendiente en el outbox. Con el broker de nuevo arriba, la API publicó el evento **4,6 s** después, en la primera reconexión (se reintenta cada 5 s), y el Worker lo procesó en el mismo lapso, sin reiniciarse ninguno de los dos. Coincide con lo esperado. |

---

## 4. Caso 3: base de datos (PostgreSQL) detenida

**Flujo probado:** crear un envío desde el Portal y consultar `/health`.

| | Comportamiento |
| :---- | :---- |
| **Esperado** | Sin base no hay datos, así que la API no puede atender los casos de uso; la degradación consiste en fallar bien. La API sigue viva y responde enseguida un **503** con `Retry-After: 5` y ProblemDetails, en lugar de un 500 o un request colgado. `/health` informa **Unhealthy** (503). Los servicios de fondo (publicador del outbox, cierre diario, depuración de posiciones, consumidores) atrapan el error y reintentan, así que el proceso no se cae. Cuando la base vuelve, la misma API vuelve a atender. Las conexiones que quedaron en el pool se descartan al fallar, así que puede haber algún 503 más antes de normalizarse. |
| **Observado** | Con la base detenida, crear un envío respondió **503** con `Retry-After: 5` y ProblemDetails en **41 ms**, y `/health` respondió 503 *Unhealthy*. Con la base de nuevo arriba, `/health` volvió a *Healthy* enseguida y el primer envío se creó **0,1 s** después, sin ninguna respuesta fallida en el medio: Npgsql vació el pool en la primera falla. Coincide con lo esperado; el 503 adicional que se preveía no apareció. |

**Cambios hechos con esta prueba:**
- **`/health` chequea PostgreSQL** (`PostgresHealthCheck`, con un límite de 3 s). Antes respondía *Healthy* siempre. Valkey y RabbitMQ no se chequean a propósito: sin ellos la API funciona degradada, y una plataforma no debería considerarla caída. En Azure Container Apps `/health` es la sonda de arranque.
- **`BaseDeDatosNoDisponibleExceptionHandler`** convierte una falla de conexión con PostgreSQL en 503. Un error de SQL sigue siendo un 500, porque es un defecto y no una caída.

**Decisión: no se reintenta en la API.** `EnableRetryOnFailure` de EF Core no admite las transacciones explícitas que usan varios casos de uso (por ejemplo, la sincronización de la PWA, ADR-0004). Reintentar es responsabilidad del cliente, que recibe el `Retry-After`. La PWA ya reintenta sola, porque guarda las operaciones en su cola local.

---

## 5. Caso 4: servicio externo (servidor de correo) detenido

**Flujo probado:** crear un envío con correo del destinatario. El Worker le manda el aviso "Recibimos tu envío" por SMTP (CU-62); en desarrollo es Mailpit y en producción Brevo.

| | Comportamiento |
| :---- | :---- |
| **Esperado** | El envío se crea igual: el aviso es asíncrono. El Worker intenta mandar el correo, registra el intento fallido en la notificación (`Intentos`, `UltimoError`) y lanza el error, así que la mensajería lo reintenta a los 5 s, 30 s y 2 min (ADR-0003, sección 2.6). Si el servidor vuelve dentro de esa ventana, un reintento entrega el mismo correo y la notificación queda *Enviada*. Si no vuelve, el mensaje termina en la cola de fallidos y se puede reenviar desde CU-09. |
| **Observado** | El envío se creó (201). El primer intento falló: la notificación quedó *Pendiente*, con 1 intento y el error "El servidor de correo no aceptó el mensaje: SocketException". Al volver a iniciar Mailpit, el reintento de los 5 s entregó el correo **4,8 s** después, en el **intento 2**, y la notificación quedó *Enviada*. Coincide con lo esperado. |

El otro servicio externo, el receptor de avisos de los comercios (CU-63/64), tiene el mismo esquema de reintentos. `EntregarAvisosTests` lo cubre con un receptor que responde error y después acepta.

---

## 6. Para verlo a mano (defensa)

Con el entorno completo levantado (`docker compose up -d --build --wait`):

| Caso | Interrumpir | Qué mirar | Restablecer |
| :---: | :---- | :---- | :---- |
| 1 | `docker compose stop valkey` | Crear un envío en el Portal funciona; en Aspire Dashboard aumenta `logistica.cache.errores` | `docker compose start valkey` |
| 2 | `docker compose stop rabbitmq` | El envío se crea; `SELECT "Tipo", "PublicadoEn" FROM envios.outbox_messages ORDER BY "OcurridoEn" DESC LIMIT 5;` muestra el evento pendiente; los logs de `api` y `worker` dicen que reintentan | `docker compose start rabbitmq`: el evento se publica y el Worker lo procesa |
| 3 | `docker compose stop postgres` | `curl -i http://localhost:8080/health` responde 503 *Unhealthy*; crear un envío responde 503 con `Retry-After` | `docker compose start postgres`: `/health` vuelve a 200 sin reiniciar la API |
| 4 | `docker compose stop mailpit` | Crear un envío con correo del destinatario: en la base, la notificación tiene `Intentos` > 0 y un `UltimoError` | `docker compose start mailpit`: el correo aparece en http://localhost:8025 en el reintento siguiente |

---

## Historial de versiones

| Versión | Fecha | Descripción |
| :---: | :---: | :---- |
| 1.0 | 08/11/2026 | Versión inicial: los cuatro casos con su prueba de integración, el chequeo de PostgreSQL en `/health`, el 503 con la base caída y el backplane de SignalR en FailFast. |
