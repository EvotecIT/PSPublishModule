# PowerForge module loader

Binary and hybrid modules built by PowerForge ship a small precompiled loader,
`PowerForge.ModuleLoader.V1.dll`, beside the generated `.psm1`:

```text
<Module>/
  <Module>.psd1
  <Module>.psm1            generated bootstrapper (plain script, no reflection)
  <Module>.Libraries.ps1   Windows PowerShell dependency preload list
  Lib/<payload folders>/   module and dependency assemblies
  Loader/Core/PowerForge.ModuleLoader.V1.dll      PowerShell 7 (netcoreapp3.1, the PowerShell 7.0 floor)
  Loader/Desktop/PowerForge.ModuleLoader.V1.dll   Windows PowerShell 5.1 (net472)
```

The bootstrapper loads the build for the running edition with `Add-Type -Path` and calls its public
members. Everything that needs reflection or a CLR callback lives in that compiled assembly, so the
script contains no `Add-Type -TypeDefinition` compilation, no private PowerShell members
(`BindingFlags NonPublic`, `[psobject].Assembly.GetType(...)`), no `Assembly.LoadFrom` and no
monitor locking. Endpoint protection that scans script content through AMSI therefore sees an
ordinary module import instead of the patterns used by attack tooling.

| Feature | Where it runs |
| --- | --- |
| PowerShell 7 isolation | `ModuleAssemblyLoadContext.LoadModuleFromGroup` loads every export assembly of the module into one non-collectible context per physical module path, resolving dependencies from `.deps.json`, `runtimes/<rid>` and the payload folder. Host `System.Management.Automation` always comes from PowerShell. |
| Windows PowerShell resolution | `DesktopAssemblyResolver` handles `AssemblyResolve` only for assemblies in the module's own payload folders, including the packaged facade binding policy. |
| Exports | `Import-Module -Assembly` inside the bootstrapper imports the binary module as a nested module; `Export-ModuleMember -Cmdlet/-Alias` publishes its commands on the module, filtered by the manifest as usual. |
| Type accelerators | `TypeAcceleratorRegistration.RegisterFromLoadContext` (PowerShell 7) and `RegisterFromDirectory` (Windows PowerShell) apply the `AllowList`, `Assembly` and `Enums` modes. `Remove-Module` calls `Unregister()`; an accelerator another PowerForge module still uses stays registered. |
| Shared assemblies | `SharedAssemblyPolicy` keeps `NETSharedAssemblies` process-wide, as described in [the development bootstrapper guide](PSPublishModule.DevelopmentBootstrapper.md#process-shared-dependencies). |

The contract version is part of the assembly name. Modules built by different PowerForge releases
can load in one session: the first loaded `V1` loader serves every module that needs `V1`, and a
breaking change ships as `V2` next to it. PowerForge embeds both builds and writes them during the
module build, so building a module no longer needs the .NET SDK for the loader. Module signing
signs them with the module's other binaries. Packaging always includes the `Loader` folder,
whatever `IncludeAll` lists.

Development bootstrappers (`NETDevelopmentBinaries`) load the same loader from
`<DevelopmentBinariesPath>/PowerForge.ModuleLoader/`. Scripts that called the former generated
types, such as `[<Module>.DevelopmentModuleLoadContext.ModuleAssemblyLoadContext]::LoadModule`, use
`[PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext]::LoadModule($path, $contextName)` instead.
