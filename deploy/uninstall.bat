@echo off
setlocal
REM ============================================================
REM  SolidWorks Copilot MVP - uninstaller (unregister add-in)
REM  Run as Administrator (this script self-elevates).
REM ============================================================

net session >nul 2>&1 || (echo 需要管理员权限,正在提升... & powershell -Command "Start-Process -Verb RunAs -FilePath '%~f0'" & exit /b)

set "REGASM=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

set "DLL=%~dp0SwCopilot.dll"
if not exist "%DLL%" set "DLL=%~dp0..\src\SwCopilot\bin\Release\SwCopilot.dll"
if not exist "%DLL%" set "DLL=%~dp0..\src\SwCopilot\bin\Debug\SwCopilot.dll"
if not exist "%DLL%" (echo [ERROR] 找不到 SwCopilot.dll。 & pause & exit /b 1)

echo 正在反注册: "%DLL%"
"%REGASM%" /u "%DLL%"
echo 完成。
pause
