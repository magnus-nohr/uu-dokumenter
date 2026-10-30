@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
echo.
echo === UU-dokumenter: installasjon ===
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK mangler. Installer .NET 10 SDK: winget install Microsoft.DotNet.SDK.10
  exit /b 1
)
dotnet --list-sdks | findstr /b "10." >nul
if errorlevel 1 (
  echo .NET 10 SDK mangler. Installer med: winget install Microsoft.DotNet.SDK.10
  exit /b 1
)
where npm >nul 2>nul
if errorlevel 1 (
  if exist "%ProgramFiles%\nodejs\npm.cmd" ( set "PATH=%ProgramFiles%\nodejs;%PATH%" ) else (
    echo Node.js mangler. Installer med: winget install OpenJS.NodeJS.LTS
    exit /b 1
  )
)

set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1

echo [1/4] Henter .NET-pakker (laaste versjoner)...
dotnet restore src\UuDok.Web --locked-mode || goto :feil
dotnet restore src\UuDok.WinHelper --locked-mode || goto :feil

echo [2/4] Bygger brukergrensesnittet...
pushd web
call npm ci --no-audit --no-fund || (popd & goto :feil)
call npm run build || (popd & goto :feil)
popd

echo [3/4] Bygger appen...
if exist publish rmdir /s /q publish
dotnet publish src\UuDok.Web -c Release -o publish --no-restore || goto :feil
dotnet publish src\UuDok.WinHelper -c Release -o publish\helper --no-restore || goto :feil

echo [4/4] Kontrollerer installasjonen...
if not exist publish\UuDok.Web.exe goto :feil
if not exist publish\helper\UuDok.WinHelper.exe goto :feil
if not exist publish\wwwroot\index.html goto :feil
publish\helper\UuDok.WinHelper.exe office-info

echo.
echo Ferdig. Start appen med start.bat.
exit /b 0

:feil
echo.
echo Installasjonen feilet. Se meldingene over.
exit /b 1
