@echo off
setlocal
cd /d "%~dp0"

if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%ProgramFiles%\dotnet"
    set "PATH=%ProgramFiles%\dotnet;%PATH%"
)

dotnet.exe --version >nul 2>nul
if not errorlevel 1 goto AVVIA_BUILD

echo.
echo Per creare UpdateCenter.exe serve .NET SDK 10 stabile, come indicato in global.json.
echo.

where winget.exe >nul 2>nul
if errorlevel 1 (
    echo WinGet non e' disponibile. Installa manualmente .NET SDK 10 da:
    echo https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

choice /C SN /M "Vuoi installare .NET SDK 10 ora tramite WinGet"
if errorlevel 2 exit /b 1

winget.exe install --id Microsoft.DotNet.SDK.10 --exact --source winget --accept-package-agreements --accept-source-agreements
set "WINGET_EXIT=%ERRORLEVEL%"

set "DOTNET_ROOT=%ProgramFiles%\dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
dotnet.exe --version >nul 2>nul
if errorlevel 1 (
    echo.
    echo Installazione o rilevamento di .NET SDK 10 non riusciti. Codice WinGet: %WINGET_EXIT%
    echo Puoi installarlo manualmente da https://dotnet.microsoft.com/download/dotnet/10.0
    echo Dopo l'installazione chiudi questa finestra e avvia nuovamente CREA-EXE.bat.
    pause
    exit /b 1
)

:AVVIA_BUILD
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
    echo.
    echo Compilazione non riuscita. Leggi il messaggio mostrato sopra.
    pause
    exit /b 1
)

echo.
echo Eseguibile creato correttamente in:
echo %~dp0dist\UpdateCenter.exe
echo.
choice /C SN /M "Vuoi avviare Update Center adesso"
if errorlevel 2 exit /b 0
start "" "%~dp0dist\UpdateCenter.exe"
exit /b 0
