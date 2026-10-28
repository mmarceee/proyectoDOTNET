# Guía para probar avisos, reintentos y cola de fallidos

Usamos un receptor de demostración que permite responder **200 (éxito)** o **503 (fallo)**.
Cloudflare conecta una dirección HTTPS pública con ese receptor en nuestra PC.

## 1. Levantar el sistema

La API, el Worker, PostgreSQL y RabbitMQ deben estar funcionando.

Si usan el entorno completo de Docker, desde la raíz del proyecto:

```powershell
docker compose up -d --build --wait
```

Si ejecutan la API y el Worker desde Visual Studio, deben iniciar ambos proyectos y mantener los servicios
de soporte encendidos. Eviten ejecutar simultáneamente otra instancia de esos proyectos en Docker.

## 2. Ejecutar el receptor

Desde la raíz del proyecto, en una terminal:

```powershell
dotnet run --project tools/Logistica.ReceptorDemo
```

Abrir el enlace **Panel** que imprime la terminal, completo con `#clave=...`.
Dejar la terminal abierta. La clave permite administrar el receptor; no compartir ese enlace.

## 3. Exponer el receptor con Cloudflare

Si falta instalar `cloudflared`:

```powershell
winget install --id Cloudflare.cloudflared --exact
```

Después de instalarlo, abrir una terminal nueva y ejecutar:

```powershell
cloudflared tunnel --url http://127.0.0.1:5099
```

Copiar la dirección `https://...trycloudflare.com` que aparece. Dejar también esta terminal abierta.
Para este túnel de prueba no hace falta una cuenta ni un dominio de Cloudflare.

Referencia: [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/).

## 4. Crear la suscripción

1. En el panel del receptor, pegar la dirección de Cloudflare en **Base HTTPS pública del túnel**.
2. Copiar la **URL completa** que genera el panel, terminada en `/avisos/...`.
3. En el Portal, ingresar como comercio y seleccionar operador.
4. Abrir **Integraciones → Avisos → Nueva suscripción**.
5. Pegar la URL completa, seleccionar **Cambio de estado** y crear la suscripción.

Cada ejecución genera su propia dirección: **no copiar la URL de otro compañero**.
Usar datos ficticios para la demostración.

## 5. Comprobar la conexión

1. En el receptor, pulsar **Responder 200 · Éxito** y esperar la confirmación del panel.
2. En el Portal, pulsar **Enviar prueba** en la suscripción nueva.
3. Debe aparecer un POST con **HTTP 200** en el receptor.

**Enviar prueba sólo comprueba la conexión: no pasa por el Worker ni tiene reintentos automáticos.**

## 6. Probar un fallo y recuperación automática

1. Cambiar el receptor a **503** y esperar que el panel confirme el modo.
2. **Crear un envío nuevo** desde el Portal, con el comercio y operador de la suscripción.
3. Comprobar que aparece un POST con **503** en el receptor.
4. Cambiar el receptor a **200** y esperar la confirmación.
5. Esperar el siguiente intento **sin pulsar Reenviar**.
6. Debe llegar otro POST con **el mismo campo `id`**, ahora con **200**.
7. En el **Historial** del Portal, actualizar la vista: la entrega debe quedar **Exitosa**, conservando los intentos fallidos.

El primer aviso llega enseguida. Después de fallar, las esperas entre intentos son **5 segundos, 30 segundos y 2 minutos**.
Si no alcanzan a cambiar el modo antes del primer reintento, pueden hacerlo durante la espera siguiente.

**Resultado esperado:** la entrega se recupera automáticamente al volver a estar disponible el receptor.

## 7. Probar la cola de fallidos y el reenvío manual

1. Cambiar el receptor a **503**, esperar la confirmación y crear otro envío nuevo.
2. Mantenerlo en 503 durante toda la prueba.
3. Deben llegar **4 POST**: el inicial y tres reintentos, aproximadamente a los **0, 5, 35 y 155 segundos**, más procesamiento.
4. En el Historial del Portal, actualizar la vista: debe quedar **En cola de fallidos**.
5. Abrir RabbitMQ Management en <http://localhost:15672>, salvo que hayan cambiado el puerto.
6. Ingresar con las credenciales de RabbitMQ configuradas en `.env`.
7. Abrir **Queues → `logistica.fallidos`** y comprobar el mensaje correspondiente. Es una cola compartida:
   correlacionar con la entrega de la prueba; su cantidad total puede incluir otros mensajes.
8. Cambiar el receptor a **200** y esperar la confirmación.
9. En el Historial del Portal, pulsar **Reenviar** sobre la entrega fallida.
10. Debe llegar otro POST con el mismo campo `id` y respuesta **200**.
11. Actualizar el Historial: la entrega debe quedar **Exitosa**, conservando los intentos anteriores.

**Resultado esperado:** tras agotar los reintentos automáticos, la entrega se recupera mediante un reenvío manual.
Ese reenvío no borra automáticamente la copia anterior de `logistica.fallidos`.

## Durante las pruebas

- Mantener encendidos la API, el Worker y los servicios de soporte.
- Mantener abiertas las terminales del receptor y Cloudflare.
- Mirar el panel del receptor conectado por Cloudflare. Una suscripción a webhook.site recibe avisos por separado.
- Las filas anteriores permanecen: cada reintento agrega otra fila con el mismo `id`.
- **Limpiar historial local** sólo borra la lista del receptor; no modifica el Historial del Portal ni RabbitMQ.
- El receptor conserva los últimos 100 POST en memoria. Al cerrarlo se pierden esos registros.
- Si reinician el receptor o el túnel, volver a obtener la URL completa y crear una nueva suscripción, desactivando la anterior.

Para seguir los errores del Worker si lo ejecutan con Docker:

```powershell
docker compose logs --follow --tail 50 worker
```
