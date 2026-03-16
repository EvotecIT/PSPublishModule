# PowerForge Studio Build And Run Guide

This guide covers the practical commands for running `PowerForgeStudio.Wpf` from a local checkout and for producing a compiled build you can launch without opening the repo in an IDE.

## What this guide targets

- GUI host: `PowerForgeStudio.Wpf`
- Optional headless companion: `PowerForgeStudio.Cli`
- Repo root assumed in examples: `C:\Support\GitHub\PSPublishModule`
- Studio worktree example: `C:\Support\GitHub\PSPublishModule-codex-studio`

All commands below work from the repo root.

## Prerequisites

- Windows machine for the WPF app
- .NET 10 SDK installed
- PowerShell 7 or Windows PowerShell

The WPF app stores its local state under:

- `%LOCALAPPDATA%\PowerForgeStudio\state\workspace-roots.json`
- `%LOCALAPPDATA%\PowerForgeStudio\workspaces\...`
- `%LOCALAPPDATA%\PowerForgeStudio\releaseops.db`

Per-workspace databases are created automatically by `PowerForgeStudioHostPaths`.

## Local developer usage

### 1. Validate the Studio build

Run the Studio-specific build script:

```powershell
.\Build\Build-PowerForgeStudio.ps1 -Configuration Release
```

This does:

1. `dotnet build` for `PowerForgeStudio.Wpf`
2. `dotnet test` for `PowerForgeStudio.Wpf.Tests`
3. `dotnet test` for `PowerForgeStudio.Tests`

Useful variants:

```powershell
.\Build\Build-PowerForgeStudio.ps1 -Configuration Debug
.\Build\Build-PowerForgeStudio.ps1 -SkipTests
.\Build\Build-PowerForgeStudio.ps1 -NoRestore
.\Build\Build-PowerForgeStudio.ps1 -IncludeCli
```

### 2. Run the WPF Studio from source

```powershell
.\Build\Run-PowerForgeStudio.ps1
```

Default behavior:

- runs `PowerForgeStudio.Wpf`
- uses `Debug`
- builds before launch
- paints the shell first, then refreshes the workspace in the background

On a large root such as `C:\Support\GitHub`, the first refresh can still take time because Studio scans repositories, enriches plan previews, and probes GitHub signals. During that phase the window should stay responsive and the status line should update instead of showing a blank white screen.

Useful variants:

```powershell
.\Build\Run-PowerForgeStudio.ps1 -Configuration Release
.\Build\Run-PowerForgeStudio.ps1 -Configuration Release -NoBuild
.\Build\Run-PowerForgeStudio.ps1 -Configuration Release -NoBuild -NoRestore
```

The direct `dotnet` form is:

```powershell
dotnet run --project .\PowerForgeStudio.Wpf\PowerForgeStudio.Wpf.csproj -c Debug --framework net10.0-windows
```

## Compiled usage

### 1. Publish a framework-dependent build

Use this when the target machine already has the matching .NET desktop runtime installed.

```powershell
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode FrameworkDependent
```

Output:

- `.\Artifacts\PowerForgeStudio\win-x64\framework-dependent\PowerForgeStudio.Wpf.exe`

### 2. Publish a self-contained build

Use this when you want the app to run on a machine without a separate .NET runtime install.

```powershell
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode SelfContained
```

Output:

- `.\Artifacts\PowerForgeStudio\win-x64\self-contained\PowerForgeStudio.Wpf.exe`

### 3. Publish both outputs at once

```powershell
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode Both
```

### 4. Optional single-file self-contained publish

```powershell
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode SelfContained -SingleFile
```

This keeps the publish simpler to move around, but the default multi-file self-contained output is the safer baseline when you are validating runtime behavior.

## Optional headless CLI usage

If you want a non-GUI snapshot or queue check while developing Studio, the CLI project is already in the solution:

```powershell
dotnet run --project .\PowerForgeStudio.Cli\PowerForgeStudio.Cli.csproj -- snapshot --root C:\Support\GitHub --json
dotnet run --project .\PowerForgeStudio.Cli\PowerForgeStudio.Cli.csproj -- inbox --root C:\Support\GitHub
```

This is optional. The main local and compiled usage path for Studio remains the WPF app.

## Recommended day-to-day workflow

### Local iteration

```powershell
.\Build\Build-PowerForgeStudio.ps1 -Configuration Debug
.\Build\Run-PowerForgeStudio.ps1 -Configuration Debug
```

### Pre-share / pre-demo validation

```powershell
.\Build\Build-PowerForgeStudio.ps1 -Configuration Release
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode Both
```

## Notes about engine resolution

When you run Studio from a repo checkout, it prefers the local `PSPublishModule` repo manifest when available. That is the safest path while iterating on unpublished Studio and pipeline changes together.

If you publish the app and run it outside the repo, Studio will still resolve its local state and workspace catalog under `%LOCALAPPDATA%\PowerForgeStudio`, but engine selection depends on what is available on that machine.

## Quick command list

```powershell
.\Build\Build-PowerForgeStudio.ps1
.\Build\Run-PowerForgeStudio.ps1
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode Both
```
