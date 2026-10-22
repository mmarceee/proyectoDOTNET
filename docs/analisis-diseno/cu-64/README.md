# CU-64 · Entregar los avisos a los comercios

| Campo | Contenido |
| :---- | :---- |
| **Estado** | Implementado, pendiente de revisión del equipo |
| **Fecha** | 21/10/2026 |
| **Responsable** | Ezequiel Marcenal |
| **Equipo** | Equipo 1: Ezequiel Marcenal, Lucas Ottonello y Cristian Reyes |

Referencias: [caso de uso](../casos-de-uso.md), [guía de integración de CU-63](../cu-63/integracion-cu64.md),
[ADR-0002](../ADR-0002-estrategia-de-multitenancy.md) y [ADR-0003](../ADR-0003-outbox-y-mensajeria.md).

## Resumen

Ante cada cambio de estado de un envío, el Worker envía un aviso firmado a cada suscripción activa del comercio que
corresponda. Cada entrega registra sus intentos, se reintenta sola con la espera creciente de la mensajería y, agotados
los reintentos, queda *En cola de fallidos* y su mensaje pasa a `logistica.fallidos`. Todo el código está en el módulo
Seguimiento, que ejecuta el Worker; la firma, el cifrado del secreto y el emisor HTTP son los de CU-63, sin cambios.

## Decisiones

### 1. De dónde sale el comercio de un envío

El ADR-0003 (sección 2.2) fija que el inquilino del sobre sale de la sesión, *nunca del llamador ni del contenido del
evento*. La entrega, el intento fallido o el paso a tránsito los publican el repartidor o el operador, cuya sesión no
tiene comercio: el sobre de esos eventos llega sólo con el operador.

Por eso Envíos publica **`EnvioAdmitido.v1`** al crear el envío (T1, ya previsto en la tabla de transiciones y en
CU-10). Lo publica desde la sesión del comercio, así su sobre trae el `ComercioId`. Seguimiento guarda con ese dato una
copia mínima del envío, **`EnvioSeguido`** (identificador, operador, comercio y número), y los eventos posteriores
buscan ahí el comercio a avisar, dentro del filtro del operador del mensaje. Ningún consumidor lee el inquilino del
contenido de un evento.

Como el orden entre colas no está garantizado (ADR-0003, sección 2.4), un evento puede llegar antes que el alta de su
envío: el consumidor lanza `EnvioNoRegistradoException` y la mensajería lo reintenta a los 5 s, 30 s y 2 min. Los
envíos creados antes de este cambio no tienen copia en Seguimiento: sus eventos terminan en la cola de fallidos.

### 2. Clasificación de los hechos

Cada hecho se clasifica en un único tipo (`ClasificadorAvisos`):

| Hecho | Tipo |
| --- | --- |
| Termina *EnDevolucion* (por ejemplo, el intento fallido que agota los intentos, T7 y T10) | `Devolucion` |
| Otro intento fallido (T7 y T9) | `EntregaFallida` |
| Todos los demás | `CambioDeEstado` |

`CambioDeEstado` equivale a "todos los hechos"; `EntregaFallida` y `Devolucion`, sólo al suyo
(`SuscripcionAviso.Recibe`). Así una suscripción recibe como máximo un aviso por hecho aunque haya elegido varios
tipos, y el campo `estado` del aviso lleva siempre el estado real del envío.

Eventos de Envíos que generan avisos y estado que informan:

| Evento | `estado` |
| --- | --- |
| `EnvioAdmitido` | `Admitido` |
| `EnvioAsignadoARuta` | `AsignadoARuta` |
| `EnvioDesasignadoDeRuta` | `EnDeposito` |
| `EnvioEnTransito` | `EnTransito` |
| `EnvioEntregado` | `Entregado` |
| `IntentoFallidoRegistrado` | El `EstadoFinal` del evento: `Reprogramado` o `EnDevolucion` |
| `EnvioReprogramado` | `Reprogramado` |
| `EnvioCancelado` | `Cancelado` |
| `EnvioReintegradoADeposito` | `EnDeposito` |

La recepción en depósito (T2), las devoluciones iniciadas por el operador o el comercio (T12, T14), la devolución
confirmada (T15) y el extravío (T16) todavía no publican eventos de integración. Cuando Envíos los publique, alcanza
con agregarlos a `PrepararAvisos.Tipos` y a su traducción.

### 3. Entrega en dos etapas

1. **Preparar** (`PrepararAvisos`, una cola por tipo de evento, `seguimiento.preparar-avisos.<tipo>`). En una sola
   transacción registra el mensaje en la inbox, crea una `EntregaAviso` *Pendiente* por cada suscripción que recibe el
   hecho y deja en el outbox de Seguimiento un `AvisoPendiente.v1` por entrega. No llama a ningún receptor. El índice
   único (`EventoId`, `SuscripcionAvisoId`) impide duplicar una entrega.
2. **Entregar** (`EntregarAviso`, cola `seguimiento.entregar-aviso`). Carga la entrega, descifra el secreto vigente de
   la suscripción, envía el aviso con `IEmisorAvisos` y registra el intento. Si el receptor no responde 2xx, el intento
   queda guardado y el consumidor lanza `AvisoNoEntregadoException`: la mensajería reintenta el mensaje con la espera
   creciente. El publicador del outbox, en la API, publica estos mensajes como los de cualquier módulo.

Así, si un receptor falla, sólo se reintenta su entrega: las demás suscripciones no reciben el aviso dos veces.

### 4. Reintentos y cola de fallidos

El sobre expone ahora el número de intento (`MensajeIntegracion.Intento`, leído de la cabecera `intento` que ya
escribía el consumidor). Cuando el mensaje ya pasó por las tres esperas, un nuevo fallo deja la entrega
*EnColaDeFallidos* y la excepción manda el mensaje a `logistica.fallidos`, como exige el ADR-0003 (sección 2.6).

Un mensaje reintentado desde CU-09, o un `AvisoPendiente` nuevo para la misma entrega encolado por CU-65, vuelve a
intentar una entrega *EnColaDeFallidos* (`EntregaAviso.Reintentar`). El receptor recibe siempre el mismo `id`.

La inbox se registra recién cuando la entrega termina bien, en la misma transacción. Si la entrega ya estaba
*Exitosa*, el mensaje se confirma sin volver a enviarla. Una suscripción desactivada después de preparar el aviso
ya no lo recibe: la entrega queda como estaba.

## Modelo

- `EntregaAviso` (raíz): suscripción, evento de origen (`EventoId`, el `MessageId` del evento de Envíos), envío,
  número y estado del envío, tipo, fecha del evento y estado de la entrega (*Pendiente*, *Exitosa*, *Fallida* o
  *EnColaDeFallidos*). Métodos `RegistrarIntento` y `Reintentar`.
- `IntentoAviso`: número, fecha, éxito, código HTTP y error de cada intento, para el historial de CU-65.
- `EnvioSeguido`: copia mínima del envío, creada con `EnvioAdmitido`.

Las tres entidades llevan `OperadorId` y `ComercioId` y tienen el filtro de inquilino. Tablas nuevas en el schema
`seguimiento`: `entregas_aviso`, `intentos_aviso` y `envios_seguidos` (migración `EntregasAviso`).

## Para CU-65

- Historial: `entregas_aviso` con su índice (`OperadorId`, `ComercioId`, `Estado`, `FechaEvento`) e `intentos_aviso`.
- Reenviar: en una unidad de trabajo, registrar `AvisoPendiente` en el outbox de Seguimiento con el `EntregaId`
  (`IIntegrationEventPublisher` del módulo). CU-65 llama a `Reintentar` en esa misma transacción para dejar la entrega
  en *Pendiente* desde la solicitud e impedir que dos solicitudes manuales la encolen simultáneamente. El consumidor
  admite ese estado y mantiene su recuperación de *EnColaDeFallidos* para CU-09.
- A2 (suscripción desactivada): verificar `SuscripcionAviso.Activa` antes de encolar.

El historial, los endpoints y las garantías del reenvío están documentados en [CU-65](../cu-65/README.md).

## Pendiente

- **Receptor de demostración** (escenario 6 de la defensa): la guía de CU-63 no admite destinos internos, ni siquiera
  en desarrollo, así que hace falta un receptor HTTPS público con escenarios de éxito, error y demora.
- Los eventos de las transiciones que todavía no se publican (sección 2).

## Verificación

- Unitarias (`EntregaAvisoTests`): clasificación, `Recibe`, creación, numeración de intentos, cola de fallidos,
  `Reintentar` y traducción de cada evento.
- Integración sobre PostgreSQL (`EntregarAvisosTests`): una entrega por suscripción que recibe el hecho, sin
  duplicados; alta pendiente que se reintenta; aislamiento por operador; fallo registrado y reintento con el mismo `id`
  y la firma verificada de forma independiente; último fallo a la cola de fallidos y recuperación; suscripción
  desactivada. Una prueba de extremo a extremo crea la suscripción y el envío desde el Portal y comprueba que el aviso
  llega firmado al receptor pasando por el outbox, RabbitMQ y el Worker.

## Historial de versiones

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 1.0 | 21/10/2026 | Versión inicial: diseño e implementación de CU-64. | Ezequiel Marcenal |
