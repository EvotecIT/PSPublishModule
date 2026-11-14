# PowerForge module loader

Binary and hybrid modules built by PowerForge ship a small precompiled loader,
`PowerForge.ModuleLoader.V2.dll`, beside the generated `.psm1`:

```text
<Module>/
  <Module>.psd1
  <Module>.psm1            generated bootstrapper (plain script, no reflection)
  <Module>.Libraries.ps1   Windows PowerShell dependency preload list
  Lib/<payload folders>/   module and dependency assemblies
  Loader/Core/PowerForge.ModuleLoader.V2.dll      PowerShell 7 (netcoreapp3.1, the PowerShell 7.0 floor)
  Loader/Desktop/PowerForge.ModuleLoader.V2.dll   Windows PowerShell 5.1 (net472)
```

The bootstrapper loads the build for the running edition with `Add-Type -Path` and calls its public
members. Everything that needs reflection or a CLR callback lives in that compiled assembly, so the
script contains no `Add-Type -TypeDefinition` compilation, no private PowerShell members
(`BindingFlags NonPublic`, `[psobject].Assembly.GetType(...)`), no `Assembly.LoadFrom` and no
monitor locking. Endpoint protection that scans script content through AMSI therefore sees an
ordinary module import instead of the patterns used by attack tooling.

The build also resolves which assemblies each host loads. The `.psm1` lists the export assemblies and
`<Module>.Libraries.ps1` the Windows PowerShell dependencies, per edition and, when the payload has
`Core-netX.Y` folders or a Core baseline, per .NET version band, so an import does not search the `Lib`
folder. A configured export assembly that matches more than one nested `Lib` file fails the build; use a
path-qualified `ExportAssemblies` entry. The loader runs in its own scope, so the module keeps only the
`$PowerForge…` state that `Remove-Module` and the Windows PowerShell failure handling need. When the caller's
`$ErrorActionPreference` is not `Stop`, an export assembly that fails to load is reported as a warning and
the module's other export assemblies still load.

| Feature | Where it runs |
| --- | --- |
| PowerShell 7 isolation | `ModuleAssemblyLoadContext.LoadModuleFromGroup` loads every export assembly of the module into one non-collectible context per physical module path, resolving dependencies from `.deps.json`, `runtimes/<rid>` and the payload folder. Host `System.Management.Automation` always comes from PowerShell. |
| Windows PowerShell resolution | `DesktopAssemblyResolver` registers before the dependency preload and prepends its handler ahead of earlier version-blind handlers. It answers only requests from the module's payload assemblies, or an exact reference declared by those assemblies when the requester is unknown. Ordinary resolution requires the same name, culture and public key token and a version at least as new as requested, including when the CLR reuses a loaded copy. The packaged facade binding policy remains separate. |
| Exports | `Import-Module -Assembly` inside the bootstrapper imports the binary module as a nested module. Its aliases are defined on the module, because `Import-Module` leaves them in the loader's scope, and `Export-ModuleMember -Cmdlet/-Alias` publishes its commands on the module, filtered by the manifest as usual. |
| Type accelerators | `TypeAcceleratorRegistration.RegisterFromLoadContext` (PowerShell 7) and `RegisterFromDirectory` (Windows PowerShell) apply the `AllowList`, `Assembly` and `Enums` modes. `Remove-Module` calls `Unregister()`; an accelerator another PowerForge module still uses stays registered. |
| Shared assemblies | `SharedAssemblyPolicy` keeps `NETSharedAssemblies` process-wide, as described in [the development bootstrapper guide](PSPublishModule.DevelopmentBootstrapper.md#process-shared-dependencies). |

Prepending on .NET Framework uses its private `_AssemblyResolve` delegate under the same AppDomain
lock as the event accessors. The loader checks the field type and falls back to normal event
subscription if reflection is unavailable. That fallback preserves module import setup, but cannot
take priority over an earlier version-blind resolver.

## Version conflicts

| Situation | PowerShell 7 | Windows PowerShell 5.1 |
| --- | --- | --- |
| Two modules ship different versions of one dependency | Each module loads its own copy in its own context. | Assemblies without a strong name bind by simple name, so the first loaded copy serves every module. Before loading, the bootstrapper compares its payload with what is loaded: the same version, or a newer patch release of the same major.minor, is reused with one warning per module that loaded them; any other version stops the import with one message explaining what to do. Minor releases can change public API (OfficeIMO 3.6.0 renamed a public enum 3.5.1 used), and an older patch can lack members the module calls. If a reused copy still fails to load, the import reports the same conflict instead of a raw type-load error. Strong-named assemblies load side by side and are not checked. |
| A second version of the same module | Each version loads in its own context. Where PowerShell would import the same cmdlet names twice into one scope it refuses ("A cmdlet named … already exists"), `-Force` included, because PowerShell does not tell the module that `-Force` was used. `Remove-Module` and import again to switch versions. | The module's own assembly follows the dependency rule above: a newer patch already loaded is reused with a warning, and its types back the module's accelerators. Any other version asks for a new session, because assemblies stay loaded after `Remove-Module`. |
| A module ships another module's library (for example `ConsoleMonster.dll`) | Both copies load in separate contexts. Objects from one copy are not instances of the other copy's types; declare the library in `NETSharedAssemblies` when modules must exchange them. | The dependency rule above applies. |
| Two modules expose the same type accelerator name | The first registration keeps the name. The next module reports it once and takes the name over when the first module is removed. PowerShell caches a type name once a script resolves it, so a name already used in the session keeps its first type. | Same. |

The contract version is part of the assembly name. The corrected Desktop resolver ships as `V2`,
so an older `V1` loader already loaded by another module cannot replace it. Future public contract
changes, or fixes that must coexist with an older loader, require a new identity. Rebuild modules
to give them the corrected loader; importing a newer builder does not rewrite installed modules.
The type accelerator registrar deliberately keeps its `V1` type name in both assemblies. Generated
scripts select the first loaded registrar, keeping accelerator ownership and handover shared with
modules built before V2.
PowerForge embeds both builds and writes them during the
module build, so building a module no longer needs the .NET SDK for the loader. Module signing
signs them with the module's other binaries. Packaging always includes the `Loader` folder,
whatever `IncludeAll` lists.

Development bootstrappers (`NETDevelopmentBinaries`) load the same loader from
`<DevelopmentBinariesPath>/PowerForge.ModuleLoader/`. Scripts that called the former generated
types, such as `[<Module>.DevelopmentModuleLoadContext.ModuleAssemblyLoadContext]::LoadModule`, use
`[PowerForge.ModuleLoader.V2.ModuleAssemblyLoadContext]::LoadModule($path, $contextName)` instead.

## PSPublishModule documentation dependencies

PSPublishModule packages its OfficeIMO and HtmlForgeX documentation dependencies but does not preload
them into the shared Windows PowerShell session. Their rendering APIs can differ from those used by
another module, so upgrading an unsigned copy is not always compatible. Formatted
`Get-ModuleDocumentation` and `Show-ModuleDocumentation` rendering runs in a short-lived Windows
PowerShell process using the packaged copies. Document selection, repository access, command-help
collection, and opening the resulting HTML stay in the calling session; only finalized render data
crosses the process boundary. Raw Markdown stays in the calling session too. Stopping the pipeline
stops its render process, and temporary render files are removed afterward. PowerShell 7 uses its
existing assembly load context and renders directly.
