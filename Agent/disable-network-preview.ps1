param()

$ErrorActionPreference = "Stop"
$serviceName = "UpdateCenterAgent"
$ruleNames = @(
    "UpdateCenterAgent-HTTPS-LAN",
    "UpdateCenterAgent-Discovery-LAN",
    "UpdateCenterAgent-HTTPS-Private",
    "UpdateCenterAgent-Discovery-Private"
)
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Esegui questa operazione come amministratore."
}

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($null -ne $service) {
    $agentPath = $service.PathName.Trim().Trim('"')
    if ((Get-Service -Name $serviceName).Status -ne "Running") { Start-Service -Name $serviceName }
    & $agentPath --network-disable
    if ($LASTEXITCODE -ne 0) { throw "Disabilitazione della gestione rete non riuscita." }
}
$ruleNames | ForEach-Object { Remove-NetFirewallRule -Name $_ -ErrorAction SilentlyContinue }
if ($null -ne $service) { Restart-Service -Name $serviceName }
Write-Host "Gestione rete disabilitata e regole firewall rimosse."
