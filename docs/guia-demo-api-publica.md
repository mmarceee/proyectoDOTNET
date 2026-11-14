# Demostración del opcional 7.4: API pública para comercios

Guion preparado a partir de la implementación del repositorio. Los resultados indicados son los esperados; esta guía no registra una ejecución de la demostración.

La defensa en vivo dura unos 5 a 7 minutos. Los pasos 1 a 5 se hacen desde el navegador y el paso 6 muestra las dos cuotas con un lanzador local. El anexo conserva las demás comprobaciones para el ensayo o para responder preguntas.

## 1. Preparar el entorno antes de presentar

Desde la raíz del proyecto, con Docker Desktop iniciado y `.env` configurado según el README:

```powershell
docker compose up -d --build --wait
```

Abrir estas pestañas:

- Portal: http://localhost:8080/portal/
- Claves: http://localhost:8080/portal/integraciones/claves-api
- Documentación: http://localhost:8080/api/docs
- OpenAPI: http://localhost:8080/api/openapi/v1.json

En el entorno de desarrollo con los usuarios iniciales y sin contraseña personalizada, ingresar al Portal con `comercio@tiendademo.uy` y `Demo-2026!`. Seleccionar **Operador Demo**. Si se configuró `Demo:Contrasena`, usar ese valor.

**Presentación:** «Esta API permite que el sistema de un comercio cree y consulte envíos sin usar manualmente el Portal. Voy a mostrar las cinco condiciones del opcional: versionado y documentación, claves, pruebas, límites diferenciados y documentación navegable».

## 2. Mostrar la documentación y el versionado

1. Abrir `/api/docs` en una ventana sin sesión para mostrar el acceso anónimo.
2. Mostrar la versión `v1` y las operaciones `POST /api/v1/envios`, `GET /api/v1/envios` y `GET /api/v1/envios/{numero}`.
3. Desplegar un esquema de solicitud, los códigos de respuesta y los modelos.
4. Abrir **Descargar OpenAPI**: `/api/openapi/v1.json`.
5. En **Autenticación y límites**, señalar la cabecera `X-Api-Key` y las cuotas publicadas. Con los valores predeterminados son 30 solicitudes por minuto en prueba y 120 en producción. Explicar que, cuando se supera la cuota, la API devuelve 429 con `Retry-After`.

**Explicación:** «El contrato está versionado en la URL y la documentación se genera desde los endpoints. La consola permite ejecutar ejemplos con claves de prueba».

## 3. Generar las claves desde el Portal — CU-80

En **Claves de API**, con Operador Demo seleccionado:

1. Nombre: `Demo opcional - prueba`; ambiente: **Prueba**; pulsar **Generar clave**.
2. Copiar el secreto antes de pulsar **Ya la guardé**. Se muestra una sola vez.
3. Mostrar el selector **Ambiente**, con las opciones **Prueba** y **Producción**. Explicar que la clave de prueba permite ensayar desde la consola y que los envíos de prueba quedan fuera de la operación real.
4. Mostrar la tabla con el nombre, ambiente, prefijo, creación, último uso y estado. Señalar **Revocar** y explicar que la clave deja de servir cuando se confirma la revocación.

**Explicación:** «La clave identifica al comercio y al operador seleccionado. El servidor obtiene de ella el ambiente; el cliente no elige otro comercio enviando un identificador en el JSON».

## 4. Crear un envío de prueba y repetir el alta — CU-81

1. En `/api/docs`, pegar la clave de prueba en **Clave de API de prueba**.
2. Ir a **Crear un envío**. Conservar el JSON de ejemplo, cambiando `referenciaComercio` por una referencia nueva, por ejemplo `DEMO74-20261109-01`.
3. Conservar el código postal `11300`, modalidad `Estandar` y el bulto de 2 kg del ejemplo: corresponden a la cobertura y tarifa iniciales de Operador Demo.
4. Pulsar **Ejecutar prueba**.
5. Mostrar `HTTP 201` y los campos `numero`, `referenciaComercio`, `tarifa`, `estado` y `enlaceSeguimiento`. Guardar el número.
6. Sin modificar el JSON, volver a pulsar **Ejecutar prueba**.
7. Mostrar `HTTP 200` y el mismo número de envío.

**Explicación:** «La referencia hace idempotente el alta dentro de esta relación y ambiente. Si el comercio reintenta porque perdió la respuesta, obtiene el envío existente».

La consola envía las solicitudes a `/api/v1/pruebas/envios`. Es el mismo caso de uso, con una protección adicional del servidor que exige una clave de prueba. Las rutas canónicas `/api/v1/envios` también seleccionan el ambiente a partir de la clave.

Si la primera llamada responde 200, esa referencia ya existía: usar una nueva para mostrar el 201.

## 5. Consultar listado, detalle e historial — CU-82

En la misma documentación:

1. En **Listar envíos**, completar `referenciaComercio` con la referencia utilizada, `pagina = 1` y `tamanoPagina = 5`.
2. Ejecutar y mostrar `HTTP 200`, `total = 1` y el envío creado. La repetición del alta no generó otro registro.
3. En **Consultar un envío y su historial**, ingresar su número público y ejecutar.
4. Mostrar `envio`, `bultos` e `historial`: resumen, total de bultos, entregados, resultados por estado, código de cada bulto y eventos con fecha.
5. Opcionalmente ejecutar la operación de seguimiento de prueba con ese número. Requiere la clave; no es un enlace público de producción.

**Explicación:** «El comercio puede consultar tanto un listado paginado como el detalle y el historial de un envío. Un envío recién creado todavía no tiene entregas».

Dejar el detalle abierto y el número a la vista para compararlo con las respuestas del alta y el reintento.

## 6. Mostrar la tasa diferenciada

Volver a **Autenticación y límites** en `/api/docs` y señalar las cuotas predeterminadas de **30/min en prueba** y **120/min en producción**. Cuando hayan pasado 61 segundos sin solicitudes públicas de esa relación, abrir `TestResults/demo-api-publica/Mostrar-cuotas.cmd`. El lanzador usa las claves locales guardadas y pide claves activas si faltan o fueron revocadas. Hace 31 consultas de solo lectura por ambiente y muestra el resultado en dos filas.

En prueba deben aparecer 30 respuestas 200 y una 429 con `Retry-After`. Producción debe aceptar las 31 consultas. Si la prueba venía con solicitudes anteriores, el 429 puede aparecer antes. Los valores de la tabla muestran el resultado de esa tanda. La cuota de 120/min se muestra en la documentación.

**Explicación:** «Hice la misma consulta 31 veces en cada ambiente. Prueba llegó al límite y devolvió 429 con los segundos de espera. Producción aceptó la tanda completa».

## Anexo de ensayo y consultas opcionales

Los comandos funcionan en Windows PowerShell 5.1 y PowerShell 7. Para los pasos que requieren producción, usar una clave de ese ambiente del mismo comercio y operador.

Ejecutar este bloque en una misma terminal. Cuando aparezca cada mensaje de `Read-Host`, pegar la clave correspondiente y pulsar Enter. Dejar el texto entre comillas tal como está. Las asignaciones preparan variables y no imprimen resultados.

```powershell
$base = 'http://localhost:8080'
$clavePrueba = Read-Host 'Pegar clave de prueba' -AsSecureString
$claveProduccion = Read-Host 'Pegar clave de produccion' -AsSecureString
$hPrueba = @{ 'X-Api-Key' = [pscredential]::new('demo', $clavePrueba).GetNetworkCredential().Password }
$hProduccion = @{ 'X-Api-Key' = [pscredential]::new('demo', $claveProduccion).GetNetworkCredential().Password }

function Invoke-DemoWebRequest {
    param([string]$Uri, [hashtable]$Headers = @{}, [string]$Method = 'Get',
        [string]$ContentType, [string]$Body)

    try {
        Invoke-WebRequest @PSBoundParameters -UseBasicParsing -ErrorAction Stop
    } catch {
        $respuesta = $_.Exception.Response
        if ($null -eq $respuesta) { throw }
        [pscustomobject]@{
            StatusCode = [int]$respuesta.StatusCode
            Headers = $respuesta.Headers
            Content = $_.ErrorDetails.Message
        }
    }
}
```

### Consultar una entrega parcial

Para mostrar progreso parcial, preparar previamente un envío de producción de tres bultos mediante el flujo operativo del proyecto. La consola web ejecuta las consultas en `/api/v1/pruebas`. Un envío real devuelve 404 con clave de prueba. Consultarlo desde la terminal con la clave de **producción** del comercio y operador del envío y su número público `ENV-…`.

Con `$base` y `$hProduccion` cargados en el bloque anterior, ejecutar:

```powershell
$numeroReal = Read-Host 'Numero publico del envio real (ENV-...)'
$detalle = Invoke-RestMethod -Uri "$base/api/v1/envios/$numeroReal" -Headers $hProduccion -UseBasicParsing
$detalle.envio | Format-List numero, estado, entregados, totalBultos, terminado
$detalle.envio.resultados | Format-List
$detalle.bultos | Format-Table codigo, estado
$detalle.historial | Format-Table tipo, ocurridoEn
```

Con dos bultos entregados y uno pendiente, esperar `estado = EntregadoParcialmente`, `entregados = 2`, `totalBultos = 3` y `terminado = false`. Si el tercero se devuelve al comercio, volver a consultar y comprobar `terminado = true` y un devuelto.

### Comprobar separación entre prueba y producción

En la terminal, ingresar los valores usados en la consola:

```powershell
$referencia = Read-Host 'Referencia del envio de prueba'
$numeroPrueba = Read-Host 'Numero del envio de prueba'
$refEscapada = [uri]::EscapeDataString($referencia)

# Prueba encuentra el envio; produccion no lo encuentra.
Invoke-RestMethod "$base/api/v1/envios?referenciaComercio=$refEscapada" -Headers $hPrueba |
    ConvertTo-Json -Depth 10
Invoke-RestMethod "$base/api/v1/envios?referenciaComercio=$refEscapada" -Headers $hProduccion |
    ConvertTo-Json -Depth 10

# El detalle de prueba no se puede leer con la clave de produccion: 404.
(Invoke-DemoWebRequest "$base/api/v1/envios/$numeroPrueba" -Headers $hProduccion).StatusCode

# El destino de la consola rechaza produccion por el servidor: 403.
(Invoke-DemoWebRequest "$base/api/v1/pruebas/envios" -Headers $hProduccion).StatusCode
```

Con una referencia nueva exclusiva de esta demo, esperar total 1 en prueba y total 0 en producción. Buscar también esa referencia en el listado operativo del Portal: no debe aparecer.

Para comprobar que la referencia se puede reutilizar en ambos ambientes, crear un envío de producción con la misma referencia. Usar la variable `$referencia` cargada al principio de este paso con la referencia del envío de prueba.

```powershell
$cuerpo = @{
    referenciaComercio = $referencia
    destinatario = @{ nombre = 'Ana Perez'; telefono = '099123456'; email = $null; documento = $null }
    direccion = @{
        calle = 'Av. Italia'; numero = '1234'; localidad = 'Montevideo'
        departamento = 'Montevideo'; codigoPostal = '11300'; referencia = $null
    }
    bultos = @(@{ pesoKg = 2; largoCm = 30; anchoCm = 20; altoCm = 10 })
    modalidad = 'Estandar'
} | ConvertTo-Json -Depth 5

$altaProduccion = Invoke-DemoWebRequest "$base/api/v1/envios" -Method Post -Headers $hProduccion `
    -ContentType 'application/json; charset=utf-8' -Body $cuerpo
$altaProduccion.StatusCode
$altaProduccion.Content
```

Esperar 201 y un número distinto al de prueba. Ahora el listado de producción encuentra su propio envío y el Portal operativo puede mostrarlo. Esta alta crea un envío operativo en el entorno local.

**Explicación:** «Las pruebas usan el mismo despliegue, pero no entran en la operación real. La misma referencia puede existir en los dos ambientes sin colisión».

Para mostrar además aislamiento por operador, generar una clave de prueba seleccionando otro operador con relación activa, por ejemplo Operador Este si conserva los datos iniciales. Consultar el número de prueba de Operador Demo con esa clave: esperar 404. Para aislamiento entre comercios, preparar una segunda cuenta de comercio con relación activa y repetir la consulta del número ajeno.

### Comprobar autenticación y errores

```powershell
# Una peticion sin clave recibe 401, aunque haya una sesion abierta en el navegador.
(Invoke-DemoWebRequest "$base/api/v1/envios").StatusCode

# Paginacion invalida: 400.
(Invoke-DemoWebRequest "$base/api/v1/envios?tamanoPagina=0" -Headers $hPrueba).StatusCode
```

Opcionalmente, en el alta de la consola:

- Usar `referenciaComercio = ""`: esperar 400.
- Usar una referencia nueva y un código postal válido en formato pero fuera de cobertura: esperar 422. Verificarlo en el ensayo con la configuración vigente.

Usar siempre una referencia nueva para probar cobertura o tarifa: con una referencia existente se devuelve el envío anterior antes de cotizar.

### Comprobar limitación de tasa manualmente

La configuración inicial es 30 solicitudes/minuto en prueba y 120 en producción, compartidas por relación comercial y ambiente. Además, hay 600/minuto por IP antes de autenticar. Si se personalizaron, usar los valores publicados en `/api/docs`.

Las altas y consultas anteriores consumieron cuota. Para mostrar una comparación limpia, esperar al menos 61 segundos sin solicitudes públicas de esa relación antes de ejecutar este bloque. Para la demo, cerrá o dejá inactivas las otras consolas.

```powershell
foreach ($ambiente in @('Prueba', 'Produccion')) {
    $cabecerasDemo = if ($ambiente -eq 'Prueba') { $hPrueba } else { $hProduccion }
    $resultados = foreach ($i in 1..31) {
        $r = Invoke-DemoWebRequest "$base/api/v1/envios?tamanoPagina=1" -Headers $cabecerasDemo
        [pscustomobject]@{
            Ambiente = $ambiente
            Solicitud = $i
            HTTP = [int]$r.StatusCode
            RetryAfter = $r.Headers['Retry-After'] -join ','
        }
    }
    $resultados | Format-Table
}
```

En una ejecución rápida que permanezca dentro de la ventana de cada cuota y sin tráfico concurrente, esperar 30 respuestas 200 y luego 429 para prueba; las 31 de producción responden 200. Mostrar `Retry-After` en el rechazo. Si la tanda cruza el reinicio de ventana, repetir después de la espera; no deducir un fallo sólo por no ver 429 en esa tanda.

**Explicación:** «Producción tiene una cuota mayor. Generar otra clave para el mismo comercio, operador y ambiente no multiplica la cuota».

El contador implementado es local a cada instancia de la API. Presentar esta comprobación como una demostración sobre una instancia.

### Comprobar último uso y revocación — CU-80

1. Después de la prueba de límites, esperar los segundos de `Retry-After` antes de continuar.
2. Volver al Portal, pulsar **Actualizar** y mostrar que las claves registran último uso.
3. Revocar la clave de prueba y pulsar **Confirmar revocación**.
4. Mostrar su estado **Revocada**.
5. Ejecutar en la terminal:

```powershell
(Invoke-DemoWebRequest "$base/api/v1/envios" -Headers $hPrueba).StatusCode
```

Esperar 401 con la misma clave que antes funcionaba.

### Evidencia para registrar durante el ensayo

| Condición | Evidencia |
| --- | --- |
| API documentada y versionada | `/api/v1`, esquemas y OpenAPI descargable. |
| Claves por comercio | Generación, secreto mostrado una vez, último uso y 401 tras revocar; 404 al consultar un envío ajeno con otra relación. |
| Ambiente de pruebas | 201 de prueba, ausencia en Portal operativo, lectura cruzada 404 y misma referencia con distinto número en producción. |
| Tasa diferenciada | Tanda de prueba con 429 y `Retry-After`; tanda equivalente de producción con 200. |
| Documentación navegable | Acceso sin sesión y ejemplo ejecutado; producción rechazada con 403 en el destino de la consola. |

Registrar fecha, entorno, códigos y números obtenidos. No marcar como verificados los escenarios que no se ejecutaron.
