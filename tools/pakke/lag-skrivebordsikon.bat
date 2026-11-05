@echo off
setlocal
chcp 65001 >nul
if not exist "%~dp0publish\UuDok.Web.exe" (
  echo Fant ikke appen. Pakk ut hele ZIP-filen foerst, og kjoer denne filen fra den utpakkede mappen.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\lag-snarvei.ps1"
if errorlevel 1 (
  echo Ikonet kunne ikke lages. Du kan starte appen med start.bat.
  pause
  exit /b 1
)
echo.
echo Ferdig. Start appen med ikonet UU-dokumenter paa skrivebordet.
pause
