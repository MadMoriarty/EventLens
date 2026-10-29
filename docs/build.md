# Build and package

Requirements: Windows x64, .NET 10 SDK, access to NuGet for the first restore.
`tools/bootstrap.ps1` downloads a project-local SDK and verifies Microsoft's
published SHA512. It is optional if a compatible SDK is already installed.

```powershell
./tools/bootstrap.ps1
./tools/build.ps1
```

Build outputs: artifacts/portable/EventLens.exe and artifacts/EventLens-win-x64.zip.
The published executable is self-contained, uses asInvoker and extracts native
libraries to .NET's temporary extraction directory. No Windows installer is used.
Native binaries are not digitally signed. Keep accompanying licenses/notices.

Equivalent SDK commands:

```powershell
dotnet test tests/EventLens.Tests/EventLens.Tests.csproj -c Release
dotnet test tests/EventLens.App.Tests/EventLens.App.Tests.csproj -c Release
dotnet publish src/EventLens.App/EventLens.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o artifacts/portable
```

The application has explicit developer QA modes:
- `--diagnose <private-directory>`: reads real local logs, exports a small System
  EVTX subset, tests reimport/exports and briefly attaches live subscriptions.
  Outputs include real event data; never commit them.
- `--preview <png-path>`: renders clearly labeled synthetic investigation data,
  captures the actual application control tree to PNG, then closes. Normal launch
  always queries Windows. This mode is for documentation, not an alternative backend.

Restore lockfiles are included. The two test projects and app use separate target
frameworks; logic tests can run without Windows APIs, UI/native tests use Windows.
GitHub Actions supplies Windows builds/tests and a separate manual draft-release
workflow. Nothing in the development process publishes externally automatically.

Version 1.3 also supports --benchmark <output.json>, generating 100,000 synthetic
records for disk-cache capture/page replay. Its JSON is safe synthetic benchmark
metadata; it is not a native Event Viewer UI speed comparison. New workflows and
export scopes are documented in investigation-guide.md and acceptance-checklist.md.
