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

`EmbeddedModule` is the private-runtime contract. Use it when the module should
carry selected dependency payloads without adding those modules to PSD1
`RequiredModules`.

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
Install-Module Company.Tools -RequiredVersion 1.0.0

Install-ModuleDependency `
    -Name Company.Tools `
    -RequiredVersion 1.0.0 `
    -Path C:\PrivateDeps
```

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

