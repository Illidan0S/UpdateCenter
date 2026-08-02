param()

$ErrorActionPreference = "Stop"
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "La configurazione dell'Agent richiede i privilegi di amministratore."
}

& (Join-Path $PSScriptRoot "install-agent-preview.ps1")
& (Join-Path $PSScriptRoot "enable-network-preview.ps1")

Write-Host "Questo PC è ora disponibile per la gestione sulla LAN corrente."
