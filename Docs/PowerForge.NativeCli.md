# Native PowerForge CLI

The standalone package exposes `powerforge`. The launcher handles
`--version`, `module install`, `module restore`, and `module manifest` directly.
Other commands go to the self-contained managed executable under `full/`.
Keep the package directory together when installing or moving it.

Use `powerforge module manifest --path ./Example.psd1 --output json` to read
the module name from the filename, the manifest's quoted numeric `ModuleVersion`,
and its optional prerelease label. The command reads the file without loading
the module.

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
`Build/Build-Project.ps1 -NativeArchiveRoot <directory> -StageRoot <directory>`
for the same version as the module and NuGet packages. Signed release builds
sign the Windows launcher and managed companion before creating their ZIP.

The unified release includes one standalone archive per runtime. Its tool
manifest directs the installer to the native archive on Windows and Linux
x64. Other runtimes continue to use their managed standalone archives.
The installer preserves the managed companion for forwarded commands.
The `PowerForge.Build` .NET tool stays on the managed CLI, and `PowerForge`
and `PowerForge.Modules` ship on the same NuGet release track.

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
