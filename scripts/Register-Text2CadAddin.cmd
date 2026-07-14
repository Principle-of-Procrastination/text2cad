@echo off
setlocal

fltmc >nul 2>&1
if errorlevel 1 (
  echo Run this script from an Administrator Command Prompt.
  exit /b 1
)

tasklist /FI "IMAGENAME eq SLDWORKS.exe" 2>nul | find /I "SLDWORKS.exe" >nul
if not errorlevel 1 (
  echo Close SOLIDWORKS before registering or updating the Add-in.
  exit /b 1
)

set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Debug"

set "ASSEMBLY=%~dp0..\src\Text2Cad.Addin\bin\x64\%CONFIGURATION%\net48\Text2Cad.Addin.dll"
set "REGASM=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

if not exist "%ASSEMBLY%" (
  echo Text2Cad.Addin.dll was not found: "%ASSEMBLY%"
  echo Build Text2Cad.sln as %CONFIGURATION%^|x64 first.
  exit /b 1
)

if not exist "%REGASM%" (
  echo 64-bit RegAsm.exe was not found: "%REGASM%"
  exit /b 1
)

"%REGASM%" /nologo /unregister "%ASSEMBLY%" >nul 2>&1
"%REGASM%" /nologo /codebase "%ASSEMBLY%"
if errorlevel 1 exit /b %errorlevel%

echo Registered Text2CAD Add-in: "%ASSEMBLY%"
exit /b 0
