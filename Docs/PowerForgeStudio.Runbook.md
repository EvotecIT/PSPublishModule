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

Activity > Releases includes recent saved release checkpoints from that local journal, scoped to the current workspace. Select a journal row and choose **Open selected release** to inspect its project Releases tab and saved receipts. Failed or waiting checkpoints also appear in Needs attention; completed checkpoints remain in Releases and All activity. Opening history is read-only and does not publish again.

## Local developer usage

Repositories can expose reviewed PowerShell and executable actions through
[`Build/powerforge.tasks.json`](PowerForgeStudio.ProjectTasks.md). Studio shows the
command and arguments before running it and keeps its result tied to the selected working copy.

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
The inventory also reports immediate folders under `_worktrees` that are absent from the scanned Git registrations. They are separate read-only evidence, not cleanup candidates; some may be independent repositories, linked to an owner outside the workspace, or non-Git data. Studio can open them for inspection but does not remove them.
For a Git-linked folder, the inspector and CLI distinguish a present administrative target from a missing one. A missing target means Git cannot use that link as recorded; it does not prove the folder has no unique files or is safe to remove.
If Git registration listing fails or is incomplete for any repository, Storage marks the working-copy totals partial and withholds the other-folder classification. Refresh after resolving the Git problem before drawing conclusions about unregistered folders.

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

For a repository with `Build/project.build.json`, Studio plans and builds through the shared JSON engine. If only `Build-Project.ps1` is present, Studio invokes that script with `-Plan`, `-PlanPath`, and build and publish switches set to false for inspection; Build & Run later invokes it with `-Build` true and publish and version-update switches false. That script must accept these parameters and write a reviewable PowerForge project plan. Studio requests a build-only run, but a custom script can ignore switches or perform other actions; inspect and trust it before using Build & Run.

Studio Releases requires `Build/project.build.json` for project publication so targets and destinations can be reviewed. Script-only repositories can plan and build locally, but cannot publish through Studio until that JSON configuration is added.

For a JSON-backed project, declare the non-secret signing certificate selection in the existing `Build/project.build.json`:

```json
{
  "CertificateThumbprint": "YOUR-CERTIFICATE-THUMBPRINT",
  "CertificateStore": "CurrentUser",
  "TimeStampServer": "http://timestamp.digicert.com"
}
```

Studio checks that the certificate is available when you prepare a release. `CertificateStore` accepts `CurrentUser` or `LocalMachine`. For project build artifacts, a certificate named in JSON takes precedence over `RELEASE_OPS_STUDIO_SIGN_THUMBPRINT`; other build adapters in a mixed release use the host setting. The preparation status shows the prerequisite for each adapter, and signing stays disabled until every required certificate is available. A changed project JSON invalidates the captured build checkpoint, so rebuild before signing. `SignPackages=false` in project JSON controls signing during **Build & Run**; the separate **Releases > Sign artifacts** step signs the captured package after preparation. Studio shows missing prerequisites without revealing certificate or credential values. After making a certificate available, choose **Prepare built artifacts** again; after editing JSON, rebuild first.

Publication remains a separate reviewed action. Configure the intended JSON destinations, inspect them in Releases, explicitly approve the displayed targets and enable `RELEASE_OPS_STUDIO_ENABLE_PUBLISH=true` only for the Studio process that should publish. A local NuGet feed can be used for a disposable end-to-end check without public credentials.

The Release activity list shows the newest update first so the current result stays visible; saved history retains the full chronological event record and receipts.

A published app still resolves its machine-local workspace and release state under the platform application-data folder. Build, signing and publication availability then depends on the PowerForge and toolchain evidence shown by Studio Connections.

## Quick command list

```powershell
.\Build\Build-PowerForgeStudio.ps1
.\Build\Run-PowerForgeStudio.ps1 -Workspace C:\Support\GitHub
.\Build\Publish-PowerForgeStudio.ps1 -Runtime win-x64 -Mode Both
```
