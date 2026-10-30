@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
set DOTNET_CLI_TELEMETRY_OPTOUT=1
rem Tekniske bevis (fase A) mot Word/PowerPoint paa denne maskinen. Resultat: docs\tekniske-bevis\RESULTATER.md
dotnet build src\UuDok.WinHelper -v q || exit /b 1
dotnet build probes\UuDok.Probes -v q || exit /b 1
probes\UuDok.Probes\bin\Debug\net10.0\UuDok.Probes.exe "%~dp0."
