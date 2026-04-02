---
topic: about_ModuleDependencies
schema: 1.0.0
---
# about_ModuleDependencies

## Short Description

Explains how module dependencies are declared, resolved, installed, and packaged in PSPublishModule builds.

## Long Description

PSPublishModule separates dependency authoring into three related concerns:

1. Declaring what the module depends on.
2. Deciding whether missing dependencies should be installed for the build host.
3. Deciding whether required dependencies should be bundled into a build artefact.

DECLARING DEPENDENCIES

Use New-ConfigurationModule to describe dependencies.

RequiredModule
Written to the manifest RequiredModules collection.
Can be installed during the build when InstallMissingModules is enabled.
Can be bundled into artefacts when AddRequiredModules is enabled.

ExternalModule
Written to PrivateData.PSData.ExternalModuleDependencies.
Can be installed during the build when InstallMissingModules is enabled.
Is not bundled into artefacts.
Use this when the target machine is expected to install the dependency separately.

ApprovedModule
Not written as a manifest dependency.
Used by merge and missing-function workflows so selected helper functions can be copied into the built module.
Use this for helper libraries that should contribute functions, not for runtime dependency declaration.

VERSION EXPECTATIONS

You can describe version intent in one of two ways:

- Minimum version using -Version or -MinimumVersion
- Exact version using -RequiredVersion

Do not mix a minimum version with RequiredVersion for the same dependency.

The values Auto and Latest are supported for version discovery scenarios.
By default those values resolve from modules already installed on the build machine.

ONLINE RESOLUTION WITHOUT INSTALLING

If you want PowerForge to resolve Auto or Latest from a repository instead of the local machine, enable:

New-ConfigurationBuild -ResolveMissingModulesOnline

This is useful when your CI runner or fresh workstation does not already have the dependency installed, but you
still want the manifest to resolve to a concrete version.

INSTALLING MISSING DEPENDENCIES FOR THE BUILD

If the build itself needs the module on the machine, enable:

New-ConfigurationBuild -InstallMissingModules

This build-time installation covers both RequiredModule and ExternalModule entries.

Related options:

- InstallMissingModulesRepository
Repository name used for installation. Defaults to PSGallery.

- InstallMissingModulesForce
Reinstalls or updates even if a matching dependency is already present.

- InstallMissingModulesPrerelease
Allows prerelease versions when resolving and installing dependencies.

- InstallMissingModulesCredentialUserName
- InstallMissingModulesCredentialSecret
- InstallMissingModulesCredentialSecretFilePath
Use these when the repository requires credentials or a token.

BUNDLING DEPENDENCIES INTO ARTEFACTS

If you want the output artefact to contain the dependency modules, configure:

New-ConfigurationArtefact -AddRequiredModules

This packaging step only bundles RequiredModule dependencies. ExternalModule entries are intentionally excluded.

HOW REQUIREDMODULESSOURCE WORKS

RequiredModulesSource controls where AddRequiredModules gets modules from:

Installed
Copy only from modules already available on the machine.
This is the default.
If the dependency is missing locally, artefact creation fails.

Auto
Prefer local modules first.
If a module is missing locally, PowerForge attempts to download it.

Download
Always download required modules for packaging, even if a local copy exists.

RequiredModulesRepository and RequiredModulesTool control where and how the package download occurs.
The tool selection is:

- Auto
- PSResourceGet
- PowerShellGet

Auto prefers PSResourceGet and falls back to PowerShellGet when needed.

COMMON EXPECTATIONS

Use RequiredModule when:
- the dependency should appear in the manifest
- the dependency may need to be bundled for offline or self-contained artefacts

Use ExternalModule when:
- the dependency should be installed on the consumer machine separately
- you do not want it copied into packaged artefacts

Use ApprovedModule when:
- you want merge-time reuse of helper functions
- you do not want a manifest dependency entry

TROUBLESHOOTING

If installation or download fails:

- Verify the repository exists with Get-PSRepository.
- If PSGallery is missing, run Register-PSRepository -Default.
- If a private feed is used, verify credentials and repository registration first.
- If PSResourceGet cannot be used in the current environment, PowerForge can fall back to PowerShellGet in Auto mode.

## Examples

```text
PS> Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {
>>     New-ConfigurationModule -Type RequiredModule -Name 'Pester' -Version 'Latest' -Guid 'Auto'
>>     New-ConfigurationBuild -Enable -InstallMissingModules -ResolveMissingModulesOnline
>> }

Declares a required dependency, resolves its version online when needed, and installs it before the build.

PS> Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {
>>     New-ConfigurationModule -Type RequiredModule -Name 'PSWriteColor' -RequiredVersion '1.0.0'
>>     New-ConfigurationArtefact -Type Packed -Enable -AddRequiredModules -RequiredModulesSource Download -RequiredModulesRepository 'PSGallery'
>> }

Builds a packed artefact and always downloads the required module into the package instead of relying on a local copy.

PS> Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {
>>     New-ConfigurationModule -Type ExternalModule -Name 'Az.Accounts' -Version 'Latest'
>>     New-ConfigurationBuild -Enable -InstallMissingModules
>> }

Installs the dependency for the build host, but keeps it out of packaged artefacts.
```

## Notes

This file is source content for generated module documentation.

