LEGACY RESEARCH DIRECTORY — DO NOT USE FOR THE NEW TEXT2CAD IMPLEMENTATION
===========================================================================

This folder belongs to the retired src\SwCopilot proof of concept. That code,
its project file, and the deploy scripts are preserved only as research
evidence. They are not the Text2CAD 2026 product baseline.

Do not copy SOLIDWORKS 2025 or 2026 PIAs here for product development, and do not
run the legacy deploy scripts after building this project.

Historical build only
---------------------

If the old proof of concept must be compiled for comparison, obtain these
three Primary Interop Assemblies from the exact SOLIDWORKS version used for
that comparison:

    <SOLIDWORKS install>\api\redist\SolidWorks.Interop.sldworks.dll
    <SOLIDWORKS install>\api\redist\SolidWorks.Interop.swconst.dll
    <SOLIDWORKS install>\api\redist\SolidWorks.Interop.swpublished.dll

Place them in this directory temporarily. Never commit proprietary,
version-specific SOLIDWORKS binaries.

Current implementation baseline
-------------------------------

The new implementation will use:

    SOLIDWORKS 2025 API baseline + SOLIDWORKS 2026 compatibility validation
    Text2Cad.Addin: .NET Framework 4.8, x64
    Text2Cad.Host:  .NET 10 LTS, x64

The final local PIA reference convention will be defined with the new
Text2Cad solution. See the repository root README.md and
text2cad_mvp_spec.md for the current architecture.
