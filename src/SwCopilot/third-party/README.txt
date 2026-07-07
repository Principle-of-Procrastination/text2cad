Put the SolidWorks 2025 Primary Interop Assemblies (PIAs) here before building.

Copy these THREE files from your SolidWorks 2025 installation:

    C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\

into this folder (src\SwCopilot\third-party\):

    SolidWorks.Interop.sldworks.dll
    SolidWorks.Interop.swconst.dll
    SolidWorks.Interop.swpublished.dll

Notes
-----
- The .csproj references them by HintPath = third-party\<name>.dll with
  Copy Local = False. SolidWorks loads its own PIAs at runtime, so we do not
  ship them; we only need them at compile time.
- Use the 2025 versions so the interop matches the machine that will run the
  add-in (your colleague's SolidWorks 2025). If that machine is a different SW
  version, copy the PIAs from THAT version and rebuild.
- If the api\redist folder is missing, some installs place them under:
    C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\CLR2\  (older)
  or you can add the "SOLIDWORKS API SDK" component via the SW installer.
