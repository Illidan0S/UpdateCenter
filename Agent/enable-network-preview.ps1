param()

$ErrorActionPreference = "Stop"
$serviceName = "UpdateCenterAgent"
$tcpRuleName = "UpdateCenterAgent-HTTPS-LAN"
$udpRuleName = "UpdateCenterAgent-Discovery-LAN"
$obsoleteRuleNames = @("UpdateCenterAgent-HTTPS-Private", "UpdateCenterAgent-Discovery-Private")
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Esegui questa operazione come amministratore."
}

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($null -eq $service) { throw "Installa prima il servizio Update Center Agent." }
$agentPath = $service.PathName.Trim().Trim('"')
if (-not (Test-Path -LiteralPath $agentPath)) { throw "Eseguibile Agent installato non trovato." }
if ((Get-Service -Name $serviceName).Status -ne "Running") { Start-Service -Name $serviceName }

$activeConfigurations = @(Get-NetIPConfiguration | Where-Object {
    $null -ne $_.IPv4DefaultGateway -and
    $null -ne $_.IPv4Address -and
    $_.NetAdapter.Status -eq "Up"
})
$interfaceAliases = @($activeConfigurations | ForEach-Object { $_.InterfaceAlias } | Sort-Object -Unique)
if ($interfaceAliases.Count -eq 0) {
    throw "Nessuna rete locale attiva è stata rilevata. Collega Ethernet o Wi-Fi e riprova."
}

& $agentPath --network-enable
if ($LASTEXITCODE -ne 0) { throw "Abilitazione della gestione rete non riuscita." }

@($tcpRuleName, $udpRuleName) + $obsoleteRuleNames | ForEach-Object {
    Remove-NetFirewallRule -Name $_ -ErrorAction SilentlyContinue
}
New-NetFirewallRule -Name $tcpRuleName -DisplayName "Update Center Agent HTTPS (solo LAN corrente)" `
    -Description "Consente la gestione Update Center soltanto dalla sottorete locale e dalle interfacce autorizzate." `
    -Direction Inbound -Action Allow -Profile Any -Program $agentPath -Protocol TCP -LocalPort 47382 `
    -RemoteAddress LocalSubnet -InterfaceAlias $interfaceAliases -EdgeTraversalPolicy Block | Out-Null
New-NetFirewallRule -Name $udpRuleName -DisplayName "Update Center Agent Discovery (solo LAN corrente)" `
    -Description "Consente il rilevamento Update Center soltanto dalla sottorete locale e dalle interfacce autorizzate." `
    -Direction Inbound -Action Allow -Profile Any -Program $agentPath -Protocol UDP -LocalPort 47381 `
    -RemoteAddress LocalSubnet -InterfaceAlias $interfaceAliases -EdgeTraversalPolicy Block | Out-Null
Restart-Service -Name $serviceName
Write-Host "Gestione abilitata sulla LAN corrente senza modificare il profilo di rete Windows."
