# Native PowerForge CLI

The standalone package exposes `powerforge`. The launcher handles
`--version`, the module commands below, `project versions`, and `nuget verify` directly.
Other commands go to the self-contained managed executable under `full/`.
Keep the package directory together when installing or moving it.

| Native module command | What it does |
| --- | --- |
| `module find` | Search a feed or a saved repository profile. |
| `module install`, `module save` | Install into a module root or save to a directory. Use `save --as-nupkg` to keep the original archive. |
| `module list` | List installed versions and paths under `--destination`, with optional name and version filters. |
| `module update`, `module uninstall` | Update in the selected root, or remove an exact named version. Use `--plan` to preview. |
| `module pack` | Build a local `.nupkg` from the module folder given in `--path`. |
| `module restore` | Verify and apply a module lock, or resolve a new lock with `--write-lock`. |
| `module repository list\|add\|set\|remove` | Manage the same user and machine profiles as the managed PowerShell cmdlets. |
| `module manifest` | Read static module manifest metadata. |

For example, save a feed once and use it by name:

```powershell
powerforge module repository add --name TeamFeed --source https://feed.example/index.json
powerforge module find --name 'Example.*' --profile TeamFeed
powerforge module update Example.Tools --profile TeamFeed --destination ./modules --plan
powerforge module uninstall Example.Tools --version 1.2.0 --destination ./modules --plan
```

Native uninstall checks the selected root and the absolute roots in
`PSModulePath` for dependent modules. An external process cannot inspect the
loaded modules of a running PowerShell session. Close sessions using the target
module before changing it, or use the managed cmdlet to check the calling session.
Module publication remains available through the existing PowerShell cmdlet.

Use `powerforge module manifest --path ./Example.psd1 --output json` to read
the module name from the filename, the manifest's quoted numeric `ModuleVersion`,
and its optional prerelease label. The command reads the file without loading
the module.

Use `powerforge module find --name 'Pester*' --output json` for direct feed
search. Pass `--source` to use an HTTP(S) feed or local folder and
`--version` with an exact module name for an exact version lookup.
The existing `powerforge find --repo …` uses PowerShell's registered repositories.

`powerforge project versions --project-root . --output json` lists versions
declared in project files, manifests, and supported build scripts. It reads
source files without evaluating MSBuild imports or running PowerShell.
Library consumers can reference `PowerForge.Versioning` on its own.

`powerforge nuget verify` validates the release manifest, package hashes,
and author signatures. It uses the installed .NET SDK's `dotnet nuget verify`
for cryptographic verification. Staging and publishing still use the full CLI.

The PowerForge.Build .NET tool continues to install the powerforge command
with the full managed feature set. The native package can be built on the
same OS as its target with .NET 10 and the Native AOT toolchain.

## Download a preview

Download the Windows x64 or Linux x64 artifact from a successful
[PowerForge Native CLI workflow run](https://github.com/EvotecIT/PSPublishModule/actions/workflows/powerforge-native.yml).
Each workflow artifact contains the same ZIP that passed the archive smoke
test. Unzip the inner archive and keep the launcher and its full directory
together. The workflow can also be run manually for a selected branch or tag.

To prepare a versioned native preview ZIP with the standard PowerForge release
builder, run this on the OS matching the selected runtime. Substitute the
intended preview version and use linux-x64 on Linux.

```powershell
./Build/Build-Project.ps1 -ConfigPath ./Build/release.native-cli.json -ToolsOnly -Target PowerForgeNative -Runtimes win-x64 -ReleaseVersion 3.0.195
```

The preview builder writes the ZIP under Artifacts/Preview/PowerForgeNative.
Publication is a separate release operation.

## Stage the full PowerForge build

Build and smoke-test the native ZIP on each target OS. Place the verified
`PowerForgeNative-<version>-win-x64.zip` and
`PowerForgeNative-<version>-linux-x64.zip` together. Pass that directory to
`Build/Build-Project.ps1 -RunMode Build -NativeArchiveRoot <directory> -StageRoot <directory>`
for the same version as the module and NuGet packages. Both directory
arguments resolve relative to the current PowerShell location. Use `-Plan`
before the ZIPs exist. `-Validate` and the build check the managed CLI version,
the exact source revision when configured, and any configured Windows publisher.
Native archive inputs are supported in
tool-only builds and in unified builds that resolve one module and package
version before building. If the Windows PowerForge
target requires signing, build its archive with `-EnableSigning:$true` and the same
configured certificate. Staging verifies the launcher and managed companion
against the configured publisher.

The staged output includes one standalone archive per runtime. The tool
manifest selects the native archives for Windows and Linux x64 and the
managed standalone archives for other runtimes. The installer keeps the
managed companion for forwarded commands. The `PowerForge.Build` .NET tool
still runs the managed CLI, and `PowerForge` and `PowerForge.Modules` share
the same version track.

## Build from source

Run the following from the repository on Linux x64. Use win-x64 on Windows.

```powershell
dotnet publish ./PowerForge.Cli.Native/PowerForge.Cli.Native.csproj -c Release -r linux-x64 --self-contained -o ./Artifacts/PowerForgeNative/linux-x64
./Build/Test-PowerForgeNative.ps1 -PackageDirectory ./Artifacts/PowerForgeNative/linux-x64
```

Set PowerForgeNativeArchive to an absolute ZIP path outside the publish
directory to produce an archive of the complete package. For an environment
that only installs or restores modules, the root executable can be copied
on its own.

The native build treats trim and AOT warnings as errors. The artifact smoke
test exercises install, frozen restore, rejection of altered packages,
standalone version and manifest commands, and forwarding to the managed CLI.
Library consumers can reference PowerForge.Modules independently of the CLI
and PowerShell SDK.
