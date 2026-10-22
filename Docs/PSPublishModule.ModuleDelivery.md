# Runnable module delivery bundles

Use `New-ConfigurationArtefact -Layout Delivery` to create a deployment folder or
ZIP containing launcher and configuration files at its root. This layout supports
`Unpacked` and `Packed`; the module remains a module with its manifest and runtime
assets. `Script` and `ScriptPacked` generate script entry points and use their own
layout instead.

Declare delivery content through `New-ConfigurationDelivery`. The artefact copies
the packaged content from its configured `InternalsPath` to the bundle root and
places the main module and remaining dependencies under `Modules` by default:

```text
Run.ps1
config.json
Modules/
  DemoTool/
    DemoTool.psd1
    DemoTool.psm1
    Files/...
  RemainingDependency/...
```

The complete original module payload stays under `Modules/DemoTool`, including
its delivery internals. This preserves module-relative assets and the generated
install/update commands. Root delivery files are copies of those packaged files;
the builder does not move or modify the signed source payload.

## Configuration

Inside the normal module build configuration:

```powershell
New-ConfigurationBuild -Enable -MergeModuleOnBuild -MergeFunctionsFromApprovedModules `
    -SignModule:$SignModule -CertificateThumbprint $CertificateThumbprint

New-ConfigurationDelivery -Enable -InternalsPath 'Files' -Sign:$SignModule `
    -GenerateInstallCommand -GenerateUpdateCommand

New-ConfigurationArtefact -Type Unpacked -Layout Delivery -Enable `
    -Path 'Artefacts/Unpacked' -AddRequiredModules -RequiredModulesSource Installed

New-ConfigurationArtefact -Type Packed -Layout Delivery -Enable `
    -Path 'Artefacts/Packed' -AddRequiredModules -RequiredModulesSource Installed `
    -IncludeTagName -ID 'VersionedBundle'

New-ConfigurationArtefact -Type Packed -Layout Delivery -Enable `
    -Path 'Artefacts/Packed' -AddRequiredModules -RequiredModulesSource Installed `
    -ArtefactName 'DemoTool.zip' -ID 'CurrentBundle'
```

The two packed declarations produce a versioned ZIP and a stable filename through
the native artefact builder. No post-build `Move-Item` or `Compress-Archive` is
needed. Use relative `ModulesPath` and `RequiredModulesPath` only when a different
module subdirectory is required. Delivery files cannot overwrite those module
directories. Layout validation runs before existing output is cleared.

Use one build declaration and pass the selected certificate thumbprint as build
configuration. Certificate enumeration and interactive selection do not belong
before the builder: unsigned builds need no certificate, and unattended builds
need an explicit signing identity. Keep delivery and module signing controlled by
the same operator setting when both are intended to be signed.

## Publishing to private repositories

Artefact layout is independent of repository publication. `Build` can produce
the bundle locally; `Publish` can additionally publish the module to its configured
repository, including Artifactory through PSResourceGet. Configure the build's
run mode once through `Build-Module -RunMode $RunMode` or `New-ConfigurationGate`.

Use supported deferred credential sources rather than reading a secret in the
wrapper:

```powershell
New-ConfigurationPublish -Type PowerShellGallery -Tool PSResourceGet `
    -JFrogBaseUri 'https://packages.example.com/artifactory' `
    -JFrogRepository 'powershell-local' -RepositoryName 'CompanyModules' `
    -RepositoryCredentialUserName 'build-user' `
    -RepositoryCredentialSecretEnvironmentVariable 'ARTIFACTORY_TOKEN' -Enabled
```

The private repository receives the normal module package. A deployment ZIP is a
separate artefact for the deployment system; it is not an alternative NuGet feed
format. Required-module bundling uses the configured installed/download source
and does not change which repository receives the published module.
