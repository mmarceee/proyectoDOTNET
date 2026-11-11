#requires -Version 7.0
#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$result = Join-Path $repo 'TestResults/lan/firewall-result.json'
$ruleName = 'proyectoDOTNET-LAN-RF32'
try {
    # Dirección y prefijo comprobados en este equipo. No abrir otra interfaz por accidente.
    $address = Get-NetIPAddress -AddressFamily IPv4 -IPAddress '192.168.1.7'
    if ($address.PrefixLength -ne 24) { throw 'Cambió la red LAN. Revisar dirección y subred antes de crear la regla.' }
    $rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
    if ($rule) {
        Set-NetFirewallRule -Name $ruleName -Enabled True -Profile Private -Direction Inbound -Action Allow | Out-Null
        $rule | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -LocalAddress '192.168.1.7' -RemoteAddress '192.168.1.0/24' | Out-Null
        $rule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol TCP -LocalPort 8081,8443 | Out-Null
    } else {
        New-NetFirewallRule -Name $ruleName -DisplayName 'proyectoDOTNET LAN RF32' -Group 'proyectoDOTNET LAN' `
            -Enabled True -Profile Private -Direction Inbound -Action Allow -Protocol TCP `
            -LocalAddress '192.168.1.7' -RemoteAddress '192.168.1.0/24' -LocalPort 8081,8443 `
            -EdgeTraversalPolicy Block | Out-Null
    }
    @{ Correcto=$true; Regla=$ruleName; Puertos=@(8081,8443); Subred='192.168.1.0/24'; Perfil='Private' } |
        ConvertTo-Json | Set-Content -LiteralPath $result
} catch {
    @{ Correcto=$false; Error=$_.Exception.Message } | ConvertTo-Json | Set-Content -LiteralPath $result
    throw
}
