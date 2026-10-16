# Standalone Windows release

Build the single-file Windows x64 release:

```powershell
dotnet publish ProcessVision.csproj -p:PublishProfile=Standalone
```

Output: `artifacts/standalone-win-x64/ProcessVision.exe`

Distribute this EXE by attaching it to a GitHub Release. The .NET desktop runtime is included; users do not need to install .NET or receive DLL files alongside the EXE. This build targets Windows x64 (including Windows 11 x64).

The executable extracts bundled native runtime libraries locally when needed. Process history is stored separately in `%LOCALAPPDATA%\ProcessVision\history.json` during use; no existing history is bundled in the release.

Dubai is used when installed; otherwise the interface falls back to Segoe UI. Font files are not bundled.

Standard monitoring works without elevation. The optional **Enable full monitoring...** button requests administrator access for WMI process-start events. Polling may miss very short-lived processes. Publisher values come from executable company metadata, not signature verification.

The generated application is not code-signed. Windows may display a warning when a downloaded executable has no established reputation.
