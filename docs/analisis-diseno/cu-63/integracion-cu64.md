# Contrato de avisos · CU-63 y continuación de CU-64

Documento para Ezequiel, responsable de CU-64. CU-63 configura las suscripciones y envía una prueba
manual; la entrega automática por eventos, sus reintentos y su historial no se implementan aquí.

## Piezas disponibles en Seguimiento

| Pieza | Uso en CU-64 |
| --- | --- |
| `Domain/Avisos/SuscripcionAviso` | Dirección, tipos de evento, activación, pertenencia a operador/comercio y vínculo comercial |
| `Application/Abstractions/ISecretosAviso` | Descifrar el secreto guardado usando los identificadores del inquilino |
| `Application/Abstractions/IEmisorAvisos` | Un intento HTTP firmado; retorna éxito, código HTTP o error |
| `MensajeAviso`, en `Application/Abstractions/IEmisorAvisos.cs` | Mensaje compartido entre la prueba y la entrega real |
| `Infrastructure/Security/FirmadorAvisos` | Serialización y firma exactas de los bytes enviados |
| `Infrastructure/Http/EmisorAvisosHttp` | POST HTTPS, 10 segundos, sin reintentos de aplicación ni lectura del cuerpo de respuesta |
| `Infrastructure/Http/ConectorAvisosHttp` | Bloqueo de destinos internos, DNS validado y conexión a la IP validada, sin redirecciones, proxy ni cookies |
| `SeguimientoModule.AddSeguimientoModule` | Registra el emisor, secretos y reloj también en el Worker |

Son tipos internos del mismo módulo: el código de CU-64 debe permanecer en Seguimiento, ejecutado por
el Worker, tal como dispone la arquitectura. No hace falta exponer dominio ni entidades a otros módulos.
`ISuscripcionesAvisoRepository` tiene operaciones de configuración; CU-64 deberá agregar la consulta de
suscripciones activas por comercio y evento dentro del contexto de operador resuelto desde el mensaje.

## Mensaje v1

POST a la URL configurada. `Content-Type: application/json; charset=utf-8`.

```json
{"id":"11111111-1111-1111-1111-111111111111","tipo":"CambioDeEstado","fechaEvento":"2026-10-20T15:00:00+00:00","numeroEnvio":"PRUEBA","estado":"Admitido","prueba":true}
```

| Campo | Tipo y significado |
| --- | --- |
| `id` | UUID del mensaje; la prueba crea uno nuevo en cada solicitud manual |
| `tipo` | `CambioDeEstado`, `EntregaFallida` o `Devolucion` |
| `fechaEvento` | Fecha del hecho, ISO 8601 con zona; la prueba usa la hora UTC de su creación |
| `numeroEnvio` | Cadena; en producción será el número real del envío |
| `estado` | Nombre del estado del dominio de Envíos, como cadena |
| `prueba` | Booleano: `true` en CU-63 y `false` para eventos reales de CU-64 |

La prueba no crea ni modifica un envío. Usa `numeroEnvio = "PRUEBA"`, el primer tipo seleccionado
según el orden del enum y un estado ficticio coherente: `Admitido`, `NoEntregado` o `EnDevolucion`.
El receptor debe verificar la firma y reconocer `prueba: true` antes de ejecutar operaciones de negocio.

Para CU-64 se debe mantener el mismo identificador al reintentar una entrega. El receptor podrá descartar
duplicados por ese identificador; el emisor no garantiza el orden. Hay que definir la clasificación de los
eventos reales, incluyendo la superposición entre cambio de estado, entrega fallida y devolución, de modo
que una misma suscripción no reciba dos mensajes por el mismo hecho sólo por coincidir en varios tipos.
Esta clasificación pertenece a CU-64 y todavía no está implementada.

## Firma HMAC-SHA256

Cabeceras:

```text
X-Aviso-Fecha: 1792508400
X-Aviso-Firma: v1=19be04bc86322406b0a4a85cbc0d9f49e4b412704a1d8b6c59af55fdd31f441d
```

1. Decodificar el secreto de firma de Base64 estándar: son exactamente 32 bytes.
2. Tomar el valor decimal de `X-Aviso-Fecha` (segundos Unix, sin espacios).
3. Concatenar sus bytes UTF-8, el byte de punto `0x2e` y el cuerpo HTTP exacto.
4. Calcular HMAC-SHA256 con el secreto decodificado y esa secuencia.
5. Enviar `v1=` seguido del hexadecimal minúsculo de los 32 bytes de firma.

La fecha de la cabecera representa el intento de envío, no la fecha del evento. Al reintentar, CU-64
debe conservar `id` y `fechaEvento` y firmar con la fecha del nuevo intento. El secreto nunca viaja en
cabeceras ni cuerpo. Una regeneración invalida el anterior inmediatamente: obtener el secreto actual
de la suscripción al preparar cada intento, en lugar de conservarlo en mensajes de cola.

El receptor debe comparar en tiempo constante y rechazar fechas fuera de una tolerancia de 300 segundos
respecto de su reloj, tanto antiguas como futuras. Es un criterio de integración acordado para el receptor
de prueba; requiere relojes sincronizados. No reemplaza la deduplicación por `id`.

### Vector reproducible, sin datos reales

Secreto de ejemplo (sólo para pruebas; bytes de 0 a 31):

```text
AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=
```

El cuerpo es exactamente la línea JSON del ejemplo anterior, sin salto de línea final. Con fecha
`1792508400` debe producir la firma mostrada arriba. Este vector está cubierto por `EmisorAvisosTests`
y fue contrastado con `hmac` de Python, independientemente del firmador de C#.

Ejemplo de verificación para el receptor, usando únicamente la biblioteca estándar de Python:

```python
import base64, hashlib, hmac, time

def verificar(cuerpo: bytes, fecha: str, firma: str, secreto_base64: str) -> bool:
    try:
        clave = base64.b64decode(secreto_base64, validate=True)
        if len(clave) != 32 or abs(int(time.time()) - int(fecha)) > 300:
            return False
        esperada = "v1=" + hmac.new(
            clave, fecha.encode("utf-8") + b"." + cuerpo, hashlib.sha256
        ).hexdigest()
        return hmac.compare_digest(esperada, firma)
    except (ValueError, TypeError):
        return False
```

No deserializar y volver a serializar para verificar: espacios, escapes y representación de fechas pueden
cambiar la firma. La fecha del vector es fija; para verificarlo fuera de esa fecha se omite únicamente el
control de antigüedad en la prueba del vector, no en el receptor operativo.

## Configuración y límites

`Seguimiento:Avisos:ClaveCifrado` es la clave maestra para descifrar los secretos; no es el secreto del
receptor. API y Worker deben usar el mismo valor de exactamente 32 bytes en Base64. Docker Compose
lo toma de `AVISOS_CLAVE_CIFRADO`; desarrollo local usa los user-secrets compartidos. Conservar la clave
maestra entre despliegues y no guardarla en el repositorio ni en los mensajes.

La URL debe usar HTTPS sin credenciales incrustadas ni fragmentos. Se mantiene la validación TLS normal.
No hay excepciones para certificados inválidos ni destinos internos en producción o desarrollo.
Se resuelven A y AAAA y se rechaza la resolución completa si incluye una dirección no pública.
La conexión se fija a una IP ya validada, conservando el host original para TLS. No se siguen respuestas
3xx, no se usan proxies del entorno y no se conservan cookies del receptor.
Los controles siguen la orientación de [OWASP sobre SSRF](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html).

## Resultado y responsabilidades

- Cualquier 2xx: éxito. Otros códigos, incluidos 3xx: fallo con código disponible.
- Sin conexión, error TLS o destino bloqueado: fallo sin código, con un mensaje acotado para el usuario.
- Sin cabeceras de respuesta antes de 10 segundos: fallo por tiempo de espera.
- Cancelación de la petición del cliente: se propaga; no se convierte en un timeout del receptor.
- No se lee ni se muestra el cuerpo de respuesta del receptor.

El endpoint manual es `POST /api/avisos/suscripciones/{id}/prueba`, sin body, con sesión y token
antifalsificación. Devuelve 200 con `{mensajeId, exitosa, codigoRespuesta, error}` para un intento
realizado, incluso si falló. 404 significa inexistente/ajena; 400, suscripción inactiva o token inválido.
La prueba no altera la suscripción, no registra `EntregaAviso`, no publica en RabbitMQ y no entra al
historial ni a una cola de reintentos. Si el navegador pierde la respuesta, no se reenvía automáticamente.

Ezequiel deberá implementar en CU-64 la reacción a eventos, la selección de suscripciones activas, el
registro de entregas/intentos, los reintentos, la cola de fallidos y su integración con CU-65. Esas piezas
podrán consumir `IEmisorAvisos` sin duplicar el contrato de firma.

## Verificación disponible y receptor de demostración

`EnviarPruebaAvisoApiTests` usa un receptor HTTP en memoria que verifica el HMAC de forma independiente,
comprueba fecha y datos de prueba y simula 202, 204, 302, 500, falta de conexión y demora. Las pruebas
de API también verifican aislamiento, permisos, antifalsificación, rechazo de inactivas y uso del nuevo
secreto después de regenerar. Las unitarias verifican el vector, la integridad y el transporte seguro,
incluida una petición real bloqueada a loopback.

Este receptor automatizado no es un servicio desplegado ni tiene una URL para copiar en el Portal.
Para la demostración conjunta exigida en la sección 6.9, el equipo deberá disponer del receptor HTTPS
con escenarios de éxito, error y demora; su URL pública será el destino configurado. El receptor deberá
usar el secreto obtenido al crear la suscripción y reconocer las pruebas sin modificar pedidos reales.
El despliegue de ese receptor y la entrega automática de CU-64 quedan fuera de este cambio de CU-63.
