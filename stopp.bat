@echo off
chcp 65001 >nul
tasklist /fi "imagename eq UuDok.Web.exe" | find /i "UuDok.Web.exe" >nul
if errorlevel 1 (
  echo Appen kjoerer ikke.
  exit /b 0
)
taskkill /im UuDok.Web.exe >nul 2>nul
ping -n 3 127.0.0.1 >nul
taskkill /f /im UuDok.Web.exe >nul 2>nul
taskkill /f /im UuDok.WinHelper.exe >nul 2>nul
echo UU-dokumenter er stoppet. Jobber som var i gang, fortsetter neste gang appen startes.
