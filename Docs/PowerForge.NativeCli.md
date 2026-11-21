# Native PowerForge CLI

The standalone package exposes one command, powerforge. The launcher runs
module install and module restore directly. Other commands go to the
self-contained managed executable under full/. The managed executable
also supports the module commands through the same implementation. Keep
the package directory together when installing or moving it.

The PowerForge.Build .NET tool continues to install the powerforge command
with the full managed feature set. The native package can be built on the
same OS as its target with .NET 10 and the Native AOT toolchain.

Run the following from the repository on Linux x64. Use win-x64 on Windows.

```powershell
dotnet publish ./PowerForge.Cli.Native/PowerForge.Cli.Native.csproj -c Release -r linux-x64 --self-contained -o ./Artifacts/PowerForgeNative/linux-x64
./Build/Test-PowerForgeNative.ps1 -PackageDirectory ./Artifacts/PowerForgeNative/linux-x64
```

Set PowerForgeNativeArchive to an absolute ZIP path outside the publish
directory to produce an archive of the complete package. For an environment
that only installs or restores modules, the root executable can be copied
on its own.

The native build treats trim and AOT warnings as errors. The artifact
smoke test exercises install, frozen restore, rejection of altered packages,
and forwarding to the managed CLI. Library consumers can reference
PowerForge.Modules independently of the CLI and PowerShell SDK.
