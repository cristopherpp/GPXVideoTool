GPXVideoTools v1.0 (libVLC)
===========================

Contents:
- Source files (.cs) for Visual Studio Class Library (net48).
- GPX import, UTM conversion, Ribbon UI, marker, libVLC-based video viewer.
- GPX -> CSV export.

How to build:
1. Open Visual Studio 2022 and create a new Class Library (.NET Framework) project targeting .NET Framework 4.8.
2. Replace the generated .cs files with the files in this package (or add them).
3. Install NuGet packages:
   - LibVLCSharp.WinForms
   - LibVLCSharp
4. Add references to AutoCAD managed assemblies (from your AutoCAD installation):
   - acmgd.dll
   - acdbmgd.dll
   Set 'Copy Local' = False for those references.
5. Build configuration: x64 (recommended) or x86 matching your AutoCAD process.
6. Deploy:
   - Copy GPXVideoTools.dll to a folder.
   - Place libvlc native libraries (libvlc.dll, libvlccore.dll and the /plugins folder) from a matching VLC build next to the DLL, or install VLC and set PATH accordingly.
7. In AutoCAD: NETLOAD -> select GPXVideoTools.dll
8. Use the Ribbon tab "VIDEO TRACKER".

Notes on libVLC:
- libVLCSharp requires native libvlc binaries. For deployment, include the matching libvlc redistribution (x64/x86) and ensure the native binaries are discoverable at runtime (same folder or PATH).
- If you prefer Windows Media Player instead of libVLC, replace VideoViewerForm.cs with a WMP-based implementation.

If you want, I can:
- Generate a ready-to-deploy ZIP that includes a x64 libvlc redistributable (I cannot fetch binaries for you, but I can prepare folder layout instructions).
- Provide a script to NETLOAD automatically on AutoCAD startup.
