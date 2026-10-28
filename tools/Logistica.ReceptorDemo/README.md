# Receptor de avisos para la demostración

Aplicación .NET independiente, sin paquetes adicionales. Permite responder **200** o **503** a los avisos del
Worker sin cambiar la URL. El panel muestra fecha, respuesta, cuerpo y cabeceras de firma de los últimos 100 POST.
No verifica el HMAC ni ejecuta operaciones de negocio. Todo queda en memoria; al reiniciar se borran los avisos,
se vuelve a 200 y se generan una nueva clave del panel y una nueva ruta receptora.

## Iniciar

Desde la raíz del repositorio:

```powershell
dotnet run --project tools/Logistica.ReceptorDemo
```

Abrir el enlace **Panel** que imprime la terminal, completo, incluido `#clave=...`.
Por defecto escucha en `http://127.0.0.1:5099`. Mantener esa terminal abierta.
La clave autoriza la consulta de los avisos y el cambio de respuesta; no compartir el enlace del panel.
El fragmento `#clave=...` se usa en el navegador y no se envía como parte de la URL al servidor.

## Conectarlo al Worker

El emisor exige HTTPS público y bloquea destinos internos: no admite `localhost`, `host.docker.internal` ni una IP
privada como URL de suscripción. Exponer **sólo este receptor** mediante un túnel HTTPS. No se necesita modificar
esa validación de la aplicación.

Una opción es [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/).
Instalar `cloudflared` siguiendo la documentación oficial y, en otra terminal del host, ejecutar:

```powershell
cloudflared tunnel --url http://127.0.0.1:5099
```

El comando imprime una base similar a `https://nombre-temporal.trycloudflare.com`.
Mantener también esa terminal abierta. No activar una pantalla de login delante del endpoint: el Worker necesita
poder enviar su POST directamente.

1. Pegar la base HTTPS en el campo del panel **Base HTTPS pública del túnel**.
2. Copiar la URL completa que aparece debajo: termina en `/avisos/<identificador-aleatorio>`.
3. En el Portal, **Integraciones → Avisos**, crear una suscripción con esa URL y `CambioDeEstado`.
4. Con el receptor en 200, **Enviar prueba** debe aparecer en el panel. Esto comprueba conectividad;
   para comprobar los reintentos, crear envíos nuevos, como se indica abajo.

La URL HTTPS se mantiene mientras dure ese túnel. Si se reinicia el receptor, cambia su ruta; si se reinicia el
túnel temporal, puede cambiar su base. En ambos casos crear una suscripción con la URL nueva y desactivar la anterior.
Usar datos ficticios de demostración. Los POST están limitados a 64 KiB.

## Caso 2: recuperarse automáticamente

1. Pulsar **Responder 503 · Fallo** y esperar a que el panel indique `503 · Fallo`.
2. Crear un envío nuevo en el Portal con el comercio y operador de la suscripción.
3. Ver el primer POST con HTTP 503 en el panel. Si pasan los primeros 5 segundos, el segundo también falla;
   después hay 30 segundos para cambiar la respuesta.
4. Pulsar **Responder 200 · Éxito**. No pulsar Reenviar en el Portal.
5. Esperar el siguiente POST: responde 200. Abrir los cuerpos para comprobar que el campo `id` es el mismo.
6. Actualizar el historial del Portal: la entrega debe estar exitosa y conservar los intentos fallidos.

## Caso 3: agotar reintentos y reenviar

1. Pulsar **Responder 503 · Fallo**, esperar la confirmación y crear otro envío.
2. Mantener 503 durante el intento inicial y los tres reintentos: aproximadamente a los 0, 5, 35 y 155 segundos,
   más procesamiento. El panel registra los cuatro POST con el mismo `id`.
3. Actualizar el historial del Portal: estado **En cola de fallidos**. En RabbitMQ comprobar el mensaje
   correspondiente en `logistica.fallidos`.
4. Pulsar **Responder 200 · Éxito** y esperar la confirmación del panel.
5. En el historial del Portal, solicitar **Reenviar**. El siguiente POST debe responder 200, conservando el `id`.
6. Actualizar el historial del Portal: la entrega debe estar exitosa, conservando los intentos anteriores.

**Limpiar historial local** sólo borra lo que muestra el receptor: no modifica RabbitMQ ni el historial del Portal
y no cambia el modo 200/503. El reenvío desde el Portal tampoco borra automáticamente la copia en la cola de fallidos.

## Verificación local del receptor

```powershell
powershell -ExecutionPolicy Bypass -File tools/Logistica.ReceptorDemo/probar.ps1
```

Compila, inicia una instancia temporal en un puerto libre, comprueba autenticación del panel, alternancia 200/503,
registro de reintentos, límite del historial y limpieza; luego detiene esa instancia. No usa el entorno de negocio,
Docker ni un túnel. Para el recorrido completo, seguir la [guía del Worker](../../docs/prueba-worker-independiente.md).
