param(
    [string]$InstallDirectory = "$env:ProgramFiles\Update Center Network"
)

$ErrorActionPreference = "Stop"
$serviceName = "UpdateCenterAgent"
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Esegui questa operazione come amministratore."
}

$sourceDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot)
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    $existingAgent = $serviceInfo.PathName.Trim().Trim('"')
    $targetDirectory = [System.IO.Path]::GetFullPath((Split-Path -Parent $existingAgent))
    if ($existingService.Status -ne "Stopped") { Stop-Service -Name $serviceName -Force }
} else {
    $targetDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
}

$programFilesRoot = [System.IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\') + '\'
if (-not $targetDirectory.StartsWith($programFilesRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "L'Agent deve essere installato in una sottocartella di Program Files."
}

$sourceAgent = Join-Path $sourceDirectory "UpdateCenter.Agent.exe"
if (-not (Test-Path -LiteralPath $sourceAgent)) {
    throw "UpdateCenter.Agent.exe non trovato nella cartella della preview."
}

New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDirectory -File | Copy-Item -Destination $targetDirectory -Force
$installedAgent = Join-Path $targetDirectory "UpdateCenter.Agent.exe"
if ($null -eq $existingService) {
    New-Service `
        -Name $serviceName `
        -BinaryPathName ('"' + $installedAgent + '"') `
        -DisplayName "Update Center Agent" `
        -Description "Agente locale di Update Center. La gestione LAN resta disabilitata finché non viene autorizzata." `
        -StartupType Automatic | Out-Null
} else {
    Set-Service -Name $serviceName -StartupType Automatic
}
Start-Service -Name $serviceName
Write-Host "Update Center Agent installato o aggiornato e avviato."
