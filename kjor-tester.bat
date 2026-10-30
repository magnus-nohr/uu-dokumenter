@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
set DOTNET_CLI_TELEMETRY_OPTOUT=1
echo === Enhets-, integrasjons- og webtester (uten Office) ===
set UUDOK_SKIP_OFFICE=1
dotnet build tests\UuDok.Tests -v q || exit /b 1
tests\UuDok.Tests\bin\Debug\net10.0\UuDok.Tests.exe -notrait "Category=Office" || set FEIL=1
set UUDOK_SKIP_OFFICE=
echo.
echo === Office-tester (krever Word og PowerPoint, tar noen minutter) ===
dotnet build src\UuDok.WinHelper -v q || exit /b 1
tests\UuDok.Tests\bin\Debug\net10.0\UuDok.Tests.exe -trait "Category=Office" -diagnostics || set FEIL=1
echo.
echo === Grensesnitt: Playwright + axe-core i Microsoft Edge ===
dotnet build tests\UuDok.E2E -v q || exit /b 1
tests\UuDok.E2E\bin\Debug\net10.0\UuDok.E2E.exe || set FEIL=1
if defined FEIL ( echo. & echo Noen tester feilet. & exit /b 1 )
echo. & echo Alle tester bestod.
