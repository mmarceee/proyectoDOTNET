# Prueba del worker independiente — entrega del 29/10/2026

## Qué está implementado

`src/Logistica.Worker` es un ejecutable con `Microsoft.NET.Sdk.Worker`, su propio `Program.cs` y su propio
Dockerfile. Compose levanta `api` y `worker` como servicios distintos. La API registra `PublicadorOutbox`;
el Worker registra `ConsumidorRabbitMq`. Por lo tanto, iniciar sólo la API no ejecuta el consumidor.

El circuito de un aviso al comercio es:

```text
API: crear envío → PostgreSQL / outbox de Envíos
API: PublicadorOutbox → RabbitMQ
Worker: PrepararAvisos → entregas pendientes + outbox de Seguimiento
API: PublicadorOutbox → RabbitMQ / seguimiento.entregar-aviso
Worker: EntregarAviso → POST HTTPS al receptor del comercio + historial de intentos
```

El Worker es independiente como proceso y despliegue. Comparte PostgreSQL, RabbitMQ y configuración con la API.
Actualmente el publicador de TODOS los outbox vive en la API: si se apaga la API, los nuevos mensajes que el Worker
guarde en el outbox de Seguimiento esperan hasta que vuelva el publicador. Esto importa al explicar la arquitectura.

## Preparación

1. Iniciar Docker Desktop y esperar que `docker info` responda.
2. Configurar `.env` según el README, incluida `AVISOS_CLAVE_CIFRADO`, compartida por API y Worker.
3. Desde la raíz del repositorio:

   ```bash
   docker compose up -d --build --wait
   docker compose ps
   docker compose logs --tail 50 api worker
   ```

4. Abrir el Portal (`http://localhost:8080/portal/`, ajustando el puerto si cambió en `.env`), ingresar como comercio
   y seleccionar operador. En **Integraciones → Avisos**, crear una suscripción activa para `CambioDeEstado`.
5. Usar un receptor HTTPS público controlado por el equipo que registre los POST y permita devolver 200 o 503.
   Los destinos locales, privados y reservados están bloqueados por el emisor. El
   [receptor de demostración](../tools/Logistica.ReceptorDemo/README.md) permite alternar 200/503 desde un panel:
   ejecutar `dotnet run --project tools/Logistica.ReceptorDemo` y seguir su guía para el túnel HTTPS.
6. Devolver 200 y usar **Enviar prueba** para verificar URL y firma. Ese botón llama al receptor desde la API:
   **por sí solo no prueba RabbitMQ ni el Worker**.

Las URLs anteriores usan los puertos predeterminados. No ejecutar un segundo Worker desde Visual Studio o
`dotnet run` durante la prueba de parada: seguiría procesando aunque se detenga el contenedor.

## Prueba 1: demostrar la independencia y recuperar pendientes

1. Con el entorno iniciado al menos una vez, detener solamente el Worker:

   ```bash
   docker compose stop worker
   docker compose ps --all api worker
   ```

2. Comprobar que la API sigue respondiendo en `/health` y que el Portal permite **crear un envío nuevo** con el
   comercio y operador de la suscripción. Anotar su número. No reutilizar un envío anterior a la implementación
   de `EnvioAdmitido`: podría faltar su copia en Seguimiento.
3. Abrir RabbitMQ Management (`http://localhost:15672`, credenciales de `.env`) y buscar en **Queues**
   `seguimiento.preparar-avisos.EnvioAdmitido.v1`. Actualizar el panel: tras el ciclo del publicador (aprox. 2 s)
   debe aumentar `Ready`. El receptor no debe recibir el aviso automático de ese envío todavía.
4. Arrancar solamente el Worker:

   ```bash
   docker compose start worker
   docker compose logs --follow --tail 50 worker
   ```

5. Comprobar que el receptor recibe el POST con el número del envío y `prueba: false`, que se vacían los pendientes
   de ese flujo y que la entrega aparece exitosa en `/portal/integraciones/avisos/historial` al actualizar la vista.
   El paso intermedio por `seguimiento.entregar-aviso` puede durar muy poco para verlo en el panel.

**Evidencia:** API operativa mientras el Worker estaba detenido, mensaje esperando, y aviso recibido al arrancarlo.
El consumidor usa `BasicGet` (consulta periódica), así que RabbitMQ puede mostrar `Consumers = 0` incluso funcionando;
ese contador no sirve para decidir si este Worker está activo.

## Prueba 2: fallo transitorio y recuperación

1. Configurar el receptor para devolver 503. Crear otro envío nuevo.
2. Comprobar el intento fallido en el historial y el mensaje en una cola de espera:
   `seguimiento.entregar-aviso.espera.5s`, luego `.espera.30s` y luego `.espera.2m` si sigue fallando.
3. Antes del siguiente intento, hacer que el receptor devuelva 200.
4. Comprobar que la entrega pasa a exitosa, conserva sus intentos anteriores y el receptor recibe el mismo
   identificador de aviso en los reintentos. El receptor debe usar ese identificador para evitar efectos duplicados.

Los tiempos son esperas entre intentos, además del tiempo de HTTP y procesamiento. Las colas de espera usan TTL y
dead-letter routing para devolver el mensaje a la cola original. El Worker confirma el original sólo después de
que RabbitMQ confirme la publicación de la copia durable.

## Prueba 3: agotar reintentos y reenviar

1. Mantener el receptor devolviendo 503 y crear un tercer envío.
2. Esperar el intento inicial y los tres reintentos: esperas de 5 + 30 + 120 segundos, más procesamiento.
   Con respuestas 503 inmediatas son aproximadamente 2 min 35 s; con timeouts tarda más (hasta 10 s por HTTP).
3. Comprobar el estado **En cola de fallidos** en el historial y la presencia del mensaje en `logistica.fallidos`.
   Es una cola compartida: correlacionar con el aviso de la prueba, no sólo con su cantidad total de mensajes.
4. Devolver 200 en el receptor y solicitar el reenvío desde el historial. Actualizar hasta ver el éxito y comprobar
   que se conserva el identificador de aviso. El reenvío del historial crea un mensaje nuevo para la misma entrega;
   no implica eliminar automáticamente la copia anterior de `logistica.fallidos`.

## Pruebas automatizadas existentes

Con Docker activo, ejecutar desde la raíz:

```bash
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --filter "FullyQualifiedName~EntregarAvisosTests|FullyQualifiedName~AvisosRutaTests|FullyQualifiedName~HistorialAvisosTests"
dotnet test tests/Logistica.ArchitectureTests/Logistica.ArchitectureTests.csproj --filter "FullyQualifiedName~WorkerDependencyTests"
```

`EntregarAvisosTests` incluye el recorrido Portal → outbox → RabbitMQ → host consumidor → receptor con firma
verificada. El receptor es un adaptador en memoria y el host de pruebas se construye con los registros del módulo:
no arranca el ejecutable ni el contenedor real de `Logistica.Worker`. La prueba manual anterior completa esa evidencia.
`AvisosRutaTests` verifica un error transitorio a través de RabbitMQ y el envío de un mensaje inválido a fallidos.
Los tests de último fallo de avisos también ejercitan directamente el consumidor con el número final de intento.

Si Windows bloquea los DLL, el README ofrece `bash scripts/test-en-docker.sh` para ejecutar la suite en Linux.

## Alcance pendiente para la entrega

En la revisión del código del 27/10, el worker, los avisos, los reintentos y la cola de fallidos tienen implementación
y pruebas escritas. Eso no sustituye ejecutar la demostración completa con el receptor del equipo.
El 28/10 se incorporó el receptor de demostración en `tools/Logistica.ReceptorDemo`; se ejecuta por separado
y requiere un túnel HTTPS público para conectarlo al Worker.

El tablero operativo en tiempo real es CU-66: exige SignalR, grupos por operador y reconexión con recarga completa.
En este checkout no se encontraron un Hub ni registros `AddSignalR`/`MapHub`; verificar e integrar ese trabajo antes
de dar el requisito por cumplido. Aspire Dashboard es el tablero técnico de telemetría y no cubre CU-66.
