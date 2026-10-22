# CU-62 · Notificar al destinatario

| Campo | Contenido |
| :---- | :---- |
| **Estado** | Implementado, pendiente de revisión del equipo |
| **Fecha** | 22/10/2026 |
| **Responsable** | Ezequiel Marcenal |
| **Equipo** | Equipo 1: Ezequiel Marcenal, Lucas Ottonello y Cristian Reyes |

Referencias: [caso de uso](../casos-de-uso.md), [CU-64](../cu-64/README.md), [ADR-0002](../ADR-0002-estrategia-de-multitenancy.md)
y [ADR-0003](../ADR-0003-outbox-y-mensajeria.md).

## Resumen

Ante cada cambio relevante de un envío, el Worker manda un correo al destinatario con la marca del operador y el enlace
de seguimiento (RF 27). Cada correo queda registrado como `Notificacion`, una por evento. Los fallos del servidor de
correo se reintentan con la espera creciente de la mensajería y, agotados los reintentos, la notificación queda
*Fallida* y su mensaje pasa a `logistica.fallidos` (CU-09). Todo el código está en Seguimiento, que ejecuta el Worker.

## Decisiones

### 1. Los datos del destinatario

El Worker no consulta Envíos. `EnvioAdmitido.v1` suma dos campos opcionales, `NombreDestinatario` y
`EmailDestinatario`; agregar un campo opcional no cambia la versión (ADR-0003, sección 2.3). Seguimiento los guarda en
`EnvioSeguido`, la copia del envío que ya creaba CU-64, y los hechos posteriores la leen de ahí.

### 2. La marca del operador

El Worker tampoco consulta Administración. CU-07 publica ahora **`IdentidadVisualActualizada.v1`** (nombre, colores y
contacto, sin el logo, igual que el seguimiento público) cada vez que el administrador guarda la identidad, en la misma
transacción que el cambio. Para eso Administración tiene su `IIntegrationEventPublisher` (ADR-0003, sección 2.3).
Seguimiento guarda una copia, `IdentidadOperador`; un evento anterior que llega tarde no pisa uno más nuevo.

Un operador que nunca guardó su identidad, como los del seed, usa una **marca neutra** («Seguimiento de envíos») hasta
que la guarde: el enlace lleva al seguimiento público, que sí muestra la suya.

### 3. Cambios que se notifican

La cola es la selección de lo relevante (paso 3): el consumidor se suscribe sólo a estos eventos y el resto no llega (A1).

| Evento | Correo |
| --- | --- |
| `EnvioAdmitido` | «Recibimos tu envío», con el enlace de seguimiento |
| `EnvioEnTransito` | «Tu envío está en camino» |
| `IntentoFallidoRegistrado` | «No pudimos entregar tu envío»: se intenta de nuevo o vuelve al remitente, según el estado final |
| `EnvioReprogramado` | «Nueva fecha de entrega», con la fecha |
| `EnvioEntregado` | «Entregamos tu envío» |

El enlace va en todos los correos. No se notifican la asignación o el retiro de una ruta, la cancelación ni el
reintegro al depósito.

### 4. Envío, idempotencia y reintentos

Una cola por evento, `seguimiento.notificar-destinatario.<tipo>` (el nombre que usa de ejemplo el ADR-0003). En una
transacción, el consumidor registra la inbox, crea la `Notificacion` (índice único por `EventoId`) y la envía:

- **Sin correo, o con un correo inválido:** queda *NoEnviable*, sin reintentos (A2).
- **Enviada:** se registra la inbox y el mensaje se confirma. El mismo evento procesado otra vez no manda otro correo.
- **Falla el servidor:** el intento queda guardado, la notificación sigue *Pendiente* y la excepción hace que la
  mensajería reintente a los 5 s, 30 s y 2 min. En el último intento queda *Fallida* y el mensaje va a la cola de
  fallidos (A3). Si CU-09 lo vuelve a publicar, la notificación se envía.

Como toda entrega "al menos una vez", queda un caso límite: si el servidor aceptó el correo y después falla el guardado
en la base, el reintento lo manda de nuevo. El correo lleva el `Message-ID` de la notificación, así un cliente de correo
puede reconocer el duplicado.

El hecho del envío, su copia y la traducción de los eventos (`HechoEnvio`, `EnviosSeguidos` y `LectorHechosEnvio`) se
comparten con CU-64. Como allí, un evento que llega antes que el alta de su envío se reintenta.

### 5. Enlace y correo

- **Enlace:** `Aplicacion:UrlPublica` más `/seguimiento/{token}`, con el token firmado de CU-60. El Worker usa la misma
  `Seguimiento:ClaveToken` que la API.
- **SMTP:** MailKit 4.18.1, el cliente que recomienda Microsoft. `ConnectionStrings:Smtp` acepta `smtp://` (STARTTLS si
  el servidor lo ofrece: Mailpit, o Brevo en el 587) y `smtps://`, con usuario y contraseña opcionales en la dirección.
  MimeKit trae `System.Security.Cryptography.Pkcs` 10.0.0, con avisos de seguridad: se fija la 10.0.13.
- **Remitente:** `Seguimiento:Correo:Remitente` (por defecto, `avisos@logistica.local`). El nombre visible es el del
  operador, y la respuesta va a su correo de contacto si lo tiene.
- **Contenido:** texto plano y HTML con estilos en línea y el color del operador. Todo dato variable se codifica y un
  color que no sea `#RRGGBB` se reemplaza por el neutro.

Las tres configuraciones se validan al usarlas, así la API y el Worker arrancan aunque falten, y el error dice qué
configurar.

## Configuración

| Clave | Docker Compose (`.env`) | Por defecto |
| --- | --- | --- |
| `Aplicacion:UrlPublica` | `URL_PUBLICA` | `http://localhost:${API_PORT}` |
| `Seguimiento:Correo:Remitente` | `CORREO_REMITENTE` | `avisos@logistica.local` |
| `ConnectionStrings:Smtp` | — | `smtp://mailpit:1025` |
| `Seguimiento:ClaveToken` | `SEGUIMIENTO_CLAVE_TOKEN` | — (obligatoria) |

`scripts/configurar-secretos.sh` carga las cuatro en los user-secrets; hasta ahora no cargaba `Seguimiento:ClaveToken`,
que el Worker necesita para firmar los enlaces. En desarrollo los correos se ven en Mailpit: http://localhost:8025.

## Pendiente

- **Producción:** la dirección pública de DigitalOcean, la cadena SMTP de Brevo y un remitente verificado. `infra/terraform`
  todavía no tiene archivos.
- **Envíos anteriores a CU-64:** no tienen `EnvioSeguido`, así que sus eventos terminan en la cola de fallidos.
- **Operadores sin identidad guardada:** usan la marca neutra hasta que el administrador guarde la de CU-07.
- **CU-02:** la invitación (`UsuarioInvitado`) todavía no se envía; puede reutilizar `IEmisorCorreo` y
  `Aplicacion:UrlPublica`.

## Verificación

- Unitarias (`NotificacionTests`): estados de la notificación y correo inválido, reintentos y recuperación, identidad
  más nueva, textos de cada hecho, lectura del destinatario y la fecha, codificación de la plantilla y validación de la
  configuración.
- Integración sobre PostgreSQL (`NotificarDestinatarioTests`): un correo por hecho y sin duplicados, con enlace absoluto
  cuyo token abre el envío; *NoEnviable* sin correo; fallo, último fallo y recuperación; alta pendiente; marca del
  operador. Una prueba de extremo a extremo crea el envío desde el Portal y comprueba en **Mailpit** que el correo llega
  pasando por el outbox, RabbitMQ y el Worker.

## Historial de versiones

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 1.0 | 22/10/2026 | Versión inicial: diseño e implementación de CU-62. | Ezequiel Marcenal |
