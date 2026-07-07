@echo off
setlocal
REM ============================================================
REM  SolidWorks Copilot MVP - installer (register the add-in)
REM  Run as Administrator (this script self-elevates).
REM ============================================================

REM --- self-elevate if not already admin ---
net session >nul 2>&1 || (echo 需要管理员权限,正在提升... & powershell -Command "Start-Process -Verb RunAs -FilePath '%~f0'" & exit /b)

set "REGASM=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

REM --- locate the built DLL (next to this script, or in the build output) ---
set "DLL=%~dp0SwCopilot.dll"
if not exist "%DLL%" set "DLL=%~dp0..\src\SwCopilot\bin\Release\SwCopilot.dll"
if not exist "%DLL%" set "DLL=%~dp0..\src\SwCopilot\bin\Debug\SwCopilot.dll"
if not exist "%DLL%" (echo [ERROR] 找不到 SwCopilot.dll,请先在 Visual Studio 里 Build ^(Release / x64^)。 & pause & exit /b 1)

if not exist "%REGASM%" (echo [ERROR] 找不到 RegAsm.exe: "%REGASM%" & pause & exit /b 1)

echo 正在注册: "%DLL%"
"%REGASM%" /codebase "%DLL%"
if %errorlevel% neq 0 (echo [ERROR] 注册失败。 & pause & exit /b 1)

echo.
echo 完成。打开 SolidWorks,然后 工具 ^> 插件 ^> 勾选 "SolidWorks Copilot"。
echo (关于 "unsigned assembly /codebase" 的提示在开发阶段属正常。)
pause
