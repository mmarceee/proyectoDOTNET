# CU-65: guía de prueba

## Verificación del 26/10/2026

- Se ejecutaron `HistorialAvisosTests` y `EntregarAvisosTests`: 23 pruebas correctas, sin errores ni omisiones.
- Se agregó una prueba que prepara una entrega en cola de fallidos, solicita su reenvío por HTTP con sesión
  y antifalsificación y verifica la recuperación por outbox, RabbitMQ y Worker. El historial termina en Exitosa,
  con el mismo identificador, el intento 503 anterior y el nuevo intento 200. El receptor automatizado valida la firma.
- La prueba prepara el fallo agotado; no espera los tiempos de reintento reales ni usa un receptor HTTPS público.
- Se reconstruyeron la API y el Worker de Docker con el código actual. Antes, la imagen en ejecución devolvía
  Not Found al acceder al historial. No se borraron los volúmenes de datos.
- En navegador se verificaron listado, detalle, filtro de estado y limpieza de filtros. El aviso de ENV-000058
  figuraba Exitosa con un intento HTTP 200 y sin posibilidad de reenvío. No había entregas en cola de fallidos.
- No se ejecutó un reenvío manual en el navegador ni se cambió la respuesta del receptor externo.

## 1. Consultar el historial actual

1. Abrir <http://localhost:8080/portal/integraciones/avisos/historial>.
2. Si solicita ingreso, usar la cuenta local de demostración `comercio@tiendademo.uy`, contraseña `Demo-2026!`.
3. Elegir **Operador Demo**. Si el ingreso lleva al inicio, volver a abrir la dirección del paso 1.
4. Sin filtros, buscar ENV-000058. Es un dato observado durante esta revisión; puede cambiar si se modifica la base.
5. Abrir **Ver detalle**: comprobar estado del envío, estado del aviso, identificador, receptor e intentos.
   Para el aviso observado: envío Admitido, aviso Exitosa, un intento Exitoso con respuesta HTTP 200.
6. Volver al historial y seleccionar **En cola de fallidos**. Al momento de la revisión, la lista queda vacía.
7. Pulsar **Limpiar filtros**. El aviso exitoso vuelve a aparecer.
8. Probar fecha desde/hasta usando el día del evento mostrado en la lista, no el día de su último intento.

Los filtros se aplican al cambiar su valor. Para consultar cambios del procesamiento, recargar la página (F5).
Un aviso exitoso no debe poder seleccionarse para reenvío ni mostrar el botón Reenviar.

## 2. Probar una entrega nueva

1. En **Suscripciones**, comprobar que haya una suscripción activa para **Cambio de estado** con un receptor
   de prueba disponible. El receptor debe ser HTTPS público; localhost y las direcciones privadas se rechazan.
2. Crear un envío de demostración para ese comercio y operador, desde el alta habitual del Portal.
3. Volver al historial y recargar. El Worker debería registrar la entrega del evento Admitido.
4. Abrir el detalle y comprobar un intento exitoso con respuesta 2xx. Si todavía está pendiente, recargar después
   de que el Worker procese el mensaje.

**Enviar prueba**, en Suscripciones, comprueba CU-63 y no genera una entrada en el historial de CU-65.

## 3. Probar fallo y reenvío manual

Se necesita un receptor de prueba controlado, capaz de devolver 503 y luego 200 **en la misma URL**.
La existencia de un capturador Webhook.site que responde 200 no acredita por sí sola ese escenario.
La preparación de ese receptor público no quedó ejecutada en esta revisión.

1. Crear una suscripción de prueba activa para Cambio de estado hacia ese receptor.
2. Configurar el receptor para responder 503 y crear un envío nuevo del comercio/operador seleccionado.
3. Recargar el historial: el aviso pasa por Fallida mientras se reintenta automáticamente. No debe permitir reenvío.
4. Mantener la respuesta 503 hasta que llegue a **En cola de fallidos**. Hay un intento inicial y tres reintentos
   con esperas de 5, 30 y 120 segundos: aproximadamente 2 minutos y 35 segundos más el procesamiento.
5. Abrir el detalle, anotar el identificador y comprobar que conserva los intentos 503.
6. Cambiar el mismo receptor a 200. Mantener activa la suscripción.
7. Pulsar **Reenviar**, comprobar la confirmación y confirmar. La solicitud se encola; esto todavía no significa
   que el receptor la haya aceptado.
8. Recargar después del procesamiento. Debe terminar **Exitosa**, conservar el identificador y los intentos
   anteriores y agregar un intento 200 con la numeración siguiente. El receptor debe recibir el mismo identificador.
9. Para el lote, repetir con dos envíos fallidos, seleccionarlos en la lista y usar **Reenviar seleccionados**.
   Ambos deben recuperarse conservando sus respectivos identificadores e intentos.

Si sigue fallando, abrir el último intento: 503 significa que el receptor continúa rechazando; sin código HTTP,
el error puede indicar conexión, resolución o tiempo de espera. Si permanece Pendiente, revisar API/outbox,
RabbitMQ y Worker. No desactivar una suscripción que se quiera reutilizar: la pantalla no ofrece reactivación.

## Repetir la verificación automatizada

Con Docker Desktop funcionando, desde la raíz del repositorio:

```powershell
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~HistorialAvisosTests|FullyQualifiedName~EntregarAvisosTests" --logger "console;verbosity=minimal"
```

Las pruebas usan contenedores temporales de PostgreSQL y RabbitMQ y un receptor automatizado en memoria.
Sus datos no aparecen en la base local del Portal.
