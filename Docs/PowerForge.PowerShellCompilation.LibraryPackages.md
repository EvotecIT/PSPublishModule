# Package a compiled PowerShell library for .NET

`powerforge powershell project pack --format nuget` creates a local NuGet package from one tested Strict CLR library. It uses the existing library packer to rebuild the emitted project independently and verify the assembly, portable PDB, public ABI, and provider dependencies before replacing the destination package.

This workflow is implemented on `feature/powershell-compiler`. It is not a public package release. Build the development CLI as described in the [development guide](PowerForge.PowerShellCompilation.Development.md); keep its absolute path in `$powerforge`. The examples use PowerShell 7 and the .NET 10 SDK.

## Create and qualify the library

In your project directory, save `Functions.psm1`:

```powershell
function Add-CompiledValues {
    param([int] $Left, [int] $Right)
    $Left + $Right
}
```

Create a project that retains its generated source:

```powershell
dotnet $powerforge powershell project init ./Functions.psm1 --name Sample --kind library --mode Strict --framework net10.0 --emit-source
```

Add a top-level `nuGet` object to `powerforge.psproject.json`. Choose metadata that describes your own source and its license; the values below describe the example:

```json
"nuGet": {
  "packageId": "Sample.CompiledLibrary",
  "packageVersion": "1.0.0",
  "authors": "Sample authors",
  "description": "Integer addition compiled from PowerShell.",
  "licenseExpression": "MIT"
}
```

IDs use the existing NuGet identity rules; versions must be stable three-part `x.y.z` values. Optional `repositoryUrl` and `repositoryCommit` record source provenance. Set metadata before locking the project: the restore environment records the project manifest identity.

```powershell
dotnet $powerforge powershell project lock ./powerforge.psproject.json
dotnet $powerforge powershell project restore ./powerforge.psproject.json
dotnet $powerforge powershell project build ./powerforge.psproject.json
dotnet $powerforge powershell project test ./powerforge.psproject.json
$packed = dotnet $powerforge powershell project pack ./powerforge.psproject.json --format nuget --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $packed.success) { throw 'Library packaging failed.' }
$package = $packed.result.targets[0].path
$feed = Split-Path $package
```

The result includes the package path, package SHA-256, and verified public ABI SHA-256. Output is under `.powerforge/packages/nuget/<target>/<id>.<version>.nupkg`. If the manifest contains multiple targets, select one with `--target <name>`; one call packages one framework. Do not publish separate framework variants under the same ID/version as if they formed one multi-target package.

`project test` checks library metadata and inventory. Run an ordinary consumer to verify the business behavior you intend to distribute. Packaging rejects stale source, targets, providers, restore evidence, test evidence, and changed artifact files. After an intentional source or manifest change, repeat lock → restore → build → test.

## Use an ordinary .NET consumer

Read the generated namespace and type name from the packaged ABI, then build a console consumer through the local feed:

```powershell
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry('powerforge/public-abi.json').Open())
    try { $abi = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
} finally { $archive.Dispose() }

dotnet new console --framework net10.0 --output ./Consumer
dotnet add ./Consumer/Consumer.csproj package Sample.CompiledLibrary --version 1.0.0 --source $feed
$methodType = $abi.namespaceName + '.' + $abi.typeName
@"
using Methods = global::$methodType;
Console.WriteLine(Methods.Add_CompiledValues(19, 23));
"@ | Set-Content ./Consumer/Program.cs
dotnet run --project ./Consumer/Consumer.csproj --no-restore
```

The example prints `42`. The generated public API and XML documentation belong to the library; consumers need no PowerForge or PowerShell reference. Reviewed provider runtime assemblies and their managed dependencies are included when the library uses them.

Windows `net10.0` and `net472` library packages have ordinary-consumer coverage. To create a .NET Framework variant, initialize a separate project with `--framework net472` and consume it from a `net472` application on Windows. The consumer needs the .NET Framework developer/reference assemblies to build and the supported runtime to execute. This does not qualify another OS, architecture, or NativeAOT.

## Offline rebuilds and upgrades

After successful acquisition, `project restore --offline` verifies the isolated project environment without using network sources. Project builds and NuGet packaging use that environment. Emitted source retains the exact `packages.lock.json`, enables locked restore, and clears ambient NuGet sources. Moving the emitted source to another machine requires supplying the matching SDK and package cache; the NuGet package itself remains usable by ordinary consumers.

To compare an upgrade against an earlier generated API, save the previous package's `powerforge/public-abi.json` inside the project and set `nuGet.compatibilityBaseline` to its relative path before locking the new version. The existing ABI checker rejects incompatible generated signatures and lifetime contracts. It does not promise binary drop-in replacement, unchanged package/assembly identity, or replacement of an already-loaded assembly; restore and rebuild the consumer for each selected upgrade.

## Delivery boundaries

`project pack` without a format, or with `--format zip`, retains qualified ZIP delivery. `project install` uses that ZIP. NuGet installation and upgrades use ordinary .NET restore. These commands create local artifacts and do not push a package to any feed.

NuGet packing requires Strict library mode, emitted source, and explicit package metadata. Signed compiler inputs are not supported by the independent-rebuild packer. Cancellation before publication or a failed integrity/ABI/rebuild check preserves the prior package; a completed publication returns success. Ctrl+C cancels the CLI rebuild and its owned process tree. Existing M29 work covers signed distribution, public feeds, long-path qualification, clean-machine deployment, and additional platforms.
