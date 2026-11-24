# ReadyToRun module payloads

`New-ConfigurationBuild -NETReadyToRun` adds a precompiled (ReadyToRun) PowerShell 7 payload to a binary
module for each listed runtime identifier. PowerShell 7 on a matching platform loads it instead of the
portable build, so the JIT has much less to compile on the first call of each command. Every other host
keeps today's payloads. The setting is off by default; a module that does not set it builds exactly as
before.

```powershell
New-ConfigurationBuild -NETProjectPath "$PSScriptRoot/../Sources/Example/Example.csproj" `
    -NETFramework @('net472', 'net8.0') `
    -NETReadyToRun `
    -NETReadyToRunRuntimeIdentifier @('win-x64')
```

| Parameter | Default | Meaning |
| --- | --- | --- |
| `-NETReadyToRun` | off | Adds the precompiled payloads. |
| `-NETReadyToRunFramework` | `net10.0` | Framework the payloads are compiled for. The binary project must target it. A platform-qualified framework such as `net10.0-windows` works for Windows-only modules. |
| `-NETReadyToRunRuntimeIdentifier` | `win-x64` | One payload per runtime identifier. Other platforms load the portable build. |
| `-NETReadyToRunScope` | `ModuleAssemblies` | `ModuleAssemblies` precompiles the module project and its project references and keeps NuGet package assemblies as IL. `All` precompiles every assembly. |

The same options are `Build.ReadyToRun` in pipeline JSON and `ReadyToRun`, `ReadyToRunFramework`,
`ReadyToRunRuntimeIdentifiers` and `ReadyToRunScope` in a `BuildLibraries` segment:

```json
{
  "Build": {
    "ReadyToRun": {
      "Enabled": true,
      "Framework": "net10.0",
      "RuntimeIdentifiers": [ "win-x64" ],
      "Scope": "ModuleAssemblies"
    }
  }
}
```

## Layout and loading

Each runtime identifier gets a complete, framework-dependent publish in `Lib/Core-ReadyToRun-<rid>`,
beside the portable `Lib/Core` (and `Lib/Default` for Windows PowerShell). The generated `.psm1` and
`.Libraries.ps1` pick a payload when the module is imported:

1. PowerShell 7 on .NET at least the ReadyToRun framework version, whose
   `RuntimeInformation.RuntimeIdentifier` matches a payload, loads that payload.
2. Any other PowerShell 7 host loads the portable payload for its .NET version, as before.
3. Windows PowerShell 5.1 loads `Lib/Default`, as before. It cannot use ReadyToRun code.

A ReadyToRun payload has to target the runtime that runs it. Code precompiled for `net8.0` is not used by
PowerShell 7.6 on .NET 10, so a `net8.0` payload gives no gain there; that is why the default framework is
`net10.0`. The portable payload stays the fallback for PowerShell 7.4 and 7.5, and for platforms without a
precompiled payload.

The build needs the binary project to target the ReadyToRun framework (add `net10.0` to its
`TargetFrameworks`) and the module to keep a portable PowerShell 7 framework such as `net8.0`. Referenced
libraries are built once for all runtime identifiers; only the module project is published per runtime.
The build downloads the ReadyToRun compiler and the runtime pack for each runtime identifier from your
NuGet sources. Which targets a build machine can compile depends on its platform: a macOS arm64 machine
built `win-x64`, `linux-x64` and `osx-arm64` payloads, while .NET doesn't list Windows targets as
supported from Linux hosts. Build Windows payloads on Windows or macOS.

## Signing

Crossgen rewrites the assemblies it compiles, which removes any signature they had. The payloads are
built before module signing, and module signing covers every file in `Lib`, so the precompiled assemblies
are signed with the rest of the module. With `ModuleAssemblies`, NuGet package assemblies keep their own
signatures. With `All`, they are precompiled as well and your certificate signs them.

## Size

A ReadyToRun assembly holds both IL and native code, typically about twice the size of the IL alone, and
every runtime identifier adds a full payload. Measure the package before choosing runtime identifiers.
No composite image is produced: a composite image recompiles the shared framework into the payload and
roughly doubles it again without making startup faster.
