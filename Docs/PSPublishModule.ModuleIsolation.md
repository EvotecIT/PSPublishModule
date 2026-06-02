# PSPublishModule Module Isolation

`Import-IsolatedModule` imports known dependency-sensitive PowerShell modules through
curated PowerForge isolation profiles.

The feature is intended for PowerShell 7+ sessions where two modules cannot share the
same assembly version in the default load context. Instead of trying to win the
default-context preload race, PowerForge copies the profiled module to a temporary
workspace, patches the configured script module, loads selected binary assemblies
through one shared module-scoped `AssemblyLoadContext`, and imports the generated
wrapper into the current session.

## Built-In Profiles

### ExchangeOnlineManagement

Example:

```powershell
Import-Module Az.Storage
Import-IsolatedModule -Profile ExchangeOnlineManagement
Connect-ExchangeOnline
Get-EXOMailbox -ResultSize 1
```

The profile currently:

- resolves `ExchangeOnlineManagement` from `PSModulePath`,
- copies the module into `%TEMP%\PowerForge\IsolatedModules\ExchangeOnlineManagement`,
- patches `netCore\ExchangeOnlineManagement.psm1`,
- patches the copied `ExchangeOnlineManagement.psd1` manifest so EXO keeps its
  upstream module name, version, and export metadata,
- loads `Microsoft.Exchange.Management.RestApiClient.dll` and
  `Microsoft.Exchange.Management.ExoPowershellGalleryModule.dll` through the same
  `ExchangeOnlineManagement.ALC` context,
- exposes public types from `Microsoft.Exchange.Management.*` and
  `Microsoft.Online.CSE.RestApiPowerShellModule.*` as type accelerators so EXO's
  script-layer type literals keep working.

This avoids the known Az.Storage/ExchangeOnlineManagement OData conflict where
Az.Storage loads `Microsoft.OData.*` 7.6.x in the default context and EXO needs
`Microsoft.OData.*` 7.22.x.

### MicrosoftTeams

`MicrosoftTeams` can also be imported through a built-in isolation profile:

```powershell
Import-IsolatedModule -Profile MicrosoftTeams
Connect-MicrosoftTeams -UseDeviceAuthentication
Get-Team
```

The profile currently:

- resolves `MicrosoftTeams` from `PSModulePath`,
- copies the module into `%TEMP%\PowerForge\IsolatedModules\MicrosoftTeams`,
- generates `MicrosoftTeams.ALC.psm1` and a patched `MicrosoftTeams.ALC.psd1`
  manifest that preserves the upstream export contract,
- loads the Teams connect, team cmdlet, policy administration, and ConfigAPI
  binary surfaces through the same `MicrosoftTeams.ALC` context,
- imports the required Teams submodule manifests after the isolated binary load
  instead of appending the upstream bootstrap script,
- exposes public types from `Microsoft.Teams.*` as type accelerators for
  script/proxy surfaces that reference Teams types directly.

The profile preloads the key Teams binary surfaces through the isolated context
and then imports only the Teams submodule manifests needed for the public proxy
surface. Importing through the generated manifest keeps the normal
`FunctionsToExport` and `CmdletsToExport` filters in place so internal generated
helper commands are not exposed as the public surface.

## Compatibility Strategy

The DLLPickle module set is a useful compatibility map, not a list of modules to
blindly isolate.

- `ExchangeOnlineManagement`: isolate when coexisting with `Az.Storage` or any
  other module that has already loaded an incompatible OData stack.
- `MicrosoftTeams`: isolate when you want its MSAL, IdentityModel, and Teams
  binary stack kept out of the default context.
- `Az.Accounts`: keep in the default module flow. Modern Az.Accounts also owns a
  private Azure SDK load context for parts of its authentication stack, and many
  Az modules expect Az context/token types to remain shared.
- `Microsoft.Graph.Authentication`: keep in the normal Graph module flow unless a
  specific conflict is proven. Graph already uses a private load context for
  Azure SDK dependencies and other Graph modules expect its authentication state.
- `Az.Storage`: keep in the default module flow and isolate EXO instead. Storage
  exports Az-facing types and loads OData 7.6.x in the default context; isolating
  EXO is the smaller and safer fix for the known OData conflict.

## Profile Design

Profiles are maintained in PowerForge so support is explicit and testable. A profile
declares:

- module name and minimum supported version,
- source script path and generated script name,
- whether the generated wrapper appends the source script body,
- number of original bootstrap lines to replace when source script content is used,
- binary assemblies to import through the isolated context,
- namespace prefixes to bridge into PowerShell type resolution,
- stable load-context name.

This keeps the cmdlet surface small while allowing future profiles to add module-specific
patching rules without duplicating loader code.

## Current Limitations

- PowerShell 7+ only. Windows PowerShell cannot use `AssemblyLoadContext`.
- Profiles are curated; arbitrary third-party modules are not automatically isolated.
- Generated module copies are process-local working artifacts and can remain locked until
  the PowerShell process exits.
