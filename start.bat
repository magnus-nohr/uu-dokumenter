@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
if not exist publish\UuDok.Web.exe (
  echo Appen er ikke installert ennaa. Kjoer installer.bat foerst.
  pause
  exit /b 1
)
set PORT=5317
tasklist /fi "imagename eq UuDok.Web.exe" | find /i "UuDok.Web.exe" >nul
if not errorlevel 1 (
  echo Appen kjoerer allerede. Aapner nettleseren.
  start "" http://127.0.0.1:%PORT%/
  exit /b 0
)
echo Starter UU-dokumenter paa http://127.0.0.1:%PORT%/ (bare tilgjengelig paa denne maskinen)
start "UU-dokumenter" /min /d "%~dp0publish" "%~dp0publish\UuDok.Web.exe" --UuDok:Port=%PORT%
rem Vent til tjenesten svarer
for /l %%i in (1,1,30) do (
  powershell -NoProfile -Command "try { (Invoke-WebRequest -UseBasicParsing http://127.0.0.1:%PORT%/api/session -TimeoutSec 1) ^| Out-Null; exit 0 } catch { exit 1 }" >nul 2>nul && goto :klar
  ping -n 2 127.0.0.1 >nul
)
echo Appen svarte ikke. Se vinduet "UU-dokumenter" for feilmeldinger.
exit /b 1
:klar
start "" http://127.0.0.1:%PORT%/
echo Appen kjoerer. Stopp den med stopp.bat.
