@echo off
setlocal

fltmc >nul 2>&1
if errorlevel 1 (
  echo Run this script from an Administrator Command Prompt.
  exit /b 1
)

tasklist /FI "IMAGENAME eq SLDWORKS.exe" 2>nul | find /I "SLDWORKS.exe" >nul
if not errorlevel 1 (
  echo Close SOLIDWORKS before unregistering the Add-in.
  exit /b 1
)

set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Debug"

set "ASSEMBLY=%~dp0..\src\Text2Cad.Addin\bin\x64\%CONFIGURATION%\net48\Text2Cad.Addin.dll"
set "REGASM=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

if not exist "%ASSEMBLY%" (
  echo Text2Cad.Addin.dll was not found: "%ASSEMBLY%"
  exit /b 1
)

"%REGASM%" /nologo /unregister "%ASSEMBLY%"
if errorlevel 1 exit /b %errorlevel%

echo Unregistered Text2CAD Add-in: "%ASSEMBLY%"
exit /b 0
