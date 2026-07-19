@echo off
setlocal
cd /d "%~dp0"

set "DOTNET_EXE=dotnet.exe"
set "SDK_PRESENTE="

where dotnet.exe >nul 2>nul
if not errorlevel 1 (
    for /f "usebackq delims=" %%S in (`dotnet.exe --list-sdks 2^>nul`) do (
        for /f "tokens=1 delims=." %%M in ("%%S") do if %%M GEQ 8 set "SDK_PRESENTE=1"
    )
)

if not defined SDK_PRESENTE goto INSTALLA_SDK
goto AVVIA_BUILD

:INSTALLA_SDK
echo.
echo Il comando dotnet e' presente, ma non e' installato alcun SDK compatibile.
echo Per creare UpdateCenter.exe serve il compilatore ufficiale Microsoft .NET 8 SDK.
echo.

where winget.exe >nul 2>nul
if errorlevel 1 (
    echo WinGet non e' disponibile. Installa manualmente .NET 8 SDK da:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

choice /C SN /M "Vuoi installare .NET 8 SDK ora tramite WinGet"
if errorlevel 2 exit /b 1

winget.exe install --id Microsoft.DotNet.SDK.8 --exact --source winget --accept-package-agreements --accept-source-agreements
set "WINGET_EXIT=%ERRORLEVEL%"

set "DOTNET_ROOT=%ProgramFiles%\dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
if exist "%DOTNET_ROOT%\dotnet.exe" set "DOTNET_EXE=%DOTNET_ROOT%\dotnet.exe"

set "SDK_PRESENTE="
for /f "usebackq delims=" %%S in (`"%DOTNET_EXE%" --list-sdks 2^>nul`) do (
    for /f "tokens=1 delims=." %%M in ("%%S") do if %%M GEQ 8 set "SDK_PRESENTE=1"
)
if not defined SDK_PRESENTE (
    echo.
    echo Installazione o rilevamento di .NET 8 SDK non riusciti. Codice WinGet: %WINGET_EXIT%
    echo Puoi installarlo manualmente da https://dotnet.microsoft.com/download/dotnet/8.0
    echo Dopo l'installazione chiudi questa finestra e avvia nuovamente CREA-EXE.bat.
    pause
    exit /b 1
)

:AVVIA_BUILD
if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%ProgramFiles%\dotnet"
    set "PATH=%ProgramFiles%\dotnet;%PATH%"
)

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
