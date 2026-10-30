# PSPublishModule Module Dependency Story

PSPublishModule has two different dependency stories. They solve different
deployment problems and should not be blurred together.

## Dependency Types

`New-ConfigurationModule` controls how a dependency participates in the build.

| Type | Written to PSD1 | Installed by `InstallMissingModules` | Bundled by `New-ConfigurationArtefact -AddRequiredModules` | Embedded under `Internals\Modules` |
| --- | --- | --- | --- | --- |
| `RequiredModule` | `RequiredModules` | Yes | Yes | No |
| `ExternalModule` | `PrivateData.PSData.ExternalModuleDependencies` | Yes | No | No |
| `ApprovedModule` | No | No | No | No |
| `EmbeddedModule` | No | Yes | No | Yes |

`RequiredModule` is the normal PowerShell contract. It tells PowerShell and
repositories that the module has install/import dependencies. Use it when
consumers should install the dependency through the gallery or private feed.

`ExternalModule` is an install/check contract without a manifest
`RequiredModules` entry. Use it when the dependency must exist in the estate but
the module package should not force PowerShell dependency resolution.

`ApprovedModule` is a build-time helper contract for merge and missing-command
workflows. It is not a runtime dependency declaration.

When a dependency is both required and approved, `-MergeFunctionsFromApprovedModules`
removes its runtime requirement once complete analysis proves that the merged
output satisfies it. This includes helpers already present in the consumer or
another approved donor; a donor does not need to contribute another copy of a
function to become unnecessary. Artefacts with `-AddRequiredModules` copy the
remaining runtime dependencies after that pruning.

Module-qualified calls, module-management lookups, module-owned types and unresolved dynamic invocation can
still require the original module. Approval permits merging; it does not make an
unresolved runtime reference safe to remove. Donors exporting variables, aliases,
binary commands, providers, type data or formatting data retain their runtime
requirement. An absent donor is retained when its selected runtime surface cannot
be inspected. Builds with merging disabled keep
their configured required modules.

For launcher/configuration ZIPs with the main module and remaining dependencies
under `Modules`, use the native [delivery bundle layout](PSPublishModule.ModuleDelivery.md).

`EmbeddedModule` is the private-runtime contract. Use it when the module should
carry selected dependency payloads without adding those modules to PSD1
`RequiredModules`.

## Selected Versions And Import Order

Use `-RequiredVersion` when a dependency must use one exact version:

```powershell
New-ConfigurationModule -Type RequiredModule -Name 'Runtime.Tools' -RequiredVersion '1.0.0'
New-ConfigurationImportModule -ImportSelf -ImportRequiredModules -PreferBinaryConflictOrder
```

Binary conflict analysis inspects the selected version, even if a newer version
is installed. `-PreferBinaryConflictOrder` imports dependencies before modules
that require them, then applies the binary preference among eligible modules.
The same ordering is used for import advice and the generated manifest, including
dependencies reached through intermediate modules and local nested modules.
Module selection uses PowerShell's version and GUID rules. Planning reports
indirect dependencies that are not installed yet; the build refreshes the order
after dependency preparation makes their metadata available. Cyclic declarations
fail with an explicit dependency-cycle error. Binary scoring for selected module
paths stays within those paths instead of scanning unrelated default stores.

Approved helper analysis uses the selected runtime context too. A donor whose
minimum requirement accepts `Runtime.Tools` 1.0.0 uses that selected module
instead of autoloading an installed 2.0.0. If the donor requires 2.0.0, analysis
reports the incompatible selected and imported versions and keeps the donor
from being treated as safely inlined. Align those requirements before building.

Import ordering cannot make incompatible assemblies coexist. Use `-Verbose`
when investigating missing-command failures: resolution messages include the
selected module and its import error, rather than only reporting that a command
has no module source.

## Installed Modules In Cloud-Synced Folders

Artefact builds can copy locally available installed dependencies from Windows
Cloud Files folders, including OneDrive-backed `Documents/PowerShell/Modules`.
No special build option is required. The input checks recognize the documented
Windows cloud reparse tags; symbolic links, junctions and unknown reparse tags
remain rejected, including links nested inside a cloud-backed input folder.

The cloud provider must make each selected file available for normal reads.
An offline or unavailable file still causes the copy to fail. Dependencies are
copied into ordinary local package files; finalized layouts, archives and
signing evidence retain their strict reparse-point checks.

## Optional Binary References

Binary dependency preflight checks the assemblies delivered with the module.
Some libraries reference alternative providers and load only the selected one.
Declare those optional references by exact referring and dependency DLL filenames:

```powershell
New-ConfigurationImportModule -ImportSelf -OptionalBinaryDependencies @{
    'itext.bouncy-castle-connector.dll' = @('itext.bouncy-castle-fips-adapter.dll')
}
```

This example applies when the module delivers iText's standard Bouncy Castle
adapter. It exempts only the connector's reference to the alternative FIPS
adapter. It does not install an adapter or change which provider iText selects.
Every other missing reference remains an error, including references to the
same DLL from another assembly. Assemblies explicitly required by the module
manifest remain required. Paths and wildcards are rejected.

Use this only for references whose absence is supported by the library and
validated by the module's intended operations. Core/Desktop import checks still
run; a successful import alone does not exercise every lazily loaded provider.

## PSD1 Rebuild Contract

The build definition is the source of truth. Existing PSD1 files can provide
baseline metadata, but dependency declarations come from `Build-Module` /
`Invoke-ModuleBuild` configuration.

That means:

- removing a `RequiredModule` declaration removes it from generated PSD1 output
- stale `RequiredModules` from the source PSD1 are not kept alive by accident
- `ExternalModule` entries are written only to `PSData.ExternalModuleDependencies`
- `EmbeddedModule` entries are not written to PSD1 dependency fields

This keeps publish/install behavior predictable. A dependency appears in a
gallery requirement only when the current build configuration says it should.

## Missing Commands

Missing-command validation fails by default. If a script calls
`Connect-MgGraph`, `Get-ADUser`, or another command that is not part of the
module being built, PowerForge expects that command to be explained by the build
configuration.

Valid explanations are:

- declare the owning module as `RequiredModule`
- declare it as `ExternalModule`
- declare it as `EmbeddedModule`
- approve/merge helper code through `ApprovedModule` when that is the actual
  intent
- intentionally ignore it with `New-ConfigurationModuleSkip`

`New-ConfigurationModuleSkip` is the escape hatch. It tells the build that the
module or command is intentionally not part of the generated dependency
contract. Skipped modules are not added back to PSD1 requirements.

## Required Modules And Private Galleries

`RequiredModule` is the right model for standard package management.

Example:

```powershell
Invoke-ModuleBuild -ModuleName 'Company.Tools' -Settings {
    New-ConfigurationModule -Type RequiredModule -Name 'Microsoft.Graph.Authentication' -RequiredVersion '2.25.0'
    New-ConfigurationBuild -Enable -InstallMissingModules
    New-ConfigurationPublish -Type PowerShellGallery -ApiKey $env:PSGALLERY_API_KEY -Enabled
}
```

When publishing to a private feed that does not already contain the required
dependency packages, opt in to dependency mirroring:

```powershell
New-ConfigurationPublish `
    -JFrogBaseUri 'https://company.jfrog.io/artifactory' `
    -JFrogRepository 'powershell-virtual' `
    -RepositoryName 'JFrogPS' `
    -Tool PSResourceGet `
    -RepositoryCredentialUserName 'name@company.com' `
    -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' `
    -PublishRequiredModules `
    -RequiredModuleSourceRepository PSGallery `
    -Enabled
```

That flow promotes missing `RequiredModules` into the private target feed before
publishing the main module. It intentionally skips `ExternalModule` and
`EmbeddedModule` entries because those are not gallery requirement contracts.

## Embedded Modules And Private Runtimes

`EmbeddedModule` is for dependency-sensitive estates where the operator wants
the exact dependency payloads next to the module and does not want PowerShell to
resolve them from every folder in `PSModulePath`.

Build configuration:

```powershell
Invoke-ModuleBuild -ModuleName 'Company.Tools' -Settings {
    New-ConfigurationModule -Type EmbeddedModule -Name 'Microsoft.Graph.Authentication' -RequiredVersion '2.25.0'
    New-ConfigurationBuild -Enable -InstallMissingModules
}
```

During build, PowerForge copies the selected module payloads under:

```text
Internals\Modules\<ModuleName>\<Version>
Internals\Modules\module-dependencies.json
```

The JSON file is a private-runtime receipt. It is not stored in PSD1 because it
does not describe PowerShell gallery installation requirements. It describes
the embedded payload layout that PSPublishModule owns.

Transitive `RequiredModules` discovered from embedded modules are included and
ordered dependency-first in the receipt. That matters for Microsoft Graph, Az,
and similar module families where a leaf module often requires an accounts or
authentication module.

## Installing A Private Runtime

After the built module is installed from PSGallery or a private feed, deploy the
module plus embedded dependencies to an explicit folder:

```powershell
Install-ManagedModule `
    -Name Company.Tools `
    -ProfileName JFrogPS `
    -InstallPrerequisites

Install-ModuleDependency `
    -Name Company.Tools `
    -RequiredVersion 1.0.0 `
    -Path C:\PrivateDeps
```

Use raw `Install-Module` only for the generic public-gallery path. For private
feeds, prefer `Install-ManagedModule` so repository registration, credentials,
profiles, and prerequisites follow the PSPublishModule private-gallery contract.

The destination becomes:

```text
C:\PrivateDeps\Company.Tools\1.0.0
C:\PrivateDeps\Microsoft.Graph.Authentication\2.25.0
C:\PrivateDeps\module-dependencies.json
```

By default, existing folders are merged: missing files are copied while existing
files are preserved. Use `-Force` when the existing files should be overwritten.
Use `-DependencyName` to refresh selected dependencies; the receipt keeps
unselected dependencies that are already part of the private runtime.

`Install-ModuleDependency` does not install into the AllUsers or CurrentUser
module store unless the chosen path is already one of those stores. It is a file
deployment step for an explicit runtime folder.

## Importing A Private Runtime

Import the private runtime by path:

```powershell
Import-ModuleDependency -Name Company.Tools -Path C:\PrivateDeps
```

This reads `C:\PrivateDeps\module-dependencies.json`, imports dependency entries
in receipt order, then imports the root module by exact path.

You can also import directly from the installed module's embedded payload:

```powershell
Import-ModuleDependency -Name Company.Tools
```

That reads the embedded receipt from the installed `Company.Tools` module and
imports only the embedded dependencies. The root module can then be imported
normally:

```powershell
Import-Module Company.Tools
```

For the most deterministic private-runtime flow, keep both steps pointed at the
same explicit path:

```powershell
Install-ModuleDependency -Name Company.Tools -Path C:\PrivateDeps
Import-ModuleDependency  -Name Company.Tools -Path C:\PrivateDeps
```

## What This Does Not Isolate

`RequiredModule` is not isolation. It uses normal PowerShell dependency
resolution from installed modules and `PSModulePath`.

`EmbeddedModule` plus `Import-ModuleDependency -Path` gives deterministic
path-based imports for the bundled dependency payloads. It does not unload
already-loaded conflicting modules or assemblies from the current session.

For Microsoft Graph, Az, ExchangeOnlineManagement, and other assembly-heavy
families, the safest operator pattern is:

1. start a fresh PowerShell session
2. call `Import-ModuleDependency -Name <Module> -Path <PrivateRoot>`
3. avoid preloading other versions of the same dependency family in that session

If the environment is already polluted with incompatible modules or assemblies,
use a fresh process or a stricter isolated import strategy instead of expecting
normal `Import-Module` semantics to undo that state.

## Choosing The Model

Use `RequiredModule` when:

- the dependency should appear in the module manifest
- gallery installers should fetch it
- private-gallery mirroring should promote it
- normal PowerShell dependency resolution is acceptable

Use `ExternalModule` when:

- the estate owns installation separately
- the package should advertise an external dependency without forcing
  `RequiredModules`
- build-time install/check is still useful

Use `EmbeddedModule` when:

- the dependency should not appear in PSD1 requirements
- the module should carry exact dependency payloads
- operators need explicit-path install/import into a private runtime folder
- avoiding random module-store resolution is more important than gallery-native
  dependency management

Use `ApprovedModule` when:

- the module is a build helper
- selected functions may be merged
- there should be no runtime dependency declaration
