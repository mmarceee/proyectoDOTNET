# Demostración del opcional 7.4: API pública para comercios

Guion preparado a partir de la implementación del repositorio. Los resultados indicados son los esperados; esta guía no registra una ejecución de la demostración.

Duración sugerida: 12 a 15 minutos. Usar el entorno local y PowerShell 7 (`pwsh`) para los comandos con `-SkipHttpErrorCheck` y `-MaskInput`.

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

Mantener disponible una terminal con `pwsh`. No ejecutar la prueba de saturación hasta el paso 8: comparte cuota con las consultas de la consola web.

**Presentación:** «Esta API permite que el sistema de un comercio cree y consulte envíos sin usar manualmente el Portal. Voy a mostrar las cinco condiciones del opcional: versionado y documentación, claves, pruebas, límites diferenciados y documentación navegable».

## 2. Mostrar la documentación y el versionado

1. Abrir `/api/docs` en una ventana sin sesión para mostrar el acceso anónimo.
2. Mostrar la versión `v1` y las operaciones `POST /api/v1/envios`, `GET /api/v1/envios` y `GET /api/v1/envios/{numero}`.
3. Desplegar un esquema de solicitud, los códigos de respuesta y los modelos.
4. Abrir **Descargar OpenAPI**: `/api/openapi/v1.json`.
5. Señalar la autenticación por cabecera `X-Api-Key` y las cuotas publicadas en la propia página.

**Explicación:** «El contrato está versionado en la URL y la documentación se genera desde los endpoints. La consola permite ejecutar ejemplos con claves de prueba».

## 3. Generar las claves desde el Portal — CU-80

En **Claves de API**, con Operador Demo seleccionado:

1. Nombre: `Demo opcional - prueba`; ambiente: **Prueba**; pulsar **Generar clave**.
2. Copiar el secreto antes de pulsar **Ya la guardé**. Se muestra una sola vez.
3. Generar otra con nombre `Demo opcional - produccion` y ambiente **Producción**. Copiar también ese secreto.
4. Mostrar la tabla: nombre, ambiente, prefijo, creación, último uso y estado.

**Explicación:** «La clave identifica al comercio y al operador seleccionado. El servidor obtiene de ella el ambiente; el cliente no elige otro comercio enviando un identificador en el JSON».

En PowerShell 7, cargar ambas claves sin escribirlas en el historial:

```powershell
$base = 'http://localhost:8080'
$clavePrueba = Read-Host 'Pegar clave de prueba' -MaskInput
$claveProduccion = Read-Host 'Pegar clave de produccion' -MaskInput
$hPrueba = @{ 'X-Api-Key' = $clavePrueba }
$hProduccion = @{ 'X-Api-Key' = $claveProduccion }
```

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

Para mostrar progreso parcial, preparar previamente un envío de producción de tres bultos mediante el flujo operativo del proyecto. Con dos entregados y uno pendiente, comprobar en la API `estado = EntregadoParcialmente`, `entregados = 2`, `totalBultos = 3` y `terminado = false`. Si el tercero luego se devuelve al comercio, comprobar `terminado = true` y un devuelto. La consola de documentación no simula entregas.

## 6. Demostrar separación entre prueba y producción

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
(Invoke-WebRequest "$base/api/v1/envios/$numeroPrueba" -Headers $hProduccion -SkipHttpErrorCheck).StatusCode

# El destino de la consola rechaza produccion por el servidor: 403.
(Invoke-WebRequest "$base/api/v1/pruebas/envios" -Headers $hProduccion -SkipHttpErrorCheck).StatusCode
```

Con una referencia nueva exclusiva de esta demo, esperar total 1 en prueba y total 0 en producción. Buscar también esa referencia en el listado operativo del Portal: no debe aparecer.

Para comprobar que la referencia se puede reutilizar en ambos ambientes, crear un envío de producción con la misma referencia:

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

$altaProduccion = Invoke-WebRequest "$base/api/v1/envios" -Method Post -Headers $hProduccion `
    -ContentType 'application/json; charset=utf-8' -Body $cuerpo -SkipHttpErrorCheck
$altaProduccion.StatusCode
$altaProduccion.Content
```

Esperar 201 y un número distinto al de prueba. Ahora el listado de producción encuentra su propio envío y el Portal operativo puede mostrarlo. Esta alta crea un envío operativo en el entorno local.

**Explicación:** «Las pruebas usan el mismo despliegue, pero no entran en la operación real. La misma referencia puede existir en los dos ambientes sin colisión».

Para mostrar además aislamiento por operador, generar una clave de prueba seleccionando otro operador con relación activa, por ejemplo Operador Este si conserva los datos iniciales. Consultar el número de prueba de Operador Demo con esa clave: esperar 404. Para aislamiento entre comercios, preparar una segunda cuenta de comercio con relación activa y repetir la consulta del número ajeno.

## 7. Mostrar autenticación y errores

```powershell
# Una peticion sin clave recibe 401, aunque haya una sesion abierta en el navegador.
(Invoke-WebRequest "$base/api/v1/envios" -SkipHttpErrorCheck).StatusCode

# Paginacion invalida: 400.
(Invoke-WebRequest "$base/api/v1/envios?tamanoPagina=0" -Headers $hPrueba -SkipHttpErrorCheck).StatusCode
```

Opcionalmente, en el alta de la consola:

- Usar `referenciaComercio = ""`: esperar 400.
- Usar una referencia nueva y un código postal válido en formato pero fuera de cobertura: esperar 422. Verificarlo en el ensayo con la configuración vigente.

Usar siempre una referencia nueva para probar cobertura o tarifa: con una referencia existente se devuelve el envío anterior antes de cotizar.

## 8. Mostrar limitación de tasa diferenciada

La configuración inicial es 30 solicitudes/minuto en prueba y 120 en producción, compartidas por relación comercial y ambiente. Además, hay 600/minuto por IP antes de autenticar. Si se personalizaron, usar los valores publicados en `/api/docs`.

Las altas y consultas anteriores consumieron cuota. Para mostrar una comparación limpia, esperar al menos 61 segundos sin solicitudes públicas de esa relación antes de ejecutar este bloque. Para la demo, cerrá o dejá inactivas las otras consolas.

```powershell
foreach ($ambiente in @('Prueba', 'Produccion')) {
    $cabecerasDemo = if ($ambiente -eq 'Prueba') { $hPrueba } else { $hProduccion }
    $resultados = foreach ($i in 1..31) {
        $r = Invoke-WebRequest "$base/api/v1/envios?tamanoPagina=1" `
            -Headers $cabecerasDemo -SkipHttpErrorCheck
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

## 9. Mostrar último uso y revocación — CU-80

1. Después de la prueba de límites, esperar los segundos de `Retry-After` antes de continuar.
2. Volver al Portal, pulsar **Actualizar** y mostrar que las claves registran último uso.
3. Revocar la clave de prueba y pulsar **Confirmar revocación**.
4. Mostrar su estado **Revocada**.
5. Ejecutar en la terminal:

```powershell
(Invoke-WebRequest "$base/api/v1/envios" -Headers $hPrueba -SkipHttpErrorCheck).StatusCode
```

Esperar 401 con la misma clave que antes funcionaba.

**Cierre sugerido:** «Mostramos el contrato v1 y su documentación navegable, generación y revocación de claves por relación comercial, alta idempotente y consultas, separación de pruebas y producción, y cuotas diferentes con 429 y Retry-After».

## Evidencia para registrar durante el ensayo

| Condición | Evidencia |
| --- | --- |
| API documentada y versionada | `/api/v1`, esquemas y OpenAPI descargable. |
| Claves por comercio | Generación, secreto mostrado una vez, último uso y 401 tras revocar; 404 al consultar un envío ajeno con otra relación. |
| Ambiente de pruebas | 201 de prueba, ausencia en Portal operativo, lectura cruzada 404 y misma referencia con distinto número en producción. |
| Tasa diferenciada | Tanda de prueba con 429 y `Retry-After`; tanda equivalente de producción con 200. |
| Documentación navegable | Acceso sin sesión y ejemplo ejecutado; producción rechazada con 403 en el destino de la consola. |

Registrar fecha, entorno, códigos y números obtenidos. No marcar como verificados los escenarios que no se ejecutaron.
