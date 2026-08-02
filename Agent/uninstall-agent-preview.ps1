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

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$installDirectory = Join-Path $env:ProgramFiles "Update Center Network"
if ($null -ne $service) {
    $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    $agentPath = $serviceInfo.PathName.Trim().Trim('"')
    $installDirectory = Split-Path -Parent $agentPath
    if ($service.Status -eq "Stopped") { Start-Service -Name $serviceName }
    try { & $agentPath --network-disable | Out-Null } catch { }
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Rimozione del servizio non riuscita." }
}

$ruleNames | ForEach-Object { Remove-NetFirewallRule -Name $_ -ErrorAction SilentlyContinue }

$programFilesRoot = [System.IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\') + '\'
$resolvedInstall = [System.IO.Path]::GetFullPath($installDirectory)
if ($resolvedInstall.StartsWith($programFilesRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
    $resolvedInstall -ne $programFilesRoot.TrimEnd('\') -and
    (Test-Path -LiteralPath $resolvedInstall)) {
    Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
}

$agentData = Join-Path $env:ProgramData "UpdateCenter\Agent"
if (Test-Path -LiteralPath $agentData) { Remove-Item -LiteralPath $agentData -Recurse -Force }

Write-Host "Update Center Agent, autorizzazioni, regole firewall, binari e dati locali sono stati rimossi."
