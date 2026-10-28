$ErrorActionPreference = 'Stop'
$proyecto = $PSScriptRoot
$salida = Join-Path ([IO.Path]::GetTempPath()) ('receptor-demo-' + [Guid]::NewGuid().ToString('N') + '.log')
$errores = $salida + '.err'
$proceso = $null
$cliente = $null

function Comprobar($condicion, $mensaje) {
    if (-not $condicion) { throw $mensaje }
}

try {
    dotnet build (Join-Path $proyecto 'Logistica.ReceptorDemo.csproj') -m:1 -nr:false -p:UseSharedCompilation=false -v:quiet
    Comprobar ($LASTEXITCODE -eq 0) 'No se pudo compilar el receptor.'
    $escucha = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $escucha.Start()
    $puerto = $escucha.LocalEndpoint.Port
    $escucha.Stop()
    $base = 'http://127.0.0.1:' + $puerto
    $dll = Join-Path $proyecto 'bin/Debug/net10.0/Logistica.ReceptorDemo.dll'
    $proceso = Start-Process dotnet -ArgumentList @(('"' + $dll + '"'), '--urls', $base,
        '--Logging:LogLevel:Default', 'Warning') -WorkingDirectory $proyecto -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $salida -RedirectStandardError $errores

    $clave = ''
    for ($i = 0; $i -lt 100; $i++) {
        if ($proceso.HasExited) { throw ('El receptor se cerro: ' + (Get-Content $errores -Raw)) }
        if (Test-Path $salida) {
            $texto = Get-Content $salida -Raw
            if ($texto -match '#clave=([A-F0-9]{64})') { $clave = $Matches[1]; break }
        }
        Start-Sleep -Milliseconds 200
    }
    Comprobar ($clave.Length -eq 64) 'El receptor no inicio a tiempo.'
    Add-Type -AssemblyName System.Net.Http
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $cliente = [Net.Http.HttpClient]::new($handler)
    $cliente.BaseAddress = [Uri]$base
    $cliente.Timeout = [TimeSpan]::FromSeconds(10)

    function Solicitar($metodo, $ruta, $cuerpo = $null, $autenticada = $true) {
        $peticion = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($metodo), $ruta)
        try {
            if ($autenticada) { $peticion.Headers.Add('X-Panel-Clave', $clave) }
            if ($null -ne $cuerpo) {
                $peticion.Content = [Net.Http.StringContent]::new($cuerpo, [Text.Encoding]::UTF8, 'application/json')
                $peticion.Headers.Add('X-Aviso-Fecha', '2026-10-28T12:00:00Z')
                $peticion.Headers.Add('X-Aviso-Firma', 'firma-demo')
            }
            try { $respuesta = $cliente.SendAsync($peticion).GetAwaiter().GetResult() }
            catch { throw ("Fallo HTTP en $metodo ${ruta}: " + $_.Exception.ToString()) }
            try {
                return [pscustomobject]@{ Codigo = [int]$respuesta.StatusCode; Cuerpo = $respuesta.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
            } finally { $respuesta.Dispose() }
        } finally { $peticion.Dispose() }
    }

    Comprobar ((Solicitar 'GET' '/' $null $false).Codigo -eq 200) 'No se sirve el panel.'
    Comprobar ((Solicitar 'GET' '/panel.js' $null $false).Codigo -eq 200) 'No se sirve el JavaScript.'
    Comprobar ((Solicitar 'GET' '/api/estado' $null $false).Codigo -eq 401) 'El historial quedo sin proteccion.'
    Comprobar ((Solicitar 'POST' '/api/respuesta' '{"codigo":503}' $false).Codigo -eq 401) 'Se pudo cambiar sin clave.'
    $estado = (Solicitar 'GET' '/api/estado').Cuerpo | ConvertFrom-Json
    Comprobar ($estado.codigo -eq 200) 'El estado inicial no es 200.'
    $ruta = $estado.rutaAvisos
    $aviso = '{"id":"aviso-demo","numeroEnvio":"ENV-DEMO","prueba":false}'
    Comprobar ((Solicitar 'POST' $ruta $aviso $false).Codigo -eq 200) 'No respondio 200.'
    Comprobar ((Solicitar 'POST' '/api/respuesta' '{"codigo":503}').Codigo -eq 200) 'No cambio a 503.'
    Comprobar ((Solicitar 'POST' $ruta $aviso $false).Codigo -eq 503) 'No respondio 503.'
    Comprobar ((Solicitar 'POST' '/api/respuesta' '{"codigo":201}').Codigo -eq 400) 'Acepto un modo invalido.'
    Comprobar ((Solicitar 'POST' $ruta $aviso $false).Codigo -eq 503) 'El modo invalido altero la respuesta.'
    Comprobar ((Solicitar 'POST' '/api/respuesta' '{"codigo":200}').Codigo -eq 200) 'No volvio a 200.'
    Comprobar ((Solicitar 'POST' $ruta $aviso $false).Codigo -eq 200) 'No recupero la respuesta 200.'
    $estado = (Solicitar 'GET' '/api/estado').Cuerpo | ConvertFrom-Json
    Comprobar ($estado.total -eq 4) 'Faltan intentos en el historial.'
    Comprobar (($estado.recibidos.codigoRespuesta -join ',') -eq '200,503,503,200') 'Los codigos registrados no coinciden.'
    Comprobar ($estado.recibidos[0].cuerpo -eq $aviso) 'No se conservo el cuerpo original.'
    Comprobar ($estado.recibidos[0].firma -eq 'firma-demo') 'No se conservo la cabecera de firma.'
    for ($i = 0; $i -lt 101; $i++) { $null = Solicitar 'POST' $ruta $aviso $false }
    $estado = (Solicitar 'GET' '/api/estado').Cuerpo | ConvertFrom-Json
    Comprobar ($estado.total -eq 105 -and $estado.recibidos.Count -eq 100) 'No se limito el historial a 100 POST.'
    Comprobar ((Solicitar 'POST' $ruta ('x' * 70000) $false).Codigo -eq 413) 'No se limito el cuerpo a 64 KiB.'
    $null = Solicitar 'POST' '/api/respuesta' '{"codigo":503}'
    Comprobar ((Solicitar 'DELETE' '/api/recibidos').Codigo -eq 204) 'No se pudo limpiar.'
    $estado = (Solicitar 'GET' '/api/estado').Cuerpo | ConvertFrom-Json
    Comprobar ($estado.total -eq 0 -and $estado.recibidos.Count -eq 0 -and $estado.codigo -eq 503) 'La limpieza modifico el modo o dejo avisos.'
    Write-Host 'OK: panel, autorizacion, alternancia 200/503, historial, firma registrada, limite de 100 avisos, limite de cuerpo y limpieza.'
} finally {
    if ($null -ne $cliente) { $cliente.Dispose() }
    if ($null -ne $proceso -and -not $proceso.HasExited) { Stop-Process -Id $proceso.Id -Force }
    Remove-Item -LiteralPath $salida, $errores -ErrorAction SilentlyContinue
}
