; ============================================================
;  SolidWorks Copilot MVP - OPTIONAL Inno Setup script
;  Produces a single SwCopilot-Setup.exe your colleague can run.
;  Build with Inno Setup 6 (https://jrsoftware.org/isdl.php):
;     iscc SwCopilot.iss
;  Requires the project to be built first (Release / x64).
;  If you prefer zero extra tooling, just ship install.bat + SwCopilot.dll.
; ============================================================

[Setup]
AppName=SolidWorks Copilot MVP
AppVersion=0.1.0
AppPublisher=SolidWorks Copilot
DefaultDirName={commonpf}\SwCopilot
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
OutputBaseFilename=SwCopilot-Setup
Compression=lzma
SolidCompression=yes

[Files]
Source: "..\src\SwCopilot\bin\Release\SwCopilot.dll"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; \
  Parameters: "/codebase ""{app}\SwCopilot.dll"""; \
  StatusMsg: "Registering SolidWorks add-in..."; \
  Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; \
  Parameters: "/u ""{app}\SwCopilot.dll"""; \
  Flags: runhidden waituntilterminated
