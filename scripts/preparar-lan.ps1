#requires -Version 7.0
param(
    [Parameter(Mandatory)][System.Net.IPAddress]$Ip,
    [switch]$SoloPreparar,
    [switch]$SinCompilar
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Este preparador está destinado al equipo Windows del proyecto.' }
if ($Ip.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or [System.Net.IPAddress]::IsLoopback($Ip)) {
    throw 'Indicá la dirección IPv4 LAN del equipo, no localhost.'
}
$repo = Split-Path -Parent $PSScriptRoot
$lan = Join-Path $repo 'TestResults/lan'
$certs = Join-Path $lan 'certs'
$public = Join-Path $lan 'public'
$context = Join-Path $lan 'context'
$envFile = Join-Path $lan 'ambiente.env'
New-Item -ItemType Directory -Force -Path $certs,$public,$context | Out-Null

if (!(Test-Path -LiteralPath $envFile)) {
    $rootKey = [System.Security.Cryptography.RSA]::Create(3072)
    $serverKey = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $sha = [System.Security.Cryptography.HashAlgorithmName]::SHA256
        $padding = [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
        $caRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=proyectoDOTNET LAN Test CA', $rootKey, $sha, $padding)
        $caRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true,$true,0,$true))
        $caUsage = [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign
        $caRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($caUsage,$true))
        $caRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($caRequest.PublicKey,$false))
        $from = [DateTimeOffset]::UtcNow.AddMinutes(-10)
        $ca = $caRequest.CreateSelfSigned($from,[DateTimeOffset]::UtcNow.AddYears(1))
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=$Ip",$serverKey,$sha,$padding)
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
        $usage = [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($usage,$true))
        $oids = [System.Security.Cryptography.OidCollection]::new()
        [void]$oids.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids,$false))
        $san = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $san.AddIpAddress($Ip)
        $request.CertificateExtensions.Add($san.Build())
        $issued = $request.Create($ca,$from,[DateTimeOffset]::UtcNow.AddDays(90),[System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16))
        $server = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey($issued,$serverKey)
        $password = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $collection = [System.Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
        [void]$collection.Add($server)
        [void]$collection.Add([System.Security.Cryptography.X509Certificates.X509Certificate2]::new($ca.RawData))
        [System.IO.File]::WriteAllBytes((Join-Path $certs 'server.pfx'),$collection.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
        [System.IO.File]::WriteAllBytes((Join-Path $public 'proyectoDOTNET-LAN.cer'),$ca.RawData)
        [System.IO.File]::WriteAllText((Join-Path $public 'root.crt'),$ca.ExportCertificatePem())
        function RandomHex { [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) }
        $settings = @(
            "LAN_IP=$Ip", "LAN_CERT_PASSWORD=$password", "LAN_POSTGRES_PASSWORD=$(RandomHex)",
            "LAN_RABBIT_PASSWORD=$(RandomHex)", "LAN_TOKEN_KEY=$(RandomHex)",
            "LAN_AVISOS_KEY=$([Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))",
            'LAN_DEMO_PASSWORD=Demo-2026!'
        )
        [System.IO.File]::WriteAllLines($envFile,$settings)
        $ca.Dispose(); $issued.Dispose(); $server.Dispose()
    } finally { $rootKey.Dispose(); $serverKey.Dispose() }
} else {
    $savedIp = Get-Content -LiteralPath $envFile | Where-Object { $_ -like 'LAN_IP=*' }
    if ($savedIp -ne "LAN_IP=$Ip") { throw 'El ambiente existente tiene otra IP. No se cambian sus datos/certificados automáticamente.' }
    $savedPassword = (Get-Content -LiteralPath $envFile | Where-Object { $_ -like 'LAN_CERT_PASSWORD=*' }).Substring('LAN_CERT_PASSWORD='.Length)
    $existingCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        (Join-Path $certs 'server.pfx'), $savedPassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    try {
        if ($existingCertificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) {
            throw 'El certificado requiere renovación. Se conservan la configuración y los datos; no borrar ambiente.env.'
        }
    } finally { $existingCertificate.Dispose() }
}

# El contexto contiene sólo fuentes y configuración de compilación: no .env ni claves privadas.
$snapshotSource = [System.IO.Path]::GetFullPath((Join-Path $context 'src'))
$expectedSnapshot = [System.IO.Path]::GetFullPath((Join-Path $repo 'TestResults/lan/context/src'))
if ($snapshotSource -ne $expectedSnapshot) { throw 'La copia de fuentes no está en el directorio generado esperado.' }
foreach ($snapshotParent in @((Join-Path $repo 'TestResults'),$lan,$context)) {
    if ((Get-Item -LiteralPath $snapshotParent).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw 'Los directorios de la copia generada no deben ser enlaces.'
    }
}
if (Test-Path -LiteralPath $snapshotSource) {
    if ((Get-Item -LiteralPath $snapshotSource).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw 'La copia de fuentes no debe ser un enlace.'
    }
    Remove-Item -LiteralPath $snapshotSource -Recurse -Force
}
foreach ($file in @('global.json','Directory.Build.props','Directory.Packages.props')) {
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $context -Force
}
& robocopy (Join-Path $repo 'src') (Join-Path $context 'src') /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'No se pudo preparar la copia de fuentes para Docker.' }
$compose = @('compose','--env-file',$envFile,'-f',(Join-Path $repo 'infra/lan/compose.yml'))
if (!$SoloPreparar) {
    if (!$SinCompilar) {
        & docker @compose build api worker
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de las imágenes LAN.' }
    }
    & docker @compose up -d --wait --wait-timeout 180
    if ($LASTEXITCODE -ne 0) { throw 'El entorno LAN no pudo iniciarse.' }
}
Write-Output "PWA: https://${Ip}:8443/repartidor/"
Write-Output "CA pública: $(Join-Path $public 'proyectoDOTNET-LAN.cer')"
Write-Output 'Entorno aislado: voluptasexpress-lan. Datos y claves locales en TestResults/lan (ignorados por Git).'
