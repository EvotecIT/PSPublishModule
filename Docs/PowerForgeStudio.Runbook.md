# PowerForge Studio build and run guide

This guide covers the supported developer and compiled-app paths for PowerForge Studio. The desktop product is the Avalonia application over the portable shared core.

## Product entry points

- Desktop host: `PowerForgeStudio.Avalonia`
- Optional headless companion: `PowerForgeStudio.Cli`
- Workspace root: `$env:EVOTEC_GITHUB_ROOT` when set, otherwise `C:\Support\GitHub` on Windows or `~/Documents/GitHub` on macOS/Linux

Run the commands below from the repository root.

## Prerequisites

- .NET 10 SDK
- PowerShell 7 for the cross-platform scripts

The desktop app stores machine-local state under `%LOCALAPPDATA%\PowerForgeStudio` on Windows and the platform local-application-data folder elsewhere. Important files include:

- `state\workspace-roots.json` for recent and active workspace roots
- `workspaces\...\releaseops.db` for workspace state
- `release-history.db` for durable release sessions and receipts
- `file-recovery\...` for Studio-managed file recovery

## Local developer usage

### Validate Studio

```powershell
.\Build\Build-PowerForgeStudio.ps1 -Configuration Release
```

The default workflow builds Avalonia and runs its UI tests plus the shared Studio tests. Useful variants:

```powershell
.\Build\Build-PowerForgeStudio.ps1 -Configuration Debug
.\Build\Build-PowerForgeStudio.ps1 -SkipTests
.\Build\Build-PowerForgeStudio.ps1 -NoRestore
.\Build\Build-PowerForgeStudio.ps1 -IncludeCli
.\Build\Build-PowerForgeStudio.ps1 -DesktopOnlyTests
```

The default path runs the complete shared Studio suite. `-DesktopOnlyTests` is an explicit reduced validation pass for desktop-host work: it excludes the existing Apple exact-source matrix and its environment-dependent release-station projection. Use the default path for repository-wide validation.

### Run Studio from source

```powershell
.\Build\Run-PowerForgeStudio.ps1
```

The command runs the Avalonia host in Debug and builds it when needed. Studio restores its last workspace root. To select a root explicitly:

```powershell
.\Build\Run-PowerForgeStudio.ps1 -Workspace C:\Support\GitHub
```

Other useful variants:

```powershell
.\Build\Run-PowerForgeStudio.ps1 -Configuration Release
.\Build\Run-PowerForgeStudio.ps1 -Configuration Release -NoBuild -NoRestore
```

The direct Avalonia command is:

```powershell
dotnet run --project .\PowerForgeStudio.Avalonia\PowerForgeStudio.Avalonia.csproj -c Debug --framework net10.0 -- --workspace C:\Support\GitHub
```

## Compiled usage

### Publish a framework-dependent build

Use this when the target machine already has a compatible .NET 10 runtime for the selected RID and the platform GUI prerequisites required by Avalonia.

```powershell
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode FrameworkDependent
```

Output:

- `.\Artifacts\PowerForgeStudio\win-x64\framework-dependent\PowerForgeStudio.exe`

### Publish a self-contained build

Use this when the target machine should not require a separate .NET runtime install.

```powershell
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode SelfContained
```

Output:

- `.\Artifacts\PowerForgeStudio\win-x64\self-contained\PowerForgeStudio.exe`

### Publish both outputs

```powershell
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode Both
```

### Publish a single-file self-contained build

```powershell
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode SelfContained -SingleFile
```

The multi-file self-contained output remains the baseline for runtime validation.

Avalonia also accepts `linux-x64`, `osx-x64`, and `osx-arm64` runtime identifiers. Windows is the first supported and validated product target. Other runtime outputs still require native validation and the target platform's Avalonia prerequisites before distribution.

## Headless CLI usage

The CLI uses the same shared domain and orchestration owners:

```powershell
dotnet run --project .\PowerForgeStudio.Cli\PowerForgeStudio.Cli.csproj -- snapshot --root C:\Support\GitHub --json
dotnet run --project .\PowerForgeStudio.Cli\PowerForgeStudio.Cli.csproj -- inbox --root C:\Support\GitHub
dotnet run --project .\PowerForgeStudio.Cli\PowerForgeStudio.Cli.csproj -- storage --root C:\Support\GitHub --top 20 --json
```

`storage` measures primary and registered working copies locally. It sends scan progress to stderr, then writes a bounded inventory to stdout. Its sizes are logical bytes, not verified reclaimable space; review any removal candidate in Studio before cleanup.

## Day-to-day workflow

Local iteration:

```powershell
.\Build\Build-PowerForgeStudio.ps1 -Configuration Debug
.\Build\Run-PowerForgeStudio.ps1 -Configuration Debug
```

Pre-share validation:

```powershell
.\Build\Build-PowerForgeStudio.ps1 -Configuration Release
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode Both
```

## Engine resolution

Studio prefers the local PSPublishModule repository manifest when it runs from a checkout. This keeps unpublished Studio and pipeline changes on the same shared PowerForge implementation.

A published app still resolves its machine-local workspace and release state under the platform application-data folder. Build, signing and publication availability then depends on the PowerForge and toolchain evidence shown by Studio Connections.

## Quick command list

```powershell
.\Build\Build-PowerForgeStudio.ps1
.\Build\Run-PowerForgeStudio.ps1 -Workspace C:\Support\GitHub
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode Both
```
