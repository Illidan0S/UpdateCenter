@echo off
setlocal
cd /d "%~dp0"

if not exist "%~dp0dist\UpdateCenter.exe" (
    if not exist "%~dp0CREA-EXE.bat" (
        echo Il pacchetto non contiene dist\UpdateCenter.exe.
        echo Scarica nuovamente il pacchetto completo dalla Release ufficiale.
        pause
        exit /b 1
    )
    echo L'eseguibile non e' ancora stato creato. Avvio la compilazione...
    call "%~dp0CREA-EXE.bat"
    if errorlevel 1 exit /b 1
)

set "DEST=%LOCALAPPDATA%\Programs\UpdateCenter"
if not exist "%DEST%" mkdir "%DEST%"
copy /Y "%~dp0dist\UpdateCenter.exe" "%DEST%\UpdateCenter.exe" >nul
copy /Y "%~dp0dist\LEGGIMI.txt" "%DEST%\LEGGIMI.txt" >nul 2>nul
copy /Y "%~dp0dist\UNINSTALLA.bat" "%DEST%\UNINSTALLA.bat" >nul

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Programs'),'Update Center.lnk')); $s.TargetPath=[IO.Path]::Combine($env:LOCALAPPDATA,'Programs\UpdateCenter\UpdateCenter.exe'); $s.WorkingDirectory=[IO.Path]::Combine($env:LOCALAPPDATA,'Programs\UpdateCenter'); $s.Description='Aggiornamenti software e driver da fonti ufficiali'; $s.Save()"

if errorlevel 1 (
    echo Il programma e' stato copiato, ma non e' stato possibile creare il collegamento Start.
) else (
    echo Update Center installato correttamente nel profilo utente.
)

start "" "%DEST%\UpdateCenter.exe"
exit /b 0
